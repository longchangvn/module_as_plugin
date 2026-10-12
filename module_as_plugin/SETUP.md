# Setup Guideline

End-to-end local setup for `module_as_plugin/`: start the app, create the
database + apply migrations (with platform seed data), then create 1 sample
tenant with seed data.

Ports: gateway `:5100`, platform `:5121`, auth `:5122`, loyalty `:5201`,
shell `:5173`, loyalty remote `:5174`, postgres `:5432`
(`:5432` is occupied on some machines — use `POSTGRES_PORT=5433` then).

## 0. Prerequisites

```bash
dotnet --version   # .NET 8 SDK
node --version     # Node 18+ for shell / remote
docker --version   # only for the Postgres path
dotnet tool install --global dotnet-ef  # only to author new migrations
```

All commands below run from `module_as_plugin/` unless noted.

## 1. Start the app

APIs require physical PostgreSQL (`ConnectionStrings:Catalog/Tenant` fail
fast when unset — no InMemory fallback).

```bash
cp .env.example .env   # set POSTGRES_PORT=5433 if :5432 is taken
docker compose up -d postgres
# or everything containerised:
docker compose up --build
```

Run APIs against Postgres (one terminal per service, or use compose):

```bash
export ConnectionStrings__Catalog="Host=localhost;Username=module_platform;Password=module_platform;Database=module_platform"
export ConnectionStrings__Tenant="Host=localhost;Username=module_platform;Password=module_platform;Database=module_platform"
export TenantStores__Host=localhost
export TenantStores__Port=5432  # 5433 if you overrode POSTGRES_PORT
export TenantStores__Username=module_platform
export TenantStores__Password=module_platform
export TenantStores__SharedDatabase=module_platform
export TenantStores__ProvisioningConnection="Host=localhost;Username=module_migrator;Password=module_migrator;Database=postgres"
export Platform__BaseUrl=http://localhost:5121
export Auth__BaseUrl=http://localhost:5122

dotnet run --project src/Platform/Platform.Api
dotnet run --project src/Auth/Auth.Api
dotnet run --project src/Gateway/Gateway.Api
dotnet run --project src/Modules/Loyalty/Loyalty.Api

# new terminals:
cd apps/shell && npm install && npm run dev          # :5173 platform portal
cd modules/loyalty/web && npm install && npm run dev # :5174 remoteEntry.js
cd apps/tenant-admin && npm install && npm run dev   # :5175 tenant backoffice
```

Frontends proxy `/api/v1 -> :5100`.

Health check:

```bash
curl localhost:5121/healthz; curl localhost:5122/healthz
curl localhost:5100/healthz; curl localhost:5201/healthz
```

## 2. Database creation + migrations + platform seed data

Two roles (see `db/README.md`): `module_platform` (runtime DML) and
`module_migrator` (`LOGIN + CREATEDB`, owns migrated objects).

### 2.1 Create the database + roles

`docker compose up -d postgres` already creates database `module_platform`
and both roles via `db/init/01-migrator.sh` (first init only). Verify:

```bash
PGPASSWORD=module_migrator psql -h localhost -p 5432 -U module_migrator \
  -d module_platform -c "SHOW server_version; SELECT rolcreatedb FROM pg_roles WHERE rolname='module_migrator';"
# expect rolcreatedb = t
```

Existing data dir (init script did not run)? Retrofit once:

```bash
PGPASSWORD=module_platform psql -h localhost -p 5432 -U module_platform \
  -d module_platform -f db/retrofit-migrator.sql
```

### 2.2 Apply migrations

You normally do **nothing**: on startup each API calls `MigrateAsync()`
when its connection string is set (Platform → catalog `InitialCatalog` +
`TenantProvisioningFields` + `TenantBootstrapAdmin`; Auth → tenant
`InitialTenant`). Per-tenant stores migrate lazily on first use (§3).

Manual equivalent (as `module_migrator`):

```bash
export MIGRATION_CONNECTION_STRING="Host=localhost;Username=module_migrator;Password=module_migrator;Database=module_platform"
# add ;Port=5433 to the string above if you use the alternate port

dotnet ef database update --project src/Platform/Platform.Infrastructure --startup-project src/Platform/Platform.Api
dotnet ef database update --project src/Auth/Auth.Api --startup-project src/Auth/Auth.Api
```

