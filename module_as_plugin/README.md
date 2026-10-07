# Module-as-Plugin Platform (`module_as_plugin/`)

Greenfield implementation of `requirement/01-module-platform-requirements.md` + gateway admin
(`requirement/ocelot_gateway_admin.md`), verified per `requirement/verification_guidline.md`
and built per `requirement/implementation_guidline.md`.

Stack: .NET 8 APIs, Ocelot 25 gateway, PostgreSQL + EF Core, React 18 + Vite 5 +
`@originjs/vite-plugin-federation` shell/remotes.

## Layout

```text
module_as_plugin/
  ModulePlatform.sln
  apps/shell/                 # SPA host (Module Federation, bootstrap-driven nav)
    src/app/                  # App, router, useBootstrap (remote loader)
    src/pages/                # CatalogPage, TenantsPage, GatewayPage
    src/components/           # RemoteRoute (lazy), ModuleErrorBoundary (isolation)
    src/lib/                  # api-client (envelope), bootstrap (SR-6b types)
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
  tests/SharedKernel.Tests/   # validator + resolver + permission + menu tests
  docker-compose.yml          # postgres + services
```

Ports: gateway `:5100`, platform `:5121`, auth `:5122`, loyalty `:5201`, shell `:5173`, loyalty-web `:5174`.

## Run (local, no Postgres)

APIs fall back to EF InMemory when `ConnectionStrings:Catalog/Tenant` is unset:

```bash
dotnet build ModulePlatform.sln
dotnet test ModulePlatform.sln
dotnet run --project src/Platform/Platform.Api      # :5121
dotnet run --project src/Auth/Auth.Api              # :5122
dotnet run --project src/Gateway/Gateway.Api        # :5100
dotnet run --project src/Modules/Loyalty/Loyalty.Api # :5201
```

Frontend:

```bash
cd apps/shell && npm install && npm run dev        # :5173 (proxies /api/v1 -> :5100)
cd modules/loyalty/web && npm install && npm run dev # :5174 remoteEntry.js
```

## Run (Postgres)

```bash
docker compose up -d postgres
export ConnectionStrings__Catalog="Host=localhost;Username=module_platform;Password=module_platform;Database=module_platform"
export ConnectionStrings__Tenant="Host=localhost;Username=module_platform;Password=module_platform;Database=module_platform"
dotnet run --project src/Platform/Platform.Api
```

## Key contracts

- `manifest.yml` is platform-only (R10). Browser uses `GET /api/v1/tenant/frontend-bootstrap` (SR-6b).
- Lifecycle: `validate -> deps topo-sort -> INSTALLING -> gateway routes -> tenant seed -> INSTALLED+ENABLED` (SR-5), per-(tenant,module) lock (R8).
- Gateway: disabled module `503 MODULE_DISABLED`, uninstalled `404`; admin writes validated before apply, persisted to `ocelot.json`.
- Permission = role permissions UNION user additional, ACTIVE only (A-BR3).
