# Implementation Progress — Module-as-Plugin Platform

Requirement: `requirement/01-module-platform-requirements.md` (+ `ocelot_gateway_admin.md`).
Workdir: `module_as_plugin/`. Updated as work proceeds — if interrupted, resume from **Next up**.

## How to verify current state

```bash
cd module_as_plugin
dotnet build ModulePlatform.sln --nologo -v q   # expect 0 warnings, 0 errors
dotnet test ModulePlatform.sln --nologo -v q    # expect all pass
cd apps/shell && npx tsc --noEmit               # expect clean
cd ../tenant-admin && npx tsc --noEmit          # expect clean
```

## Done

- [x] P0 Solution + Clean Architecture skeleton (SharedKernel, Platform D/A/I/Api, Auth, Gateway, Loyalty sample, tests)
- [x] P1 Manifest validator + Semver resolver (`Semver` lib) + permission union + menu tree, 9 unit tests
- [x] P2 Platform registry/lifecycle/catalog/bootstrap APIs; Auth users/roles/resolve; Gateway Ocelot + admin + traffic; Loyalty sample API; shell host + loyalty remote (both `vite build` clean)
- [x] P3 Runtime smoke (health/ready/swagger, gateway summary=2 services, traffic) — alt ports (see Notes)

## In progress

- [ ] P10 Requirement verification pass (`verification_guidline.md`: black/gray/white-box vs §5–§14)

## Done (this session)

- [x] P8 Frontend: login (platform/tenant), catalog with lifecycle actions (install/enable/disable/
  uninstall/upgrade + plan preview) for tenant + platform sessions, tenants CRUD, gateway overview/
  services (+health probe, delete guard surfaced)/routes/traffic (5s refresh + reset). `tsc` + build clean
- [x] P9 EF Postgres migrations (`InitialCatalog`, `InitialTenant` + design-time factories),
  auto-`MigrateAsync` on startup when a connection string is set, Dockerfiles (platform/auth/gateway/
  loyalty/shell+nginx), compose updated (service URLs, shell, gateway persistence notes).
  Verified: `docker compose config` ok; migrations applied to real Postgres 16 (tables + history);
  login works against Postgres
- [x] P4–P7 as recorded below (E2E verified live)

## Done (this session)

- [x] P4 Cross-service side effects: Platform→Gateway route push/remove (`internal/module-routes`),
  Platform→Auth seed apply/revoke (`internal/seed:apply|revoke`, idempotent upserts, R6 revoke)
- [x] P5 Dev login + JWT mint (`JwtMint` in SharedKernel; platform `/auth/login`, tenant `/auth/login`
  with `tenant_admin` claim); dev auto-seed (`admin@local`, `tnt_demo`); password verify stubbed (noted)
- [x] P6 Upgrade path (`UpgradeAsync` + platform/tenant endpoints; newer-only guard; migrate-hook via
  `artifactApiUrl`; additive seed) + full tenant self-service (detail + install plan preview,
  enable/disable/uninstall/upgrade) with `CanWrite` (Admin or tenant_admin+matching tenant)
- [x] P7 Gateway `gateway-modules.json` persistence, PUT/DELETE service (BR-R2 409), PUT route,
  traffic reset, `?health=true` TCP probe; uninstall removes state (404, not 503)
- [x] E2E verified live: login → publish (valid 201, invalid 400 with fields, 2nd version ok after
  same-module scope fix) → install 1.0.0 → disable (503 `MODULE_DISABLED`) → enable → upgrade 1.1.0
  (downgrade rejected) → uninstall (404 `UNKNOWN_MODULE_ROUTE`, perms revoked) → bootstrap returns
  Vite-only keys for tenant JWT
- [x] Semver lib corrections: `SemVersion` has no operators/`IComparable`; all ordering via
  `SemverOrdering` (`ComparePrecedenceTo`); `SemverOrdering.Latest` unit-tested (10 tests pass)

## Done (this session)

- [x] P11 Multi-tenant Phase A (lifecycle + enforcement, greenfield): tenant
  validation (V-C1..C5, 400 field list) + `409 DUPLICATE_TENANT` (name/slug/
  domain/pathPrefix, CI); PATCH (immutable dbName/schema/slug → 422);
  disable (reason required) / enable + audit (`tenant.*`, incl. audits API);
  usability guard (unknown → 404, disabled → 403) on bootstrap, tenant +
  platform lifecycle, Auth login/resolve, loyalty module APIs; `X-Tenant-Id`
  mismatch → 403 (BR-R2); internal status endpoint (30s cache, 503 fail
  closed); shell tenants UI (create/edit/disable/enable/audit, badges);
  23 new unit tests (validator + SR-4 resolution order). Verified live
  end-to-end (disable → login/bootstrap/members/resolve all 403 → enable →
  200). Remaining: per-tenant DB provisioning (Phase B), Argon2/OIDC (Phase C).

- [x] P12 Multi-tenant Phase B (provisioning, greenfield): `DeploymentMode` /
  `DbStatus` / `DbError` / `SchemaVersion` fields + migration; validated
  `dbName`/`schema` (V-C6/V-C7, 400/409 incl. dedicated-scoped dbName and
  schema uniqueness); `202` create with synchronous `CREATE DATABASE`-if-missing
  (reuse) / `CREATE SCHEMA` via privileged connection; lazy `MigrateAsync` per
  tenant store in Auth (schema-stamped DDL generator + per-schema history +
  `search_path`, per-tenant model cache) with Ready/Failed callbacks;
  `provision:retry`; bootstrap users via `X-Tenant-Id`; shell deployment radio
  + store badges. Verified live on Postgres: dedicated DB (9 tables+history),
  shared schema (9 tables+history, 0 cross-schema FKs), same email isolated in
  both, disable/enable gates, 46/46 tests. Remaining: Argon2/OIDC (Phase C).