New migration (authoring only):

```bash
dotnet ef migrations add <Name> --project src/Platform/Platform.Infrastructure --startup-project src/Platform/Platform.Api
dotnet ef migrations add <Name> --project src/Auth/Auth.Api --startup-project src/Auth/Auth.Api
```

Check:

```bash
PGPASSWORD=module_platform psql -h localhost -U module_platform -d module_platform \
  -c "\dt" -c "SELECT * FROM \"__EFMigrationsHistory\";"
```

### 2.3 Platform seed data (automatic)

On first `Development` start with an empty catalog, Platform seeds
(`src/Platform/Platform.Api/Program.cs`):

| row | values |
| --- | ------ |
| platform admin | `admin@local` / any password (v1 stub-verify), role `Admin` |
| demo tenant | id `tnt_demo`, slug `demo`, `tenant_demo` / `public` |

Login to confirm:

```bash
curl -s -X POST localhost:5121/api/v1/platform/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@local","password":"admin"}'
```

## 3. Create 1 sample tenant with seed data

This creates tenant `Acme Retail`, provisions its store, seeds the bootstrap
admin on first login, then installs the `loyalty` sample module (which seeds
roles / permissions / users / menus).

### 3.1 Platform login (save token)

```bash
PLATFORM_TOKEN=$(curl -s -X POST localhost:5121/api/v1/platform/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@local","password":"admin"}' | python3 -c "import sys,json; print(json.load(sys.stdin)['data']['accessToken'])")
echo $PLATFORM_TOKEN
```

Via gateway (`:5100`) works identically — replace host with `:5100`.

### 3.2 Create the tenant (dedicated DB + bootstrap admin)

```bash
curl -s -X POST localhost:5121/api/v1/platform/tenants \
  -H "Authorization: Bearer $PLATFORM_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{
    "name": "Acme Retail",
    "slug": "acme",
    "contactEmail": "ops@acme.example",
    "bootstrapAdminEmail": "admin@acme.example",
    "bootstrapAdminName": "Acme Admin"
  }' | python3 -m json.tool
# -> 202 while Provisioning (TenantStores configured), else 201 Active
```

Notes:

- `deploymentMode` defaults to `dedicated` (`CREATE DATABASE tenant_acme`,
  reused if it exists). For a shared-schema tenant pass
  `"deploymentMode": "shared", "schema": "acme"` instead (creates schema).
- `slug` / `name` / `domain` / `pathPrefix` / `dbName` / `schema` must be
  unique — duplicates return `409 DUPLICATE_TENANT`.
- `pathPrefix` defaults to `{base}/{slug}` (`TenantRouting:PathPrefix`,
  default `/t`) when omitted; an explicit value must match that form.
- Save the returned `data.id` (e.g. `tnt_xxx`) as `TENANT_ID`:

```bash
TENANT_ID=<id-from-above>
```

### 3.3 First tenant login (migrates tenant store + seeds bootstrap admin)

The first Auth call for the tenant runs `MigrateAsync()` on its store,
inserts `admin@acme.example` as its first user, and reports `Ready/Active`
back to Platform:

```bash
TENANT_TOKEN=$(curl -s -X POST localhost:5122/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d "{\"tenantId\":\"$TENANT_ID\",\"email\":\"admin@acme.example\",\"password\":\"anything\"}" \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['data']['accessToken'])")
echo $TENANT_TOKEN

# confirm store is Ready:
curl -s localhost:5121/api/v1/platform/tenants \
  -H "Authorization: Bearer $PLATFORM_TOKEN" | python3 -m json.tool
# acme row: status Active, dbStatus Ready, schemaVersion set
```

Any password works in v1 (stub verify); production wires Argon2.

### 3.4 Publish + install the loyalty module (module seed data)

Publish once (Admin), then install into the tenant:

