# Module-as-Plugin Platform (`module_as_plugin/`)

Greenfield implementation of `requirement/01-module-platform-requirements.md` + gateway admin
(`requirement/ocelot_gateway_admin.md`), verified per `requirement/verification_guidline.md`
and built per `requirement/implementation_guidline.md`.

Stack: .NET 8 APIs, Ocelot 25 gateway, PostgreSQL + EF Core, React 18 + Vite 5 +
`@originjs/vite-plugin-federation` shell/remotes. Shell UI: Tailwind CSS v4 +
shadcn/ui-style component set (Radix primitives, `src/components/ui/`) per
`requirement/UIDesign_guideline.md`.

## Layout

```text
module_as_plugin/
  ModulePlatform.sln
  apps/shell/                 # Platform SPA host (Module Federation, bootstrap-driven nav)
    src/app/                  # App, router, useBootstrap (remote loader)
    src/pages/                # LoginPage (platform only), CatalogPage, TenantsPage, GatewayPage
    src/components/           # RemoteRoute (lazy), ModuleErrorBoundary (isolation)
    src/lib/                  # api-client (envelope), bootstrap (SR-6b types)
  apps/tenant-admin/          # Tenant backoffice SPA (tenant login + users/roles/permissions)
    src/app/                  # App, router, tenant session
    src/pages/                # LoginPage, HomePage, UsersPage, UserDetailPage,
                              # RolesPage, RoleDetailPage, PermissionsPage
    src/lib/                  # api-client (envelope), tenant auth
  modules/loyalty/
    manifest.yml              # normative contract example (§10.3)
    web/                      # Federation remote (scope loyalty_remote)
  template/manifest.yml       # blank module contract copy
  src/Shared/SharedKernel/    # Manifest models/parser, validator, dep resolver,
                              # permission union, menu tree, error codes, Cuid/Result
  src/Platform/               # Clean Architecture: Domain / Application / Infrastructure / Api (:5121)
  src/Auth/Auth.Api/          # Tenant users/roles/permissions + resolve (:5122, Domain/Infrastructure folders)
  src/Gateway/Gateway.Api/    # Ocelot proxy + admin API + traffic (:5100, ocelot.json + registry)
  src/Modules/Loyalty/        # Sample module API (:5201, health/ready/seed-preview/migrate)
  src/Aspire/                 # .NET Aspire orchestration (AppHost + ServiceDefaults)
    ModulePlatform.AppHost/   # Postgres (:5434, volume) + 4 APIs + 3 frontends + dashboard
    ModulePlatform.ServiceDefaults/ # resilience, service discovery, OTel, /health + /alive
  tests/SharedKernel.Tests/   # validator + resolver + permission + menu tests
  docker-compose.yml          # postgres + services
```

Ports: gateway `:5100`, platform `:5121`, auth `:5122`, loyalty `:5201`, shell `:5173`, loyalty-web `:5174`, tenant-admin `:5175`.

Full local setup (DB + migrations + sample tenant): see [SETUP.md](SETUP.md).

## Run (Aspire, recommended)

One command starts Postgres (`:5434`, volume `module-platform-pgdata`), all 4
APIs on their fixed ports, the 3 Vite frontends, and the Aspire dashboard
(telemetry + resource graph). First run initializes an empty database; both
APIs auto-apply EF migrations and the dev seed (`admin@local` + demo tenant):

```bash
dotnet run --project src/Aspire/ModulePlatform.AppHost
```

`TenantStores` (provisioning + per-tenant reads) is wired by the AppHost to the
same Postgres, so tenant provisioning works with no extra config. The
standalone `msa-postgres` container (`:5433`) and F5 launch profiles are
unaffected — use those when you want to debug a single service.

## Run (manual, Postgres required)

APIs need physical PostgreSQL — `ConnectionStrings:Catalog/Tenant` are required
(no InMemory fallback; missing value fails fast at startup):

```bash
dotnet build ModulePlatform.sln
dotnet test ModulePlatform.sln
export ConnectionStrings__Catalog="Host=localhost;Username=module_platform;Password=module_platform;Database=module_platform"
export ConnectionStrings__Tenant="Host=localhost;Username=module_platform;Password=module_platform;Database=module_platform"
dotnet run --project src/Platform/Platform.Api      # :5121
dotnet run --project src/Auth/Auth.Api              # :5122
dotnet run --project src/Gateway/Gateway.Api        # :5100
dotnet run --project src/Modules/Loyalty/Loyalty.Api # :5201
```