- [x] P13 Tenant bootstrap admin: `bootstrapAdminEmail/Name` at creation
  (validated, immutable, stored on tenant row), seeded as first tenant user on
  first store use — first tenant login works immediately; shell fields;
  `01-module-platform-requirements.md` updated (P-BR3, §9.1, §10.1).
  Verified live (create → login → user row + Active/Ready; 400/422 guards).
  48/48 tests.

- [x] P14 Login split + TenantAdmin portal + assignments + configurable path prefix
  (see above for detail).
  new `apps/tenant-admin` SPA (:5175, same React/Vite/Tailwind/shadcn stack):
  tenant-only login (no platform link) → Home (counts + effective permissions),
  Users (CRUD list/create/activate-deactivate + detail with role assignment and
  direct permission grants), Roles (ditto + permission grants), read-only
  Permissions catalog. Backend: `TenantRouting:PathPrefix` config (default
  `/t`) drives validation/defaulting/resolution; new Auth endpoints
  (`users/{id}/roles`, `roles/{id}/permissions`, `users/{id}/permissions`
  GET/POST/DELETE + `PATCH users/{id}`/`roles/{id}`; `UNKNOWN_USER/ROLE/
  PERMISSION`, dup `409`). Gateway rules moved to platform catalog
  (`MoveGatewayRulesToPlatform` + `DropGatewayRulesFromTenant`; lifecycle
  records rules on apply, removes on uninstall). Docs (README/SETUP/PROGRESS/
  Postman) updated. Verified live (assign/revoke/activate flows, dup-name 409,
  custom-base 201+400; QA rows cleaned). 58/58 tests + both `tsc` clean.

- [ ] P15 .NET Aspire orchestration (IN PROGRESS, builds clean, not yet run
  green): `src/Aspire/ModulePlatform.AppHost` (net9.0, Aspire 9.5.2) + fresh
  `src/Aspire/ModulePlatform.ServiceDefaults` (net8.0+net9.0; resilience,
  discovery, OTel, `/health`+`/alive`) wired into all 4 APIs via
  `AddServiceDefaults()`/`MapDefaultEndpoints()`. AppHost: Postgres pinned to
  host `:5434` (volume `module-platform-pgdata`, fixed dev password),
  `Catalog`/`Tenant` database resources, 4 APIs + 3 npm apps on fixed ports,
  full `TenantStores` env wired to Aspire Postgres. API code otherwise
  untouched (no behavior change outside Aspire). Gotchas fixed along the way:
  AppHost csproj must be `Microsoft.NET.Sdk` + nested `Aspire.AppHost.Sdk`
  element; JS hosting is `Aspire.Hosting.NodeJs` in 9.x (not `.JavaScript`);
  `UseServiceDiscovery` is now `AddServiceDiscovery`; `AddPostgres` password
  takes a Parameter (`AddParameter` with value); port pinning must use
  `WithEndpoint("http", e => { e.Port = …; })` statement-lambdas (expression
  lambdas bind the wrong overload); AppHost needs `Properties/
  launchSettings.json` (dashboard URL `:15038` + OTLP `:4317`/`:4318` + allow
  unsecured transport). `.vscode/launch.json` gained `Aspire: Full Stack
  (AppHost)`; README/SETUP document the one-command run. 58/58 tests.

## Next up

1. P15 Aspire — RESUME HERE: run `dotnet run --project src/Aspire/ModulePlatform.AppHost`
   (from `module_as_plugin/`, WITHOUT `--no-launch-profile` — the profile feeds
   the dashboard its URL/OTLP env). Then verify: dashboard `:15038`, APIs
   `:5100/:5121/:5122/:5201`, frontends `:5173/:5174/:5175`, Postgres `:5434`
   (fresh volume `module-platform-pgdata`, auto-migrated + seeded), platform
   login → create tenant (expect 202 provisioning via wired TenantStores) →
   tenant login → assignments. F5 sessions were stopped for port conflicts;
   restart via F5 or use Aspire. NOTE: `module_as_plugin/.gitignore` is
   untracked — `src/Aspire/` is new/untracked; nothing committed.
2. P10: run the `verification_guidline.md` pass and fix gaps.
2. Known deferred items: real Argon2 password verify, per-tenant DB/schema provisioning
   (single tenant store today), Ocelot in-memory hot-swap (file rewrite + reload today),
  `:5121`/`:5432` occupied on this machine by other projects (use alt ports / 5433 override).
  Run verification after each phase.

## Notes / decisions

- Ports: gateway :5100, platform :5121, auth :5122, loyalty :5201, shell :5173, remote :5174, tenant-admin :5175.
- `:5121` on this machine is occupied by another `LoyaltyOS.Platform.API`; smoke tests use `--no-launch-profile ASPNETCORE_URLS=http://localhost:51xx`.
- EF: physical PostgreSQL only (`ConnectionStrings:Catalog/Tenant` required, fail fast; no InMemory fallback).
- Gateway module install states are in-memory (restart loses them) until P7.
- Password verification is stubbed (random hash at create); real Argon2 verify is a P5 follow-up note, not silently claimed.
