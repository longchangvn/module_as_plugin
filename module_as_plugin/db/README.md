# Database roles

Two Postgres roles (local dev defaults in `docker-compose.yml` / `.env.example`):

| role             | powers        | purpose                                              |
| ---------------- | ------------- | ---------------------------------------------------- |
| `module_platform`| superuser (seed `POSTGRES_USER`) | runtime app DML (`ConnectionStrings__Catalog/Tenant`) |
| `module_migrator`| `LOGIN + CREATEDB` | create databases + author/apply EF migrations        |

The migrator owns schema objects created during migration; the app role gets
DML (`SELECT/INSERT/UPDATE/DELETE`, sequence usage) on current + future
objects via `ALTER DEFAULT PRIVILEGES`. `CREATEDB` covers future
per-tenant `CREATE DATABASE tenant_*` provisioning.

## Fresh install

```bash
cd module_as_plugin
cp .env.example .env   # adjust POSTGRES_MIGRATOR_PASSWORD / POSTGRES_PORT
docker compose up -d postgres
# verify:
PGPASSWORD=module_migrator psql -h localhost -p 5432 -U module_migrator \
  -d module_platform -c "SHOW server_version; SELECT rolcreatedb FROM pg_roles WHERE rolname='module_migrator';"
# expect: rolcreatedb = t
```

`db/init/01-migrator.sh` runs automatically on first init (empty `pgdata`).

## Existing data dir (retrofit)

`docker-entrypoint-initdb.d` scripts do NOT re-run. Apply once manually:

```bash
PGPASSWORD=module_platform psql -h localhost -p 5432 -U module_platform \
  -d module_platform -f db/retrofit-migrator.sql
```

This creates the role, moves `public`-schema object ownership to it, and
re-grants DML to the app role. Database ownership stays with
`module_platform`. Set the migrator password first if it differs from the
default (`ALTER ROLE module_migrator WITH PASSWORD '...'`).

## Create + apply migrations as `module_migrator`

```bash
cd module_as_plugin
export MIGRATION_CONNECTION_STRING="Host=localhost;Username=module_migrator;Password=module_migrator;Database=module_platform"
# catalog (Platform.Infrastructure):
dotnet ef migrations add <Name> --project src/Platform/Platform.Infrastructure --startup-project src/Platform/Platform.Api
dotnet ef database update --project src/Platform/Platform.Infrastructure --startup-project src/Platform/Platform.Api
# tenant (Auth.Api):
dotnet ef migrations add <Name> --project src/Auth/Auth.Api --startup-project src/Auth/Auth.Api
dotnet ef database update --project src/Auth/Auth.Api --startup-project src/Auth/Auth.Api
```

Both `*DbContextFactory` classes default to the migrator connection string
above, so the export can be omitted for default local passwords/ports
(add `;Port=5433` when using the alternate port).

Runtime APIs keep using `module_platform` and call `MigrateAsync()` on
startup when a connection string is set — no change needed there.

## Tenant connection secrets + Data Protection keys

Per-tenant custom connection strings (`tenants.DbConnectionEncrypted`) are
ciphertext (ASP.NET Core Data Protection, purpose `TenantDbConnection:v1`).
The DB alone cannot decrypt them — the key ring can:

- Local/dev: `DataProtection:KeyDirectory` (default `./keys` under the
  Platform content root).
- Compose/Aspire: mount a persistent volume at that directory.

Back up the key directory with the database. If keys are lost, affected
tenants report `provision-failed` ("stored connection cannot be decrypted")
and must have their connection re-entered (recreate the tenant — the value is
immutable). Never commit keys; rotate `InternalApi:Key` per environment.
