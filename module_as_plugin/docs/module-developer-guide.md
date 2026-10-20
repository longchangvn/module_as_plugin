# Module Developer Guide

This guide covers how to create, run, publish and install a new **module** (plugin) on the
Module-as-Plugin platform. Run every command from the `module_as_plugin/` folder unless a
step says otherwise.

- Starter kit / CLI: [`tools/create-module/`](../tools/create-module/)
- Templates (the source of truth): [`tools/create-module/templates/`](../tools/create-module/templates/)
- Reference modules: [`src/Modules/Sample1/Sample1.Api`](../src/Modules/Sample1/Sample1.Api) + [`modules/sample1/`](../modules/sample1/)
- Platform overview: [README.md](../README.md), local setup: [SETUP.md](../SETUP.md)

## Contents

1. [Overview](#1-overview)
2. [Prerequisites](#2-prerequisites)
3. [Quick start (5 minutes)](#3-quick-start-5-minutes)
4. [What gets generated](#4-what-gets-generated)
5. [Manual step-by-step (without the CLI)](#5-manual-step-by-step-without-the-cli)
6. [manifest.yml reference](#6-manifestyml-reference)
7. [Extending your module](#7-extending-your-module)
8. [Run, publish, install and verify](#8-run-publish-install-and-verify)
9. [Rules and conventions](#9-rules-and-conventions)
10. [Troubleshooting](#10-troubleshooting)
11. [Pre-PR checklist](#11-pre-pr-checklist)

---

## 1. Overview

A module has three parts. Each one can be built, deployed and versioned on its own:

| Part | Technology | Location | Role |
|------|------------|----------|------|
| Module API | .NET 8 minimal API | `src/Modules/<Pascal>/<Pascal>.Api/` | Business endpoints, plus the template endpoints `/healthz`, `/readyz`, `/internal/seed:preview`, `/internal/migrate` |
| Federation remote | React 18 + Vite 5 + `@originjs/vite-plugin-federation` | `modules/<name>/web/` | Builds `remoteEntry.js`, which exposes pages (`./Page`) to the host |
| Contract | `manifest.yml` | `modules/<name>/manifest.yml` | Identity, version, dependencies, frontend routes/menus, gateway routes, tenant seed (roles/permissions) |

Only the platform reads `manifest.yml`. The browser never sees it. Tenant Admin (`:5175`) acts as
the Module Federation **host**. At runtime it calls `GET /api/v1/tenant/frontend-bootstrap` to
get the installed modules (scope, `remoteUrl`, routes, menus) and then loads each remote
([`apps/tenant-admin/src/lib/remotes.ts`](../apps/tenant-admin/src/lib/remotes.ts), which uses
`setRemote(scope, { url, format: 'esm', from: 'vite' })` and then `getRemote(scope, exposes)`).

### Architecture

```text
                      +-------------------------------------------+
  Browser  ---------> | Tenant Admin host (:5175, Vite)           |
                      |  1. GET /api/v1/tenant/frontend-bootstrap |----+
                      |  2. load remoteUrl  ---------------------+|    |
                      +------------------------------------------||    |
                                                                 ||    |
           +-----------------------------------------------------+|    |
           v                                                      |    |
  +-----------------------------------+                           |    |
  | Module remote (:5178, vite preview)|                          |    |
  |  /assets/remoteEntry.js -> ./Page |                           |    |
  +-----------------------------------+                           |    |
           | Page calls relative /api/v1/<name>/... (host proxy)  |    |
           v                                                      v    v
  +---------------------------------------------------------------------+
  | Gateway (Ocelot) :5100                                              |
  |  /api/v1/<name>/{rest}  -> <artifactApiUrl>/api/v1/{rest}           |
  |  disabled module -> 503 MODULE_DISABLED, not installed -> 404       |
  +---------------------------------------------------------------------+
           |                               |                     |
           v                               v                     v
  +--------------------+      +------------------------+   +---------------+
  | Module API :5204   | ---> | Platform :5121         |   | Auth :5122    |
  | JWT + ModuleGuards |      | /internal/tenants/{id}/|   | permissions/  |
  |                    | ---> |   status (tenant usable)|  | resolve       |
  +--------------------+      +------------------------+   +---------------+
```

### Install lifecycle

When a platform Admin installs a module for a tenant
(`POST /api/v1/platform/tenants/{tid}/modules/{id}/install`), the platform runs:

```text
validate manifest -> resolve dependencies (topo-sort, ranges) -> INSTALLING
  -> apply gateway routes -> apply tenant seed (roles, permissions, menus, users)
  -> INSTALLED + ENABLED
```

A per-(tenant, module) lock serialises lifecycle operations. Uninstall revokes the seed before it
removes gateway routes. Other operations: enable, disable, upgrade, uninstall.

---

## 2. Prerequisites

### Tools

| Tool | Version | Used for | Download |
|------|---------|----------|----------|
| .NET 8 SDK | 8.0.x | Module APIs, platform APIs, tests (`net8.0`) | https://dotnet.microsoft.com/download/dotnet/8.0 |
| .NET 9 SDK | 9.0.x | Aspire AppHost (`net9.0`, `Aspire.AppHost.Sdk` 9.5.2 restored from NuGet; no `dotnet workload install` needed) | https://dotnet.microsoft.com/download/dotnet/9.0 |
| Node.js (includes npm) | 20 LTS or newer (the CLI needs at least 18) | Federation remotes, host apps, `create-module` CLI | https://nodejs.org/en/download |
| Docker Desktop | current | Postgres (Aspire container or `docker compose`), container builds | https://www.docker.com/products/docker-desktop/ |
| Git | any recent | Source control | https://git-scm.com/downloads |
| jq (optional) | 1.6+ | Parsing JSON in the curl examples | https://jqlang.github.io/jq/download/ |
| VS Code + C# Dev Kit (optional) | current | Editing and debugging | https://code.visualstudio.com/ , https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit |
| Postman (optional) | current | Collection at [`postman/ModulePlatform.postman_collection.json`](../postman/ModulePlatform.postman_collection.json) | https://www.postman.com/downloads/ |

Check your setup:

```bash
dotnet --list-sdks   # expect 8.0.x and 9.0.x
node --version       # v20+ recommended
npm --version
docker --version
```

### NuGet packages (module API)

These come from [`templates/api/__PASCAL__.Api.csproj.template`](../tools/create-module/templates/api/__PASCAL__.Api.csproj.template) (stored with a `.template` suffix so IDEs don't restore it in place; the CLI drops the suffix). `dotnet restore` (or `dotnet build`) restores them automatically.

| Package / reference | Version | Link |
|---------------------|---------|------|
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 8.0.11 | https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.JwtBearer/8.0.11 |
| `Microsoft.AspNetCore.OpenApi` | 8.0.11 | https://www.nuget.org/packages/Microsoft.AspNetCore.OpenApi/8.0.11 |
| `Swashbuckle.AspNetCore` | 6.6.2 | https://www.nuget.org/packages/Swashbuckle.AspNetCore/6.6.2 |
| Project ref `SharedKernel` | in repo | [`src/Shared/SharedKernel`](../src/Shared/SharedKernel) (manifest models, validator, `ModuleGuards`, error codes) |
| Project ref `ModulePlatform.ServiceDefaults` | in repo | [`src/Aspire/ModulePlatform.ServiceDefaults`](../src/Aspire/ModulePlatform.ServiceDefaults) (`AddServiceDefaults`, `RequireSecret`, `/health`, OTel) |

### npm packages (federation remote)

These come from [`templates/module/web/package.json`](../tools/create-module/templates/module/web/package.json). `npm install` restores them automatically (`create-module new` runs it unless you pass `--skip-install`).

| Package | Version range | Type | Link |
|---------|---------------|------|------|
| `react` | ^18.3.1 | dependency | https://www.npmjs.com/package/react |
| `react-dom` | ^18.3.1 | dependency | https://www.npmjs.com/package/react-dom |
| `@originjs/vite-plugin-federation` | ^1.3.9 | dev | https://www.npmjs.com/package/@originjs/vite-plugin-federation |
| `@types/react` | ^18.3.12 | dev | https://www.npmjs.com/package/@types/react |
| `@types/react-dom` | ^18.3.1 | dev | https://www.npmjs.com/package/@types/react-dom |
| `@vitejs/plugin-react` | ^4.3.4 | dev | https://www.npmjs.com/package/@vitejs/plugin-react |
| `typescript` | ^5.7.2 | dev | https://www.npmjs.com/package/typescript |
| `vite` | ^5.4.11 | dev | https://www.npmjs.com/package/vite |

### Starter kit

You do not need to download a separate zip. The starter kit is the
[`tools/create-module/`](../tools/create-module/) folder in this repo: a zero-dependency Node CLI
plus its templates. To give the kit to another team, run:

```bash
node tools/create-module/create-module.mjs pack          # -> dist/create-module-kit.zip
node tools/create-module/create-module.mjs pack --out /tmp/kit.zip
```

---

## 3. Quick start (5 minutes)

### 3.1 CLI usage

```bash
node tools/create-module/create-module.mjs <command> [options]
# alternatives:
npx ./tools/create-module <command> ...
cd tools/create-module && npm link     # then: create-module <command> ...
```

| Command | Purpose |
|---------|---------|
| `new <name>` | Scaffold the API, federation remote, manifest and parity test, then wire them into the repo |
| `list` | List modules found under `modules/` with their API/web ports |
| `publish <name>` | Register the manifest: `POST {gateway}/api/v1/platform/modules {manifestYaml, artifactApiUrl}` |
| `install <name> --tenant <id> [--version x.y.z]` | `POST {gateway}/api/v1/platform/tenants/{id}/modules/{name}/install` |
| `remove <name> [-y]` | Delete the module's generated folders and undo the wiring (asks for confirmation unless `-y` is set) |
| `pack [--out <file>]` | Zip the starter kit (default `dist/create-module-kit.zip`) |

Options for `new`:

| Option | Default |
|--------|---------|
| `--display-name <text>` | Title Case of the name (`store-ops` becomes `Store Ops`) |
| `--description <text>` | `"<Display Name> module."` |
| `--owner <email>` | `platform-team@company.com` |
| `--category <slug>` | `ops` |
| `--icon <name>` | `box` |
| `--api-port <n>` | Next free port >= 5204 (found by scanning launchSettings, vite configs and compose) |
| `--web-port <n>` | Next free port >= 5178 |
| `--perm-prefix <CODE>` | Derived from the name, with digits spelled out (`sample3` becomes `SAMPLE_THREE`, `store-ops` becomes `STORE_OPS`) |
| `--menu-order <n>` | `50` |
| `--no-wire` | Only generate files. Skip the sln, AppHost, compose and test csproj edits |
| `--skip-install` | Skip `npm install` in `modules/<name>/web` |
| `--dry-run` | Print the plan and write nothing |
| `-y`, `--yes` | No interactive prompts. Without it, the CLI prompts for missing values when it runs in a TTY |

Options for `publish` and `install`:

| Option | Default |
|--------|---------|
| `--gateway <url>` | `http://localhost:5100` (env `MODULE_GATEWAY`) |
| `--token <jwt>` | Platform Admin JWT (env `PLATFORM_TOKEN`) |
| `--email <email>` / `--password <pw>` | Use these instead of `--token` to log in via `POST /api/v1/platform/auth/login`. Email defaults to `admin@local`; password comes from env `PLATFORM_PASSWORD` |
| `--api-url <url>` (publish only) | `http://localhost:<port from the module's launchSettings.json>` |

Name rules: the name must match `^[a-z][a-z0-9-]{2,49}$`, must not be reserved (`admin`, `api`,
`platform`, `auth`, `gateway`, `t`, `health`, `metrics`, `docs`, `login`, `static`), and must
not already exist.

### 3.2 Create, run, publish, install

```bash
# 1. Scaffold + wire (preview first with --dry-run if you like)
node tools/create-module/create-module.mjs new inventory --display-name "Inventory" -y

# 2a. Run everything with Aspire (Postgres + platform + all modules incl. inventory + inventory-web)
dotnet run --project src/Aspire/ModulePlatform.AppHost

# 2b. ...or run only the new module next to an already-running core stack
dotnet run --project src/Modules/Inventory/Inventory.Api                 # :5204
(cd modules/inventory/web && npm install && npm run serve)               # build + preview :5178

# 3. Build and test (the generated parity test runs here)
dotnet build ModulePlatform.sln
dotnet test ModulePlatform.sln
(cd modules/inventory/web && npx tsc --noEmit)

# 4. Publish to the registry (Development: admin@local accepts any password on first login)
PLATFORM_PASSWORD=admin node tools/create-module/create-module.mjs publish inventory

# 5. Install for a tenant (the dev seed creates tnt_demo; or use your own tenant id)
PLATFORM_PASSWORD=admin node tools/create-module/create-module.mjs install inventory --tenant tnt_demo
```

6. Verify: sign in to Tenant Admin at http://localhost:5175 as a tenant user. Go to **Home**, open
   **Inventory**, and click **Get sample data**. The **Get protected data** button stays disabled
   until you assign the **Inventory Viewer** role (Users, then your user, then Roles) and sign in
   again.

---

## 4. What gets generated

For `new inventory` (Pascal `Inventory`, API `:5204`, web `:5178`):

```text
src/Modules/Inventory/Inventory.Api/          <- templates/api/
  Inventory.Api.csproj                        net8.0 web project, NuGet + SharedKernel/ServiceDefaults refs
  Program.cs                                  Host: service defaults, Swagger, JWT bearer, HttpClients "platform"/"auth"
  Endpoints/ModuleEndpoints.cs                ModuleId/Version/permission constants, Seed* arrays, all endpoints
  Models/Requests.cs                          MigrateRequest record
  Properties/launchSettings.json              "http" profile on :5204, Development
  appsettings.json                            Logging
  appsettings.Development.json                Platform:BaseUrl (:5121), Auth:BaseUrl (:5122)
  Dockerfile                                  Multi-stage sdk:8.0 -> aspnet:8.0 (build context = repo root)
  Inventory.Api.http                          Ready-made requests (health, seed preview, gateway calls)

modules/inventory/                            <- templates/module/
  manifest.yml                                The platform contract (section 6)
  README.md                                   Module-specific run/publish/test notes
  web/
    package.json                              Scripts dev/build/preview/serve/typecheck
    vite.config.ts                            Federation remote: name inventory_remote, exposes ./Page
    tsconfig.json                             Strict TS, bundler resolution
    index.html                                Standalone dev page (mounts dev-main.tsx)
    Dockerfile                                node:20-alpine build -> nginx:alpine
    src/api.ts                                fetch wrapper: host session token + X-Tenant-Id, error mapping
    src/Page.tsx                              Exposed page: sample + protected data, permission-aware button
    src/dev-main.tsx                          Dev-only entry (not used by the host)
    src/page.css                              Self-contained styles prefixed .mod-inventory-

tests/SharedKernel.Tests/Modules/
  InventoryModuleTests.cs                     <- templates/test/: manifest validation + identity + seed parity
```

Wiring edits made by `new` (skipped with `--no-wire`):

| File | Edit |
|------|------|
| `ModulePlatform.sln` | `dotnet sln ModulePlatform.sln add src/Modules/Inventory/Inventory.Api/Inventory.Api.csproj` |
| `src/Aspire/ModulePlatform.AppHost/ModulePlatform.AppHost.csproj` | `ProjectReference` to the API |
| `src/Aspire/ModulePlatform.AppHost/Program.cs` | `AddProject` + `AddNpmApp("inventory-web", ..., "serve")` block before `builder.Build().Run();` |
| `docker-compose.yml` | `inventory` and `inventory-web` services before `volumes:` |
| `tests/SharedKernel.Tests/SharedKernel.Tests.csproj` | `ProjectReference` to the API (for the parity test) |

---

## 5. Manual step-by-step (without the CLI)

The code below is the template rendered with these values:

| Token | Value |
|-------|-------|
| `__MODULE_NAME__` | `inventory` |
| `__PASCAL__` | `Inventory` |
| `__DISPLAY_NAME__` | `Inventory` |
| `__DESCRIPTION__` | `Inventory module.` |
| `__OWNER__` | `platform-team@company.com` |
| `__CATEGORY__` / `__ICON__` | `ops` / `box` |
| `__API_PORT__` / `__WEB_PORT__` | `5204` / `5178` |
| `__SCOPE__` | `inventory_remote` |
| `__PERM__` | `INVENTORY` |
| `__CSS__` | `mod-inventory` |
| `__MENU_ORDER__` | `50` |

If you use different values, replace them consistently. The templates in
`tools/create-module/templates/` are authoritative. If this guide and the templates ever
disagree, the templates win.

### Step 1: API project file

`src/Modules/Inventory/Inventory.Api/Inventory.Api.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.11" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="8.0.11" />
    <PackageReference Include="Swashbuckle.AspNetCore" Version="6.6.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\Shared\SharedKernel\SharedKernel.csproj" />
    <ProjectReference Include="..\..\..\Aspire\ModulePlatform.ServiceDefaults\ModulePlatform.ServiceDefaults.csproj" />
  </ItemGroup>

</Project>
```

### Step 2: Program.cs

`src/Modules/Inventory/Inventory.Api/Program.cs`

```csharp
// Inventory API :5204 — scaffolded by tools/create-module (module template §10.5, M-BR1).
// Template endpoints: GET /healthz, GET /readyz, POST /internal/seed:preview,
// POST /internal/migrate. Business: GET /api/v1/data (any tenant user),
// GET /api/v1/protected (INVENTORY_DATA_PROTECTED, enforced via Auth resolve).
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModulePlatform.ServiceDefaults;
using Inventory.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwtSecret = builder.RequireSecret("Jwt:SecretKey", Extensions.DevJwtSecret);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew = TimeSpan.FromMinutes(1),
    };
});
builder.Services.AddAuthorization();

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("platform", c => c.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddHttpClient("auth", c => c.Timeout = TimeSpan.FromSeconds(5));

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

ModuleEndpoints.Map(app);

app.Run();

public partial class Program;
```

### Step 3: ModuleEndpoints.cs

`src/Modules/Inventory/Inventory.Api/Endpoints/ModuleEndpoints.cs`

```csharp
using Microsoft.Extensions.Caching.Memory;
using Inventory.Api.Models;
using SharedKernel;

namespace Inventory.Api.Endpoints;

public static class ModuleEndpoints
{
    public const string ModuleId = "inventory";
    public const string Version = "1.0.0";
    public const string ViewPermission = "INVENTORY_DATA_VIEW";
    public const string ProtectedPermission = "INVENTORY_DATA_PROTECTED";

    // Must equal manifest.yml api.seed / frontend.menus (InventoryModuleTests parity).
    public static readonly string[] SeedRoles = ["Inventory Viewer"];
    public static readonly string[] SeedPermissions = [ViewPermission, ProtectedPermission];
    public static readonly string[] SeedMenus = ["inventory"];

    public static void Map(WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Json(new { status = "ok", module = ModuleId, version = Version }));
        app.MapGet("/readyz", () => Results.Json(new { ready = true, checks = new { db = "n/a", dependencies = "ok" } }));

        app.MapPost("/internal/seed:preview", () => Results.Json(new
        {
            data = new { roles = SeedRoles, permissions = SeedPermissions, menus = SeedMenus },
        }));

        // Additive migrations only in v1 (no module data store).
        app.MapPost("/internal/migrate", (MigrateRequest req) =>
            req.FromVersion == req.ToVersion
                ? Results.Json(new { data = new { migrated = false } })
                : Results.Json(new { data = new { migrated = true, from = req.FromVersion, to = req.ToVersion } }));

        // Open endpoint: any signed-in user of an active tenant.
        app.MapGet("/api/v1/data", async (HttpContext ctx, IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg) =>
            await ModuleGuards.RequireTenantAsync(ctx, http, cache, cfg)
                ?? Results.Json(new { data = Content("sample", ModuleGuards.TenantId(ctx)!) }))
            .RequireAuthorization();

        // Permission-gated endpoint: re-checked on every call via Auth resolve (fail closed).
        app.MapGet("/api/v1/protected", async (HttpContext ctx, IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg) =>
        {
            if (await ModuleGuards.RequireTenantAsync(ctx, http, cache, cfg) is { } tenantError) return tenantError;
            var tenantId = ModuleGuards.TenantId(ctx)!;
            return await ModuleGuards.RequirePermissionAsync(ctx, http, cfg, tenantId, ProtectedPermission)
                ?? Results.Json(new { data = Content("protected", tenantId) });
        }).RequireAuthorization();
    }

    private static object Content(string kind, string tenantId) => new
    {
        module = ModuleId, version = Version, kind,
        generatedAt = DateTimeOffset.UtcNow.ToString("O"), tenantId,
        items = Items(kind),
    };

    private static object[] Items(string kind)
    {
        var suffix = kind == "protected" ? " (protected)" : "";
        return
        [
            new { id = "inventory-1", name = $"Inventory · Alpha{suffix}", value = 10 },
            new { id = "inventory-2", name = $"Inventory · Beta{suffix}", value = 20 },
            new { id = "inventory-3", name = $"Inventory · Gamma{suffix}", value = 30 },
        ];
    }
}
```

### Step 4: Models/Requests.cs

`src/Modules/Inventory/Inventory.Api/Models/Requests.cs`

```csharp
namespace Inventory.Api.Models;

public sealed record MigrateRequest(string FromVersion, string ToVersion);
```

### Step 5: launchSettings.json

`src/Modules/Inventory/Inventory.Api/Properties/launchSettings.json`

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5204",
      "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" }
    }
  }
}
```

### Step 6: appsettings

`src/Modules/Inventory/Inventory.Api/appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Information"
    }
  },
  "AllowedHosts": "*"
}
```

`src/Modules/Inventory/Inventory.Api/appsettings.Development.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Information"
    }
  },
  "Platform": {
    "BaseUrl": "http://localhost:5121"
  },
  "Auth": {
    "BaseUrl": "http://localhost:5122"
  }
}
```

Do not put `Jwt:SecretKey` in appsettings. In Development, `RequireSecret` falls back to the
built-in dev key. In every other environment, supply it through the environment variable
`Jwt__SecretKey` (at least 32 characters).

### Step 7: API Dockerfile

`src/Modules/Inventory/Inventory.Api/Dockerfile` (build context = repo root)

```dockerfile
# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ModulePlatform.sln ./
COPY src/Aspire/ModulePlatform.ServiceDefaults/ModulePlatform.ServiceDefaults.csproj src/Aspire/ModulePlatform.ServiceDefaults/
COPY src/Shared/SharedKernel/*.csproj src/Shared/SharedKernel/
COPY src/Modules/Inventory/Inventory.Api/*.csproj src/Modules/Inventory/Inventory.Api/
RUN dotnet restore src/Modules/Inventory/Inventory.Api/Inventory.Api.csproj
COPY . .
RUN dotnet publish src/Modules/Inventory/Inventory.Api/Inventory.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Inventory.Api.dll"]
```

Optionally, copy `templates/api/__PASCAL__.Api.http` to `Inventory.Api.http` and render its
tokens. It contains ready-made requests for VS Code / Rider.

### Step 8: manifest.yml

`modules/inventory/manifest.yml`

```yaml
# manifest.yml — Inventory (scaffolded by tools/create-module).
manifestVersion: "1.0"

module:
  name: inventory
  displayName: Inventory
  description: "Inventory module."
  version: 1.0.0
  owner: platform-team@company.com
  license: proprietary
  icon: box
  category: ops

dependencies: []

service:
  healthCheck: /healthz
  readinessCheck: /readyz
  openapiUrl: /swagger/v1/swagger.json

frontend:
  remoteUrl: http://localhost:5178/assets/remoteEntry.js
  scope: inventory_remote
  modulePrefix: /inventory
  routes:
    - path: /inventory
      exposes: ./Page
      requiredPermission: null
      menuId: inventory
  menus:
    - id: inventory
      parentId: null
      title: Inventory
      icon: box
      route: /inventory
      requiredPermission: null
      order: 50
  uiPermissions: [INVENTORY_DATA_PROTECTED]

api:
  routes:
    - upstream: /api/v1/inventory/{rest}
      downstream: /api/v1/{rest}
      methods: [GET]
      auth: true
      requiredPermission: null
      priority: 10
  seed:
    roles:
      - name: Inventory Viewer
        description: Can view Inventory protected data
        isSystem: true
    permissions:
      - code: INVENTORY_DATA_VIEW
        object: INVENTORY_DATA
        action: VIEW
        name: View Inventory data
        isMenuAccess: true
      - code: INVENTORY_DATA_PROTECTED
        object: INVENTORY_DATA
        action: PROTECTED
        name: View Inventory protected data
        isMenuAccess: false
    menus: []
    userRoles: []
    rolePermissions:
      - roleName: Inventory Viewer
        permissions: [INVENTORY_DATA_VIEW, INVENTORY_DATA_PROTECTED]
    userAdditionalPermissions: []
    users: []
    gatewayRules: []

upgrade:
  notes: "Initial release."
  migrateEndpoint: /internal/migrate
```

### Step 9: Web package.json

`modules/inventory/web/package.json`

```json
{
  "name": "@modules/inventory-web",
  "private": true,
  "version": "1.0.0",
  "type": "module",
  "scripts": {
    "dev": "vite --port 5178",
    "build": "tsc --noEmit && vite build",
    "preview": "vite preview --port 5178 --strictPort",
    "serve": "npm run build && npm run preview",
    "typecheck": "tsc --noEmit"
  },
  "dependencies": {
    "react": "^18.3.1",
    "react-dom": "^18.3.1"
  },
  "devDependencies": {
    "@originjs/vite-plugin-federation": "^1.3.9",
    "@types/react": "^18.3.12",
    "@types/react-dom": "^18.3.1",
    "@vitejs/plugin-react": "^4.3.4",
    "typescript": "^5.7.2",
    "vite": "^5.4.11"
  }
}
```

### Step 10: vite.config.ts

`modules/inventory/web/vite.config.ts`

```ts
import react from '@vitejs/plugin-react';
import federation from '@originjs/vite-plugin-federation';
import { defineConfig } from 'vite';

// Module Federation remote for Inventory. Exposes ./Page (frontend.routes[].exposes).
// originjs remotes only work from a build: `npm run serve` = build + preview on :5178.
export default defineConfig({
  plugins: [
    react(),
    federation({
      name: 'inventory_remote',
      filename: 'remoteEntry.js',
      exposes: { './Page': './src/Page.tsx' },
      shared: ['react', 'react-dom'],
    }),
  ],
  server: { port: 5178 },
  preview: { port: 5178, strictPort: true, cors: true },
  build: { target: 'esnext', cssCodeSplit: false, minify: true },
});
```

### Step 11: tsconfig.json

`modules/inventory/web/tsconfig.json`

```json
{
  "compilerOptions": {
    "target": "ES2020",
    "lib": ["ES2020", "DOM", "DOM.Iterable"],
    "module": "ESNext",
    "moduleResolution": "bundler",
    "jsx": "react-jsx",
    "strict": true,
    "skipLibCheck": true,
    "esModuleInterop": true,
    "isolatedModules": true,
    "noEmit": true,
    "types": ["vite/client"]
  },
  "include": ["src", "vite.config.ts"]
}
```

### Step 12: index.html

`modules/inventory/web/index.html`

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Inventory Remote</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/dev-main.tsx"></script>
  </body>
</html>
```

### Step 13: src/api.ts

`modules/inventory/web/src/api.ts`

```ts
// Runs inside the host page (same origin as Tenant Admin): reuse its session
// (JWT in sessionStorage, SR-9) and call the gateway via relative /api/v1 paths.
const FRIENDLY: Record<string, string> = {
  MODULE_DISABLED: 'This module is disabled for your tenant.',
  UNKNOWN_MODULE_ROUTE: 'This module is not available (not installed).',
};

export async function call<T>(path: string): Promise<T> {
  let res: Response;
  try {
    res = await fetch(path, {
      headers: {
        Authorization: `Bearer ${sessionStorage.getItem('access_token') ?? ''}`,
        'X-Tenant-Id': sessionStorage.getItem('tenant_id') ?? '',
      },
    });
  } catch {
    throw new Error('Service unreachable.');
  }
  if (res.status === 401) {
    window.location.assign('/login');
    throw new Error('Session expired.');
  }
  const body = await res.json().catch(() => ({}));
  if (!res.ok) {
    const code: string | undefined = body?.error?.code;
    const message: string = (code && FRIENDLY[code]) ?? body?.error?.message ?? `HTTP ${res.status}`;
    throw new Error(code ? `${code}: ${message}` : message);
  }
  return body.data as T;
}
```

### Step 14: src/Page.tsx

`modules/inventory/web/src/Page.tsx`

```tsx
import { useEffect, useState } from 'react';
import { call } from './api';
import './page.css';

const TITLE = 'Inventory';
const VERSION = '1.0.0';
const API = '/api/v1/inventory';
const PROTECTED_PERMISSION = 'INVENTORY_DATA_PROTECTED';

type Result = { status: 'idle' } | { status: 'loading' } | { status: 'success'; data: unknown } | { status: 'error'; message: string };

function useFetcher(path: string) {
  const [result, setResult] = useState<Result>({ status: 'idle' });
  const run = async () => {
    setResult({ status: 'loading' });
    try {
      setResult({ status: 'success', data: await call(path) });
    } catch (e) {
      setResult({ status: 'error', message: (e as Error).message });
    }
  };
  return { result, run };
}

function Output({ result }: { result: Result }) {
  if (result.status === 'loading') return <p className="mod-inventory-muted">Loading…</p>;
  if (result.status === 'error') return <p role="alert" className="mod-inventory-error">{result.message}</p>;
  if (result.status === 'success') return <pre className="mod-inventory-json">{JSON.stringify(result.data, null, 2)}</pre>;
  return null;
}

export default function Page() {
  const sample = useFetcher(`${API}/data`);
  const secured = useFetcher(`${API}/protected`);
  const [permission, setPermission] = useState<'checking' | 'granted' | 'missing' | 'failed'>('checking');

  useEffect(() => {
    call<{ permissions?: string[] }>('/api/v1/auth/me')
      .then((me) => setPermission(me.permissions?.includes(PROTECTED_PERMISSION) ? 'granted' : 'missing'))
      .catch(() => setPermission('failed'));
  }, []);

  const hint =
    permission === 'missing' ? `Requires ${PROTECTED_PERMISSION}`
      : permission === 'failed' ? 'Permission check failed'
        : permission === 'checking' ? 'Checking permission…' : null;

  return (
    <div className="mod-inventory-page">
      <header className="mod-inventory-header">
        <h1>{TITLE}</h1>
        <span className="mod-inventory-badge">v{VERSION}</span>
      </header>

      <section className="mod-inventory-card">
        <h2>Sample data</h2>
        <p className="mod-inventory-muted">Available to every signed-in user of this tenant.</p>
        <button className="mod-inventory-button" onClick={sample.run} disabled={sample.result.status === 'loading'}>
          Get sample data
        </button>
        <Output result={sample.result} />
      </section>

      <section className="mod-inventory-card">
        <h2>Protected data</h2>
        <p className="mod-inventory-muted">Requires the {PROTECTED_PERMISSION} permission.</p>
        <button
          className="mod-inventory-button"
          onClick={secured.run}
          disabled={permission !== 'granted' || secured.result.status === 'loading'}
          title={hint ?? undefined}
        >
          Get protected data
        </button>
        {hint && <small className="mod-inventory-hint">{hint}</small>}
        <Output result={secured.result} />
      </section>
    </div>
  );
}
```

### Step 15: src/dev-main.tsx

`modules/inventory/web/src/dev-main.tsx`

```tsx
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import Page from './Page';

// Dev entry only: production is consumed as a Federation remote (remoteEntry.js).
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Page />
  </StrictMode>,
);
```

### Step 16: src/page.css

`modules/inventory/web/src/page.css`

```css
/* Self-contained styles (host Tailwind does not scan remotes). Prefixed .mod-inventory- to avoid collisions. */
.mod-inventory-page { display: grid; gap: 16px; font-family: inherit; color: inherit; }
.mod-inventory-header { display: flex; align-items: center; gap: 12px; }
.mod-inventory-header h1 { margin: 0; font-size: 1.5rem; font-weight: 600; }
.mod-inventory-badge { font-size: 0.75rem; padding: 2px 8px; border-radius: 9999px; background: #eef2ff; color: #3730a3; }
.mod-inventory-card { display: grid; gap: 8px; justify-items: start; padding: 16px; border: 1px solid #e5e7eb; border-radius: 8px; background: #fff; }
.mod-inventory-card h2 { margin: 0; font-size: 1rem; font-weight: 600; }
.mod-inventory-muted { margin: 0; color: #6b7280; font-size: 0.875rem; }
.mod-inventory-button { padding: 8px 14px; border-radius: 6px; border: 1px solid #111827; background: #111827; color: #fff; font-size: 0.875rem; cursor: pointer; }
.mod-inventory-button:disabled { background: #e5e7eb; border-color: #e5e7eb; color: #9ca3af; cursor: not-allowed; }
.mod-inventory-hint { color: #b45309; font-size: 0.8rem; }
.mod-inventory-error { margin: 0; color: #b91c1c; font-size: 0.875rem; }
.mod-inventory-json { justify-self: stretch; margin: 0; padding: 12px; background: #f9fafb; border: 1px solid #e5e7eb; border-radius: 6px; font-size: 0.8rem; overflow: auto; max-height: 320px; }
```

### Step 17: Web Dockerfile

`modules/inventory/web/Dockerfile` (build context = repo root)

```dockerfile
# syntax=docker/dockerfile:1
# Module Federation remote (:5178) — serves remoteEntry.js + assets.
FROM node:20-alpine AS build
WORKDIR /app
COPY modules/inventory/web/package.json modules/inventory/web/package-lock.json* ./
RUN npm install --no-audit --no-fund
COPY modules/inventory/web/ ./
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
```

### Step 18: Parity test

`tests/SharedKernel.Tests/Modules/InventoryModuleTests.cs`

```csharp
using Xunit;
using Endpoints = global::Inventory.Api.Endpoints.ModuleEndpoints;

namespace SharedKernel.Tests.Modules;

// Inventory (scaffolded by tools/create-module): the manifest must pass
// platform validation and the module's seed preview must equal the manifest seed.
public sealed class InventoryModuleTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ModulePlatform.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("ModulePlatform.sln not found above test output.");
    }

    private static ManifestFile Load() =>
        ManifestParser.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "modules", Endpoints.ModuleId, "manifest.yml")));

    [Fact]
    public void Manifest_passes_platform_validation()
    {
        var failures = ManifestValidator.Validate(Load(), new HashSet<string> { "loyalty_remote" }, isDevelopment: true);
        Assert.Empty(failures);
    }

    [Fact]
    public void Manifest_identity_matches_api()
    {
        var m = Load();
        Assert.Equal(Endpoints.ModuleId, m.Module.Name);
        Assert.Equal(Endpoints.Version, m.Module.Version);
    }

    [Fact]
    public void Seed_preview_matches_manifest()
    {
        var m = Load();
        Assert.Equal(m.Api.Seed.Roles.Select(r => r.Name).Order(), Endpoints.SeedRoles.Order());
        Assert.Equal(m.Api.Seed.Permissions.Select(p => p.Code).Order(), Endpoints.SeedPermissions.Order());
        Assert.Equal(m.Frontend.Menus.Select(x => x.Id).Order(), Endpoints.SeedMenus.Order());
        Assert.Contains(Endpoints.ProtectedPermission, m.Frontend.UiPermissions);
    }
}
```

### Step 19: Add the API to the solution

```bash
dotnet sln ModulePlatform.sln add src/Modules/Inventory/Inventory.Api/Inventory.Api.csproj
```

### Step 20: Wire into the Aspire AppHost

[`src/Aspire/ModulePlatform.AppHost/ModulePlatform.AppHost.csproj`](../src/Aspire/ModulePlatform.AppHost/ModulePlatform.AppHost.csproj): add this inside the `ProjectReference` `ItemGroup`:

```xml
    <ProjectReference Include="..\..\Modules\Inventory\Inventory.Api\Inventory.Api.csproj" />
```

[`src/Aspire/ModulePlatform.AppHost/Program.cs`](../src/Aspire/ModulePlatform.AppHost/Program.cs): add this block **before** `builder.Build().Run();`. The `platform` and `auth` variables already exist in that file. `Projects.Inventory_Api` is generated from the project reference.

Keep the `>>>`/`<<<` marker comments: `create-module remove` uses them to undo the wiring.

```csharp
// >>> module:inventory (create-module)
var inventory = builder.AddProject<Projects.Inventory_Api>("inventory")
    .WithEndpoint("http", e => { e.Port = 5204; e.IsProxied = false; })
    .WithEnvironment("ASPNETCORE_URLS", "http://localhost:5204")
    .WithReference(platform)
    .WithReference(auth)
    .WithEnvironment("Platform__BaseUrl", "http://localhost:5121")
    .WithEnvironment("Auth__BaseUrl", "http://localhost:5122");

// Federation remotes must be built (vite preview) — remoteEntry.js is not served by `vite dev`.
var inventoryWeb = builder.AddNpmApp("inventory-web", "../../../modules/inventory/web", "serve")
    .WithEndpoint("http", e => { e.Port = 5178; e.IsProxied = false; })
    .WithExternalHttpEndpoints();
// <<< module:inventory
```

### Step 21: Wire into docker-compose.yml

[`docker-compose.yml`](../docker-compose.yml): add these services **before** the top-level `volumes:` key:

```yaml
  # >>> module:inventory (create-module)
  inventory:
    build:
      context: .
      dockerfile: src/Modules/Inventory/Inventory.Api/Dockerfile
    ports:
      - "5204:8080"
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      Jwt__SecretKey: ${JWT_SECRET:?set JWT_SECRET (>=32 chars, see .env.example)}
      Platform__BaseUrl: http://platform:8080
      Auth__BaseUrl: http://auth:8080
    depends_on:
      - platform
      - auth

  inventory-web:
    build:
      context: .
      dockerfile: modules/inventory/web/Dockerfile
    ports:
      - "5178:80"
  # <<< module:inventory
```

### Step 22: Reference the API from the test project

[`tests/SharedKernel.Tests/SharedKernel.Tests.csproj`](../tests/SharedKernel.Tests/SharedKernel.Tests.csproj): add this to the first `ItemGroup`:

```xml
    <ProjectReference Include="..\..\src\Modules\Inventory\Inventory.Api\Inventory.Api.csproj" />
```

### Step 23: Restore, build, test

```bash
dotnet build ModulePlatform.sln
dotnet test ModulePlatform.sln --filter "FullyQualifiedName~InventoryModuleTests"
(cd modules/inventory/web && npm install && npx tsc --noEmit)
```

---

## 6. manifest.yml reference

Models: [`src/Shared/SharedKernel/ManifestModels.cs`](../src/Shared/SharedKernel/ManifestModels.cs)
(YamlDotNet, camelCase keys, unknown keys ignored). Full example with every key:
[`template/manifest.yml`](../template/manifest.yml). Minimal real example:
[`modules/sample1/manifest.yml`](../modules/sample1/manifest.yml).

### Keys

| Key | Type | Notes |
|-----|------|-------|
| `manifestVersion` | string | `"1.0"` |
| **`module`** | | |
| `module.name` | slug | Unique module id. Used in gateway paths (`/api/v1/<name>/...`) and registry URLs |
| `module.displayName` | string | Shown in the catalog and on Home |
| `module.description` | string | |
| `module.version` | semver | Strict semver. Registry versions are immutable |
| `module.owner` | string | Team or email. Required |
| `module.license`, `module.icon`, `module.category` | string, optional | Catalog metadata |
| **`dependencies[]`** | | |
| `dependencies[].name` | slug | Another module id |
| `dependencies[].versionRange` | semver range | For example `"^1.0.0"`. Installed first (topo-sort) |
| **`service`** | | |
| `service.healthCheck` | path | Default `/healthz` |
| `service.readinessCheck` | path | Default `/readyz` |
| `service.openapiUrl` | path, optional | `/swagger/v1/swagger.json` |
| `service.minPlatformVersion` | string, optional | |
| **`frontend`** | | |
| `frontend.remoteUrl` | absolute URL | Location of `remoteEntry.js` (Vite build: `http://host:port/assets/remoteEntry.js`) |
| `frontend.scope` | string | Federation `name` in `vite.config.ts`. Must be globally unique |
| `frontend.modulePrefix` | path | Host route prefix, for example `/inventory` |
| `frontend.routes[]` | list | `path` (starts with modulePrefix), `exposes` (for example `./Page`, must exist in `vite.config.ts` `exposes`), `requiredPermission` (code or `null`), `menuId` |
| `frontend.menus[]` | list | `id`, `parentId` (`null` or an existing id), `title`, `icon`, `route`, `requiredPermission`, `order` |
| `frontend.uiPermissions[]` | list of codes | Permission codes the UI checks (each must be seeded) |
| **`api`** | | |
| `api.routes[]` | list | Gateway routes: `upstream` (public path, starts with `/`), `downstream` (path on the module API), `methods`, `auth` (default `true`), `requiredPermission`, `priority` |
| `api.seed.roles[]` | list | `name`, `description`, `isSystem` |
| `api.seed.permissions[]` | list | `code`, `object`, `action`, `name`, `isMenuAccess` |
| `api.seed.menus[]` | list | Same shape as `frontend.menus` (ids must be unique across both lists) |
| `api.seed.userRoles[]` | list | `userEmail`, `roleName` |
| `api.seed.rolePermissions[]` | list | `roleName`, `permissions[]` |
| `api.seed.userAdditionalPermissions[]` | list | `userEmail`, `permissions[]` (direct grants) |
| `api.seed.users[]` | list | `email`, `name` |
| `api.seed.gatewayRules[]` | list | Free-form. Usually `[]` |
| **`upgrade`** | | |
| `upgrade.notes` | string, optional | Release notes |
| `upgrade.migrateEndpoint` | path, optional | Called on upgrade, for example `/internal/migrate` |

Gateway downstream host: the gateway resolves the downstream host from the `artifactApiUrl` you
send when you publish. It is not stored in the manifest.

### Validation rules

Source: [`ManifestValidator.Validate`](../src/Shared/SharedKernel/ManifestValidator.cs). The
platform runs it on publish (`POST /api/v1/platform/modules`), passing the scopes of all *other*
registered modules and `isDevelopment = env.IsDevelopment()`.

| Field | Rule | Error message |
|-------|------|---------------|
| `module.name` | Matches `^[a-z0-9-]{3,50}$` | `Must match ^[a-z0-9-]{3,50}$.` |
| `module.name` | Not reserved (case-insensitive): `admin`, `api`, `platform`, `auth`, `gateway`, `t`, `health`, `metrics`, `docs`, `login`, `static` | `Reserved name '<name>'.` |
| `module.displayName` | Not blank, 3-100 characters | `Must be 3-100 chars.` |
| `module.version` | Strict semver (`1.0.0`; not `1.0` or `v1.0.0`) | `Must be valid semver.` |
| `module.owner` | Not blank | `Required.` |
| `dependencies[]` | No duplicates, no self-dependency, `versionRange` parses as a semver range | `Duplicate dependency.` / `Self-dependency is forbidden.` / `Invalid semver range.` |
| `frontend.scope` | Not blank, and not used by another registered module | `Required, globally unique.` / `Scope '<s>' already registered.` |
| `frontend.remoteUrl` | Absolute URL. `https`, or `http` only when the platform runs in Development | `Must be absolute https (http allowed in Development only).` |
| `frontend.modulePrefix` | Not blank, starts with `/` | `Must start with '/'.` |
| `api.seed.permissions[].code` | Unique, matches `^[A-Z]+_[A-Z_]+$` (uppercase letters and underscores, at least one `_`, **no digits**) | `Duplicate permission code.` / `Must match ^[A-Z]+_[A-Z_]+$.` |
| `frontend.routes[].path` | Starts with `modulePrefix` | `Must start with modulePrefix '<p>'.` |
| any `requiredPermission` (routes, menus, api routes) and every `frontend.uiPermissions[]` entry | `null`, or a code present in `api.seed.permissions` | `Unknown requiredPermission '<c>'. Must exist in api.seed.permissions[].` |
| `frontend.menus[]` + `api.seed.menus[]` | `id` unique across both lists. `parentId` is `null` or an existing id | `Duplicate menu id.` / `Parent '<p>' does not exist (or must be null).` |
| `api.routes[].upstream` | Starts with `/`, unique (case-insensitive) | `Upstream must start with '/'.` / `Duplicate upstream template.` |
| `api.routes[].methods` | Each one of `GET POST PUT PATCH DELETE HEAD OPTIONS` | `Invalid method '<m>'.` |
| `api.routes[].priority` | 0..999 | `Must be 0..999.` |

Publish also rejects invalid YAML (`400 VALIDATION_ERROR`), self-dependency, and a
`module.version` that is already published (`Version already published (immutable).`).

Why the CLI spells out digits: permission codes cannot contain digits, so `sample3` gets the
prefix `SAMPLE_THREE` (the code becomes `SAMPLE_THREE_DATA_PROTECTED`). Module *names* may
contain digits.

Seed parity: `POST /internal/seed:preview` returns `ModuleEndpoints.SeedRoles`,
`SeedPermissions` and `SeedMenus`. These must equal the role names, permission codes and
`frontend.menus` ids in the manifest. The generated `<Pascal>ModuleTests.Seed_preview_matches_manifest`
test enforces this, and `Manifest_identity_matches_api` checks that `ModuleId`/`Version` match
`module.name`/`module.version`.

---

## 7. Extending your module

### 7.1 Add an endpoint

Add the endpoint in `Endpoints/ModuleEndpoints.cs`. Always call `.RequireAuthorization()` and
`RequireTenantAsync`. Add `RequirePermissionAsync` for anything that is not open to every
tenant user. Put request records in `Models/Requests.cs`.

```csharp
// Models/Requests.cs
public sealed record AdjustStockRequest(string Sku, int Delta);

// Endpoints/ModuleEndpoints.cs (inside Map)
app.MapPost("/api/v1/stock/adjust", async (AdjustStockRequest req, HttpContext ctx,
    IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg) =>
{
    if (await ModuleGuards.RequireTenantAsync(ctx, http, cache, cfg) is { } tenantError) return tenantError;
    var tenantId = ModuleGuards.TenantId(ctx)!;
    if (await ModuleGuards.RequirePermissionAsync(ctx, http, cfg, tenantId, StockAdjustPermission) is { } denied) return denied;

    if (string.IsNullOrWhiteSpace(req.Sku))
        return ApiErrors.Problem(ErrorCodes.ValidationError, "sku is required.", 400);
    return Results.Json(new { data = new { tenantId, req.Sku, req.Delta } });
}).RequireAuthorization();
```

What the guards do ([`ModuleGuards.cs`](../src/Shared/SharedKernel/ModuleGuards.cs)):

- `RequireTenantAsync`: requires the `tenantId` claim (`403 FORBIDDEN_TENANT`), checks that any
  `X-Tenant-Id` header matches it (`403 FORBIDDEN_TENANT`), and checks that the tenant is usable
  via Platform `GET /internal/tenants/{id}/status`. The verdict is cached for 30 s. Unknown tenant
  gives `404 UNKNOWN_TENANT`, inactive gives `403 TENANT_DISABLED`, and an unreachable platform
  gives `503 AUTHZ_UNAVAILABLE`.
- `RequirePermissionAsync`: asks Auth `GET /api/v1/auth/permissions/resolve?tenantId&userId` on
  every call, with no module-side cache. A missing permission gives `403 FORBIDDEN` with
  `details.requiredPermission`. Auth unreachable or non-2xx gives `503 AUTHZ_UNAVAILABLE`.

The gateway route must cover the new path and method. The template route
`/api/v1/inventory/{rest}` with `methods: [GET]` does **not** forward `POST`, so update it:

```yaml
api:
  routes:
    - upstream: /api/v1/inventory/{rest}
      downstream: /api/v1/{rest}
      methods: [GET, POST]
      auth: true
      requiredPermission: null
      priority: 10
```

### 7.2 Add a permission

Change all four places together, or the parity test fails:

1. `manifest.yml`, `api.seed.permissions`:
   ```yaml
      - code: INVENTORY_STOCK_ADJUST
        object: INVENTORY_STOCK
        action: ADJUST
        name: Adjust Inventory stock
        isMenuAccess: false
   ```
2. `manifest.yml`, `api.seed.rolePermissions` (grant it to a seeded role, or add a new role under `api.seed.roles`):
   ```yaml
      - roleName: Inventory Viewer
        permissions: [INVENTORY_DATA_VIEW, INVENTORY_DATA_PROTECTED, INVENTORY_STOCK_ADJUST]
   ```
3. `manifest.yml`, `frontend.uiPermissions` (if the UI checks it):
   ```yaml
     uiPermissions: [INVENTORY_DATA_PROTECTED, INVENTORY_STOCK_ADJUST]
   ```
4. `ModuleEndpoints.cs`:
   ```csharp
   public const string StockAdjustPermission = "INVENTORY_STOCK_ADJUST";
   public static readonly string[] SeedPermissions = [ViewPermission, ProtectedPermission, StockAdjustPermission];
   ```

Then bump the version (7.6). A new seed applies only through a new published version (install
or upgrade).

### 7.3 Add a page and route

1. Create `modules/inventory/web/src/StockPage.tsx` with a default export. Reuse `call()` from
   `api.ts` and the `.mod-inventory-` CSS prefix.
2. Expose it in `vite.config.ts`:
   ```ts
   exposes: { './Page': './src/Page.tsx', './StockPage': './src/StockPage.tsx' },
   ```
3. Add the route and menu to `manifest.yml`. The route path must start with `modulePrefix`.
   The parent menu must exist. Also add the new menu id to `ModuleEndpoints.SeedMenus`
   (the parity test compares it with `frontend.menus`).
   ```yaml
   frontend:
     routes:
       - path: /inventory/stock
         exposes: ./StockPage
         requiredPermission: INVENTORY_STOCK_ADJUST
         menuId: inventory.stock
     menus:
       - id: inventory.stock
         parentId: inventory
         title: Stock
         icon: box
         route: /inventory/stock
         requiredPermission: INVENTORY_STOCK_ADJUST
         order: 1
   ```
   ```csharp
   public static readonly string[] SeedMenus = ["inventory", "inventory.stock"];
   ```
4. Rebuild the remote (`npm run serve`), bump the version, then publish and upgrade.

### 7.4 Call the API from a page

The remote runs inside the Tenant Admin page, so call the gateway with **relative** paths
(`/api/v1/<name>/...`). The host proxies `/api/v1` to `:5100`. `call()` attaches the host
session (`access_token`, `tenant_id` from `sessionStorage`), redirects to `/login` on `401`, and
throws `CODE: message` for error envelopes. The host returns `{ data }` on success and
`{ error: { code, message } }` on failure.

```tsx
const res = await fetch('/api/v1/inventory/stock/adjust', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    Authorization: `Bearer ${sessionStorage.getItem('access_token') ?? ''}`,
    'X-Tenant-Id': sessionStorage.getItem('tenant_id') ?? '',
  },
  body: JSON.stringify({ sku: 'SKU-1', delta: 5 }),
});
```

`call()` in the template only does `GET`. Extend it with an optional `init` parameter if you need
other methods. Gate buttons on `/api/v1/auth/me` permissions for UX only. The server-side
`RequirePermissionAsync` is the real check.

### 7.5 Depend on another module

```yaml
dependencies:
  - name: loyalty
    versionRange: "^1.0.0"
```

On install, the platform topo-sorts the dependencies and installs the highest registry version
that satisfies each range first. If no version matches, the install fails with
`DEPENDENCY_MISSING`. A selected version that conflicts with another range fails with a version
conflict, and cycles are rejected. The dependency must already be published. To call it, go
through the gateway (`/api/v1/loyalty/...`) with the caller's token. Do not call its port
directly.

### 7.6 Versioning and upgrade

Registry versions are **immutable**. Re-publishing the same `module.version` fails. For every
release:

1. Bump the version in all three places, and keep them equal (the parity test checks the first two):
   - `modules/inventory/manifest.yml`: `module.version: 1.1.0`
   - `ModuleEndpoints.cs`: `public const string Version = "1.1.0";`
   - `modules/inventory/web/package.json`: `"version": "1.1.0"` (and `VERSION` in `Page.tsx` if you show it)
2. Update `upgrade.notes`. If data changes, implement it in `POST /internal/migrate`
   (`{ fromVersion, toVersion }`). Keep migrations additive.
3. Publish, then upgrade each tenant:
   ```bash
   curl -s -X POST localhost:5100/api/v1/platform/tenants/$TENANT_ID/modules/inventory/upgrade \
     -H "Authorization: Bearer $PLATFORM_TOKEN" -H 'Content-Type: application/json' \
     -d '{"toVersion":"1.1.0"}'
   ```

---

## 8. Run, publish, install and verify

### 8.1 Run

```bash
# Everything (Postgres :5434 + core APIs + all modules + frontends + dashboard)
dotnet run --project src/Aspire/ModulePlatform.AppHost

# Or the module alone, next to a running core stack (see SETUP.md for the core)
dotnet run --project src/Modules/Inventory/Inventory.Api
(cd modules/inventory/web && npm install && npm run serve)

# Smoke checks
curl -s localhost:5204/healthz                          # {"status":"ok","module":"inventory","version":"1.0.0"}
curl -s -X POST localhost:5204/internal/seed:preview    # roles/permissions/menus
curl -sI localhost:5178/assets/remoteEntry.js | head -1 # HTTP/1.1 200 OK
```

Swagger UI: http://localhost:5204/swagger

### 8.2 Get tokens

```bash
# Platform Admin (Development: any password works on first login for admin@local)
PLATFORM_TOKEN=$(curl -s -X POST localhost:5100/api/v1/platform/auth/login \
  -H 'Content-Type: application/json' -d '{"email":"admin@local","password":"admin"}' \
  | jq -r .data.accessToken)

TENANT_ID=tnt_demo   # or the id of a tenant you created (SETUP.md section 3.2)

# Tenant user (needed to call module endpoints)
TENANT_TOKEN=$(curl -s -X POST localhost:5100/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d "{\"tenantId\":\"$TENANT_ID\",\"email\":\"admin@acme.example\",\"password\":\"anything\"}" \
  | jq -r .data.accessToken)
```

### 8.3 Publish

With the CLI:

```bash
node tools/create-module/create-module.mjs publish inventory --token "$PLATFORM_TOKEN"
# optional: --api-url http://inventory:8080 (compose) --gateway http://localhost:5100
```

With curl:

```bash
curl -s -X POST localhost:5100/api/v1/platform/modules \
  -H "Authorization: Bearer $PLATFORM_TOKEN" -H 'Content-Type: application/json' \
  -d "{\"manifestYaml\": $(jq -Rs . < modules/inventory/manifest.yml), \"artifactApiUrl\": \"http://localhost:5204\"}"
# -> 201 {"data":{"id":"inventory","version":"1.0.0"}}
```

You can also go to the platform shell (http://localhost:5173), open Modules, choose **Register
module**, then paste or upload the manifest and enter the API URL.

### 8.4 Install, and other lifecycle operations

```bash
node tools/create-module/create-module.mjs install inventory --tenant "$TENANT_ID" --token "$PLATFORM_TOKEN"

# curl equivalents
BASE=localhost:5100/api/v1/platform/tenants/$TENANT_ID/modules/inventory
H=(-H "Authorization: Bearer $PLATFORM_TOKEN" -H 'Content-Type: application/json')
curl -s -X POST "$BASE/install"   "${H[@]}" -d '{}'                    # or {"version":"1.0.0"}
curl -s -X POST "$BASE/disable"   "${H[@]}" -d '{"reason":"maintenance"}'
curl -s -X POST "$BASE/enable"    "${H[@]}" -d '{}'
curl -s -X POST "$BASE/uninstall" "${H[@]}" -d '{}'
curl -s localhost:5100/api/v1/platform/tenants/$TENANT_ID/modules -H "Authorization: Bearer $PLATFORM_TOKEN"
```

### 8.5 Verify

```bash
curl -s localhost:5100/api/v1/tenant/frontend-bootstrap -H "Authorization: Bearer $TENANT_TOKEN" | jq
curl -s localhost:5100/api/v1/inventory/data      -H "Authorization: Bearer $TENANT_TOKEN" | jq
curl -s localhost:5100/api/v1/inventory/protected -H "Authorization: Bearer $TENANT_TOKEN" | jq
# without the Inventory Viewer role -> 403 FORBIDDEN, details.requiredPermission = INVENTORY_DATA_PROTECTED
```

### 8.6 Validation scenarios

These are condensed from [`specs/001-sample-test-modules/quickstart.md`](../../specs/001-sample-test-modules/quickstart.md).
Use **one** tenant, because gateway module state is global. You also need a second tenant user
with no module role.

| # | Action | Expected |
|---|--------|----------|
| 1 | Before install, sign in to Tenant Admin `:5175` and open Home | No Inventory entry |
| 2 | Install, then reload Home | "Inventory" card and nav link. Page shows the name, version and two buttons |
| 3 | Click **Get sample data** | JSON with `module: inventory`, `kind: sample`, fresh `generatedAt`, 3 items, in under 2 s |
| 4 | Without the role, look at **Get protected data** | Disabled, hint "Requires INVENTORY_DATA_PROTECTED" |
| 5 | `curl .../api/v1/inventory/protected` with the tenant token | `403 FORBIDDEN` with `details.requiredPermission` |
| 6 | Assign **Inventory Viewer** (Users, then you, then Roles), then reload | Button enabled. Click returns `kind: protected` |
| 7 | Remove the role, then repeat #5 | Button disabled. curl returns `403` immediately (no cache) |
| 8 | Disable the module, then click on the open page | `MODULE_DISABLED` message. After reload the nav entry is gone. Other modules are unaffected |
| 9 | Enable it, then reload | Entry and data return without reinstall |
| 10 | Uninstall | Entry gone. `/api/v1/inventory/data` returns `404 UNKNOWN_MODULE_ROUTE`. `INVENTORY_*` permissions and "Inventory Viewer" removed |
| 11 | Stop the remote (`:5178`), then reload its route | "Module unavailable" card. The rest of the host still works |
| 12 | Stop the API (`:5204`), then click Get sample data | Inline error (gateway upstream failure). Page stays usable |
| 13 | Call `/api/v1/inventory/data` as a user of tenant B with `X-Tenant-Id: <tenant A>` | `403 FORBIDDEN_TENANT` |

---

## 9. Rules and conventions

Security

- [ ] Every business endpoint calls `.RequireAuthorization()` **and** starts with `ModuleGuards.RequireTenantAsync`.
- [ ] Every non-public operation calls `ModuleGuards.RequirePermissionAsync`. It fails closed (`503 AUTHZ_UNAVAILABLE`). Never treat an authorization error as "allow".
- [ ] Never trust tenant or user identity from the request body. Use `ModuleGuards.TenantId(ctx)` / `UserId(ctx)` (from JWT claims).
- [ ] Do not add a module-side permission cache. Auth already invalidates its own cache on grant and revoke.
- [ ] Never commit secrets. `Jwt:SecretKey` comes from env (`Jwt__SecretKey` / compose `JWT_SECRET`). Outside Development, the API refuses to start if the secret is missing, shorter than 32 characters, or the dev default.
- [ ] Do not log tokens or personal data.
- [ ] Only the gateway is exposed outside local dev. `/internal/*` endpoints must not be routed by the gateway: keep `api.routes` scoped to `/api/v1/<name>/...`.

Contract

- [ ] `module.name` = folder name = `ModuleEndpoints.ModuleId`. Versions are equal in the manifest, `ModuleEndpoints.Version` and `package.json`.
- [ ] Seed arrays in `ModuleEndpoints` match the manifest (parity test).
- [ ] Permission codes are `<PREFIX>_<OBJECT>_<ACTION>`: uppercase letters and underscores only, no digits.
- [ ] `frontend.scope` equals the federation `name` in `vite.config.ts` and is unique across all modules (convention: `<name>_remote`, with `-` replaced by `_`).
- [ ] Every `exposes` used in `frontend.routes` exists in `vite.config.ts`.

Frontend

- [ ] Prefix every CSS class with `.mod-<name>-`. The host's Tailwind does not scan remotes, so utility classes in a remote produce no CSS, and unprefixed classes can collide with the host.
- [ ] Share only `react` and `react-dom`. Do not bundle a second router or state library that the host expects to own.
- [ ] Remotes must be **built**: use `npm run serve` (build + `vite preview`). `vite dev` does not serve `remoteEntry.js` for originjs federation. `npm run dev` + `index.html` are for standalone page work only.
- [ ] Use relative `/api/v1/...` paths. Never hard-code gateway or API hosts in the remote.

Operations

- [ ] Every `api.routes[].upstream` starts with `/api/v1/<name>/`. The gateway rejects routes outside the module's own prefix (`400 CONFIG_INVALID` on install). Gateway enable/disable state is per tenant, so you can test with several tenants ([`docs/gap-closure-report.md`](gap-closure-report.md), R-GW1 closed).
- [ ] Keep ports unique: APIs from 5204 up, remotes from 5178 up. `create-module list` shows the ports in use.

---

## 10. Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `address already in use` / Vite `Port 5178 is in use` (`strictPort`) | Another process, or another module, on the port | `lsof -i :5178`, then stop it, or regenerate with `--api-port`/`--web-port`. Update `launchSettings.json`, `vite.config.ts`, `package.json` scripts, `manifest.yml` `remoteUrl`, the AppHost and compose |
| Publish `400` `frontend.scope: Scope '<s>' already registered.` | Another module already registered the scope | Pick a unique scope. Change it in **both** `manifest.yml` and the `vite.config.ts` `name`, then bump the version |
| `api.seed.permissions[X]: Must match ^[A-Z]+_[A-Z_]+$.` | Digits, lowercase or hyphens in the code | Spell digits out (`SAMPLE_THREE_...`) or use `--perm-prefix`. Update the manifest and `ModuleEndpoints` |
| `Unknown requiredPermission '<c>'` | Route, menu or `uiPermissions` references a code that is not seeded | Add it to `api.seed.permissions` |
| Parity test fails (`Seed_preview_matches_manifest` / `Manifest_identity_matches_api`) | `ModuleEndpoints` arrays, id or version drifted from the manifest | Make them equal (section 7.2 / 7.6) |
| "Module unavailable" card / remote not loading | Remote not running, or started with `vite dev`. Wrong `remoteUrl`/`scope`/`exposes` | Run `npm run serve`. `curl -I http://localhost:5178/assets/remoteEntry.js` must return 200. Check that scope and `exposes` match `vite.config.ts`. Check the browser console |
| CORS error loading `remoteEntry.js` | Preview server without CORS | Keep `preview: { cors: true }` in `vite.config.ts`. For nginx, add `Access-Control-Allow-Origin` |
| `403 FORBIDDEN_TENANT` | Token has no `tenantId` claim (platform token used), or `X-Tenant-Id` differs from the token's tenant | Use a tenant token (`POST /api/v1/auth/login`) and send the matching header, or none |
| `403 FORBIDDEN` with `requiredPermission` | User lacks the permission | Assign the role (for example **Inventory Viewer**) or a direct grant in Tenant Admin |
| `503 AUTHZ_UNAVAILABLE` | Platform (`:5121`) or Auth (`:5122`) unreachable from the module, or wrong `Platform:BaseUrl`/`Auth:BaseUrl` | Start the core services. Check `appsettings.Development.json`, the AppHost env, and the compose `Platform__BaseUrl`/`Auth__BaseUrl`. The error is retryable |
| `404 UNKNOWN_MODULE_ROUTE` on `/api/v1/<name>/...` | Module not installed **for the token's tenant**, or the path or method is not covered by `api.routes` | Install it for that tenant. Check the `upstream` template and `methods` |
| `401 UNAUTHORIZED` from the gateway on `/api/v1/<name>/...` | No bearer token, or the token is invalid or expired (the gateway validates the JWT to find the tenant) | Log in again and send `Authorization: Bearer <tenant token>` |
| `503 MODULE_DISABLED` | Module disabled for this tenant | `POST .../modules/<name>/enable` for that tenant |
| Gateway `502` / upstream error | Module API down, or wrong `artifactApiUrl` at publish | Start the API. Re-publish a **new version** with the correct `--api-url` |
| Publish `400 Version already published (immutable).` | Same `module.version` re-published | Bump the version (section 7.6). This is expected when you re-run publish |
| API exits: `Required secret 'Jwt:SecretKey' is missing…` | Non-Development environment without the secret | Set `Jwt__SecretKey` (compose: `JWT_SECRET` in `.env`, at least 32 characters, not the dev default). Locally, use the Development launch profile |
| `401` from the module but the host works | API validates JWTs with a different secret than Auth/Platform | Use the same `Jwt:SecretKey` everywhere |
| Publish `403 Module publish requires Admin.` | Token is not a platform Admin | Log in as `admin@local` (or another Admin) |
| `Projects.Inventory_Api` not found in the AppHost | Missing `ProjectReference` in `ModulePlatform.AppHost.csproj` | Add it (step 20) and rebuild |

---

## 11. Pre-PR checklist

```bash
dotnet build ModulePlatform.sln --nologo -v q      # 0 errors, 0 new warnings
dotnet test ModulePlatform.sln --nologo -v q       # all green, incl. <Pascal>ModuleTests
(cd modules/inventory/web && npx tsc --noEmit && npx vite build)
```

- [ ] Manifest validated: the parity test passes. If you edited the manifest by hand, publish succeeded on a local registry.
- [ ] Version bumped consistently (manifest, `ModuleEndpoints.Version`, `package.json`) if anything shipped changed.
- [ ] New endpoints use `RequireAuthorization` + `RequireTenantAsync` (+ `RequirePermissionAsync`).
- [ ] New permissions are added in all four places (section 7.2).
- [ ] CSS is prefixed `.mod-<name>-`. The remote works when served with `npm run serve`.
- [ ] No secrets, tokens or connection strings in the diff.
- [ ] Wiring present: sln, AppHost csproj + `Program.cs`, `docker-compose.yml`, test csproj.
- [ ] Validation scenarios (section 8.6) run against a local stack.
- [ ] `modules/<name>/README.md` is up to date. The module is added to the root [README.md](../README.md) (layout and ports) if it is a long-lived module.