```bash
MANIFEST=$(python3 -c "import json; print(json.dumps(open('modules/loyalty/manifest.yml').read()))")
curl -s -X POST localhost:5121/api/v1/platform/modules \
  -H "Authorization: Bearer $PLATFORM_TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"manifestYaml\": $MANIFEST}" | python3 -m json.tool
# -> 201 { id: loyalty, version: 1.0.0 }

curl -s -X POST localhost:5121/api/v1/platform/tenants/$TENANT_ID/modules/loyalty/install \
  -H "Authorization: Bearer $PLATFORM_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{}' | python3 -m json.tool
# -> 202 INSTALLED
```

Install applies the manifest `api.seed` via Auth `internal/seed:apply`
(idempotent upserts) into **this tenant's store**:

- role `Loyalty Operator` (+ `ops@example.com` user + grants
  `LOYALTY_MEMBER_VIEW/UPDATE`)
- user `auditor@example.com` (+ direct `LOYALTY_MEMBER_VIEW` grant)
- menus `loyalty`, `loyalty.members`; the gateway route
  `/api/v1/loyalty/members/{rest}` is applied to the gateway and recorded in
  the platform catalog (`GatewayRules`), never in the tenant store

Verify:

```bash
# tenant login as seeded module user:
curl -s -X POST localhost:5122/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d "{\"tenantId\":\"$TENANT_ID\",\"email\":\"ops@example.com\",\"password\":\"anything\"}" | python3 -m json.tool

# seeded permissions visible (use X-Tenant-Id bootstrap header + platform token):
curl -s "localhost:5122/api/v1/auth/permissions?tenantId=$TENANT_ID" \
  -H "Authorization: Bearer $PLATFORM_TOKEN" -H "X-Tenant-Id: $TENANT_ID" | python3 -m json.tool

# frontend bootstrap shows the remote (tenant JWT):
curl -s localhost:5121/api/v1/tenant/frontend-bootstrap \
  -H "Authorization: Bearer $TENANT_TOKEN" | python3 -m json.tool

# module API through the tenant context:
curl -s localhost:5201/api/v1/members \
  -H "Authorization: Bearer $TENANT_TOKEN" | python3 -m json.tool
```

Shell check: sign in at `http://localhost:5173` (platform `admin@local`),
open Tenants → `acme` → Active/Ready, Catalog → loyalty INSTALLED+ENABLED.

### 3.5 Tenant Admin portal (`:5175`)

Tenant users sign in at `http://localhost:5175/login` (tenant ID + email +
password — no platform mode). The portal manages the tenant store:

- Home: user/role/permission counts + your effective permissions.
- Users: list/search/create, activate/deactivate, detail page for role
  assignment and direct (additional) permission grants.
- Roles: list/search/create, activate/deactivate, detail page for permission
  grants.
- Permissions: read-only catalog seeded by installed modules.

```bash
# assign a role (tenant JWT):
curl -s -X POST localhost:5122/api/v1/admin/users/<userId>/roles \
  -H "Authorization: Bearer $TENANT_TOKEN" -H 'Content-Type: application/json' \
  -d '{"roleId":"<roleId>"}' | python3 -m json.tool
# -> 201; duplicate -> 409; unknown user/role -> 404 UNKNOWN_USER/UNKNOWN_ROLE

# grant a direct permission:
curl -s -X POST localhost:5122/api/v1/admin/users/<userId>/permissions \
  -H "Authorization: Bearer $TENANT_TOKEN" -H 'Content-Type: application/json' \
  -d '{"permissionId":"<permissionId>"}' | python3 -m json.tool

# deactivate a user (blocked from login afterwards):
curl -s -X PATCH localhost:5122/api/v1/admin/users/<userId> \
  -H "Authorization: Bearer $TENANT_TOKEN" -H 'Content-Type: application/json' \
  -d '{"isActive":false}' | python3 -m json.tool
```

### 3.6 Reset / retry

```bash
# failed provisioning:
curl -s -X POST localhost:5121/api/v1/platform/tenants/$TENANT_ID/provision:retry \
  -H "Authorization: Bearer $PLATFORM_TOKEN" -H 'Content-Type: application/json' -d '{}'

# full local reset (Postgres):
docker compose down -v   # wipes pgdata; re-run §2-§3
```

Postman alternative: import `postman/ModulePlatform.postman_collection.json`
(Platform login → Create tenant (dedicated + bootstrap admin) →
Tenant login → Publish module → Install).