Frontends:

```bash
cd apps/shell && npm install && npm run dev        # :5173 (proxies /api/v1 -> :5100)
cd modules/loyalty/web && npm install && npm run dev # :5174 remoteEntry.js
cd apps/tenant-admin && npm install && npm run dev # :5175 tenant backoffice (proxies /api/v1 -> :5100)
```

## Key contracts

- `manifest.yml` is platform-only (R10). Browser uses `GET /api/v1/tenant/frontend-bootstrap` (SR-6b).
- Lifecycle: `validate -> deps topo-sort -> INSTALLING -> gateway routes -> tenant seed -> INSTALLED+ENABLED` (SR-5), per-(tenant,module) lock (R8).
- Gateway: disabled module `503 MODULE_DISABLED`, uninstalled `404`; admin writes validated before apply, persisted to `ocelot.json`.
- Permission = role permissions UNION user additional, ACTIVE only (A-BR3).

## Tenant lifecycle (P-BR3, SR-4)

- Validation on create/update: name 3–100 unique (CI), slug 3–50 `^[a-z0-9-]+$`
  with reserved words blocked, optional domain (lowercase hostname) / pathPrefix
  (`{base}/{slug}`, base from `TenantRouting:PathPrefix`, default `/t`),
  resolutionMode `domain|path|header`, valid contact email.
- Dedicated tenants accept an optional `dbConnectionString` (collapsed input in
  the create dialog; `Host` + `Database` required, `shared` mode rejected).
  Its `Database` is honored and tables live under a slug-derived schema;
  uniqueness is the (server, database, schema) tuple. Stored
  Data-Protection-encrypted (`DbConnectionEncrypted`, never returned —
  responses carry only `hasCustomConnection`), immutable after creation
  (`422`), handed to Auth at runtime via cluster-internal
  `GET /internal/tenants/{id}/connection` (`X-Internal-Key`
  `InternalApi:Key`). Absent → platform default server.
  Duplicates → `409 DUPLICATE_TENANT` with `{field}`; shape errors → `400`
  with field list; immutable `slug/dbName/schema` on PATCH → `422 IMMUTABLE_FIELD`.
- `PATCH /api/v1/platform/tenants/{tid}`, `POST .../disable {reason}` and
  `POST .../enable` (Admin only), `GET .../audits` (recent audit trail).
  All mutations are audited (`tenant.create/update/disable/enable`).
- Tenant must exist and be `Active` on every tenant-scoped call: bootstrap,
  tenant + platform lifecycle, Auth login/resolve, loyalty module APIs.
  Unknown → `404 UNKNOWN_TENANT`, disabled → `403 TENANT_DISABLED`.
  An `X-Tenant-Id` header disagreeing with the effective tenant → `403 FORBIDDEN_TENANT` (BR-R2).
- Auth/module APIs check usability via `GET /internal/tenants/{id}/status`
  (Platform, cluster-internal — not routed by the gateway), cached 30s;
  platform unreachable → `503 AUTHZ_UNAVAILABLE` (fail closed, retryable).

## Login (platform vs tenant)

- Platform portal (`apps/shell`, `:5173/login`): platform operators only
  (`POST /api/v1/platform/auth/login`). The page links out to Tenant Admin —
  tenant login is not embedded here. Logged-in operators get a **Profile**
  page (`/profile`): editable name, readonly email, role/status/IDs/timestamps,
  plus a **Change password** action (`GET|PATCH /api/v1/platform/auth/me`,
  `POST /api/v1/platform/auth/change-password`).
- Tenant Admin portal (`apps/tenant-admin`, `:5175/login`): tenant ID + email +
  password (`POST /api/v1/auth/login`), no platform mode and no link back.
  Successful login lands on the tenant Home (counts + effective permissions).
  Logged-in users get a **Profile** page (`/profile`): editable name, readonly
  email, tenant/roles/effective-permissions/status/timestamps, plus a
  **Change password** action (`GET|PATCH /api/v1/auth/me`,
  `POST /api/v1/auth/change-password`). Neither profile response includes a
  password hash; new passwords must be 8–200 chars and are stored as Argon2id.
