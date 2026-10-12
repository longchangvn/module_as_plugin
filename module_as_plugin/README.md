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

## Tenant lifecycle (P-BR3, SR-4)

- Validation on create/update: name 3–100 unique (CI), slug 3–50 `^[a-z0-9-]+$`
  with reserved words blocked, optional domain (lowercase hostname) / pathPrefix
  (`/t/{slug}`), resolutionMode `domain|path|header`, valid contact email.
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
  immediately and Auth uses the legacy single store (no-Postgres dev).
- Bootstrap users are created with `X-Tenant-Id` into the tenant's store.
  Retry: `POST /platform/tenants/{tid}/provision:retry`. Restarts lose
  InMemory catalog rows; tenant DBs/schemas in Postgres persist.
- Bootstrap admin: pass `bootstrapAdminEmail` (+ optional `bootstrapAdminName`)
  at creation; it is seeded as the first tenant user on first store use, so the
  first tenant login with that email succeeds (any password in v1).
- Config (`TenantStores`): `Host/Port/Username/Password/SharedDatabase` for
  building tenant connections server-side (never in catalog traffic, NFR4);
  `ProvisioningConnection` for DDL. See `.env.example` and `docker-compose.yml`.