- Credential throttling: the gateway limits `POST` login and change-password
  (`/api/v1/platform/auth/*`, `/api/v1/auth/*`) to 10/min per client IP
  (`RateLimiting:CredentialPermitPerMinute`); over the limit →
  `429 RATE_LIMITED` + `Retry-After: 60`. Direct service ports are not
  throttled — expose only the gateway outside local dev (G-BR3).
- Last-admin guard (edge case 12): deactivating a user, removing a user-role
  link, deactivating a role, or uninstalling a module whose seeded system role
  is the tenant's last admin grant → `409 LAST_ADMIN`, nothing changed. "Admin
  grant" = active user with an active role whose name contains `Admin` (same
  rule as the `tenant_admin` login claim). Uninstall revokes the seed before
  touching gateway routes, so a refusal leaves the module fully installed.
- Tenant Admin portal (`apps/tenant-admin`, `:5175/login`): tenant ID + email +
  password (`POST /api/v1/auth/login`), no platform mode and no link back.
  Successful login lands on the tenant Home (counts + effective permissions).
- Tenant Admin manages the tenant store: users (list/create/activate/deactivate),
  roles (list/create/activate/deactivate), read-only permission catalog, plus
  user-role assignment and direct (additional) user-permission grants on the
  detail pages. Backend: `GET/POST /api/v1/admin/users/{id}/roles`,
  `DELETE .../roles/{roleId}`, `GET/POST /api/v1/admin/roles/{id}/permissions`,
  `DELETE .../permissions/{permissionId}`,
  `GET/POST /api/v1/admin/users/{id}/permissions`,
  `DELETE .../permissions/{permissionId}`,
  `PATCH /api/v1/admin/users/{id}` (`name`, `isActive`),
  `PATCH /api/v1/admin/roles/{id}` (`description`, `isActive`).
  Unknown user/role/permission → `404 UNKNOWN_USER/UNKNOWN_ROLE/UNKNOWN_PERMISSION`;
  duplicate grant → `409`; revoked link → `404`.

## Sample test modules (`sample1`, `sample2`)

Two template-built modules for exercising the install lifecycle end to end
(spec: `../specs/001-sample-test-modules/`).

| | Sample Module 1 | Sample Module 2 |
|---|---|---|
| API | `src/Modules/Sample1/Sample1.Api` :5202 | `src/Modules/Sample2/Sample2.Api` :5203 |
| Remote | `modules/sample1/web` :5176 (`npm run serve`) | `modules/sample2/web` :5177 |
| Gateway | `/api/v1/sample1/{data,protected}` | `/api/v1/sample2/{data,protected}` |
| Permission / role | `SAMPLE_ONE_DATA_PROTECTED` / Sample 1 Viewer | `SAMPLE_TWO_DATA_PROTECTED` / Sample 2 Viewer |

- Each page has **Get sample data** (any tenant user) and **Get protected
  data** (enabled only when `/api/v1/auth/me` lists the module's protected
  permission; the API re-checks every call via Auth resolve, `403 FORBIDDEN`
  / fail-closed `503 AUTHZ_UNAVAILABLE`, no module-side cache).
- **Tenant Admin (:5175) is the Module Federation host**: Home lists installed
  modules, the top nav gets one link per module menu, and module routes
  lazy-load the remote from `GET /api/v1/tenant/frontend-bootstrap`. The
  platform shell stays operator-only.
- Shared guard helper: `SharedKernel/ModuleGuards.cs` (tenant claim + header
  match + usability, permission resolve).
- Gateway module gating is per tenant (tenant from the validated JWT):
  enable/disable/uninstall in one tenant never affects another (R-GW1 closed).
  The Platform pushes state to the `X-Internal-Key`-protected
  `/api/v1/gateway/internal/modules/{moduleId}/tenants/{tenantId}` and the
  gateway reconciles from `GET /internal/gateway/module-states` every 5 min
  (R-GW2 closed; see `docs/gap-closure-report.md`). The gateway therefore
  needs `InternalApi:Key` and `Platform:BaseUrl`.
- Known limits: remotes must be built (`vite preview`), not `vite dev`.

## Creating a new module

Guide: [docs/module-developer-guide.md](docs/module-developer-guide.md). Scaffold + wire in one step:

```bash
node tools/create-module/create-module.mjs new <name> -y     # API + remote + manifest + parity test
node tools/create-module/create-module.mjs publish <name> --password <pw>
node tools/create-module/create-module.mjs install <name> --tenant <TENANT_ID> --password <pw>
```

## Module Catalog (platform shell, `/modules`)

- **All modules** (default picker value): every registered module with
  latest version, dependencies, Available/Deprecated badge and "Installed in
  N tenants". **Install to tenants…** opens a multi-select dialog (active
  tenants only; tenants that already have it are shown with their state),
  version defaults to latest; the UI calls the per-tenant install API once per
  tenant and shows a result per tenant. Deprecated modules can't be installed.
- **Register module** (Admin): upload/paste `manifest.yml` + module API URL
  (+ optional remote URL) → `POST /api/v1/platform/modules`; validation
  errors are listed inline.
- **Tenant selected**: only that tenant's Installed / Disabled / Failed
  modules, with Enable / Disable / Upgrade / Uninstall and Retry install.
- Maintainer sees everything read-only (R1).
- APIs: `GET /api/v1/platform/modules` (versions newest-first by semver,
  dependencies of the latest version, `installedTenantCount`),
  `GET /api/v1/platform/modules/{id}/installs` (per-tenant state for one module).

## Tenant routing prefix

- `TenantRouting:PathPrefix` (default `/t`, e.g. `TenantRouting__PathPrefix=/tenants`):
  base of tenant path prefixes. Create/PATCH validate `pathPrefix` against
  `{base}/{slug}` and default blank prefixes to `{base}/{slug}` (name/slug stay
  unique per BR-T1). The shell create dialog prefills the prefix from the slug
  until edited by hand.

## Tenant store provisioning (dedicated DB / shared schema)

- Create accepts `deploymentMode` (`dedicated`, default | `shared`), optional
  `dbName` (auto `tenant_{slug}`, `^[a-z_][a-z0-9_]*$` ≤63, unique among
  dedicated tenants), and `schema` (required for shared, unique, never
  `public`/`pg_*`/`information_schema`). Returns `202` while `Provisioning`.
- Platform creates the store synchronously (`CREATE DATABASE` if missing, else
  reuse; `CREATE SCHEMA IF NOT EXISTS`) with the privileged
  `TenantStores:ProvisioningConnection` (module_migrator, CREATEDB), then Auth
  migrates the tenant Auth model lazily on first use (per-schema DDL stamping
  + per-schema `__EFMigrationsHistory`, `search_path` backstop) and reports
  back: `Ready/Active` (+ `schemaVersion`) or `Failed` (+ `dbError`).
- Without `TenantStores` config, creation marks the tenant `Ready/Active`
  immediately on the shared store.
- Bootstrap users are created with `X-Tenant-Id` into the tenant's store.
  Retry: `POST /platform/tenants/{tid}/provision:retry`.
- Bootstrap admin: pass `bootstrapAdminEmail` (+ optional `bootstrapAdminName`)
  at creation; it is seeded as the first tenant user on first store use, so the
  first tenant login with that email succeeds (Development: any password on
  first use, claimed and stored as Argon2id; Production requires the real
  password — no stub fallback).
- Config (`TenantStores`): `Host/Port/Username/Password/SharedDatabase` for
  building tenant connections server-side (never in catalog traffic, NFR4);
  `ProvisioningConnection` for DDL. `InternalApi:Key` guards the
  custom-connection hand-off endpoint.
- Required secrets: `Jwt:SecretKey` (all 4 APIs) and `InternalApi:Key`
  (Platform + Auth). Development falls back to built-in dev values; any other
  environment refuses to start when a secret is missing, shorter than 32
  chars, or the dev default (`docker compose` needs `JWT_SECRET` and
  `INTERNAL_API_KEY` in `.env`). Data Protection keys persist under
  `DataProtection:KeyDirectory` (default `./keys`) — back this directory up
  with the database or stored secrets become undecryptable. See `.env.example` and `docker-compose.yml`.
