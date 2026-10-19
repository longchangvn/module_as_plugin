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

- [x] P15 .NET Aspire orchestration (VERIFIED LIVE): `src/Aspire/
  ModulePlatform.AppHost` (net9.0, Aspire 9.5.2) + `src/Aspire/
  ModulePlatform.ServiceDefaults` (net8.0+net9.0; resilience, discovery,
  OTel, `/health`+`/alive`) wired into all 4 APIs via `AddServiceDefaults()`/
  `MapDefaultEndpoints()`. AppHost: Postgres pinned to host `:5434`... (see
  gotcha: pinning works via proxy — app ports bind direct, PG via proxy),
  `Catalog`/`Tenant` database resources, 4 APIs + 3 npm apps on fixed ports,
  full `TenantStores` env wired to Aspire Postgres. Verified live on a fresh
  volume: dashboard `:15038`, all APIs/frontends healthy, platform login →
  tenant create `202`/Provisioning → tenant login (lazy migrate + bootstrap)
  → user/role/assign `201` → gateway `/api/v1/admin/*` `200`; `aspire1`
  schema has 7 tables, no `GatewayRules`. API code otherwise untouched.
  Gotchas fixed: AppHost csproj must be `Microsoft.NET.Sdk` + nested
  `Aspire.AppHost.Sdk`; JS hosting is `Aspire.Hosting.NodeJs` in 9.x;
  `UseServiceDiscovery` is now `AddServiceDiscovery`; `AddPostgres` password
  takes a Parameter; port pinning needs `WithEndpoint("http", e => {
  e.Port = …; e.IsProxied = false; })` statement-lambdas (expression lambdas
  bind the wrong overload; default `IsProxied=true` makes DCP proxy the port
  and the app crash on bind); AppHost needs `Properties/launchSettings.json`
  (dashboard `:15038` + OTLP `:4317`/`:4318`). P15b migration-ordering bug
  found by the fresh Aspire DB: shared-DB/shared-history `GatewayRules`
  collided (`42P07`) depending on migrate order → renamed catalog table to
  `PlatformGatewayRules`, removed it from tenant `InitialTenant`, made the
  drop `IF EXISTS`; convergence matrix-tested both orders on fresh DBs +
  existing DB. `.vscode/launch.json` gained `Aspire: Full Stack (AppHost)`;
  README/SETUP document the one-command run. 58/58 tests.

- [x] P16 Per-tenant custom connection string (VERIFIED LIVE): dedicated-only
  optional `dbConnectionString` at create (collapsed UI section; `Host` +
  `Database` required, `shared` rejected, immutable after → 422); its
  `Database` honored, tables under slug-derived schema (`SanitizeSchemaSlug`);
  uniqueness = (server, database, schema) + same-server dedicated-DB guard.
  Stored Data-Protection-encrypted (`DbConnectionEncrypted`, `[JsonIgnore]`,
  `hasCustomConnection` flag only in responses); keys persisted via
  `DataProtection:KeyDirectory`. Provisioning creates DB (3D000-aware) +
  schema on the custom server; Auth resolves via cluster-internal
  `/internal/tenants/{id}/connection` (`X-Internal-Key`) into schema-qualified
  store. Verified live: custom tenant → 202 → schema/tables/login/bootstrap
  on custom server, ciphertext≠plaintext, zero secret leakage in GETs,
  422/400/401 guards, default flow unchanged, QA data cleaned. Docs
  (README/SETUP/PROGRESS/db/README/spec P-BR3/§9.1/§10.1) + Postman-worthy
  curl examples updated. 69/69 tests.

- [x] P17 Profile page for logged-in users (platform + tenant), VERIFIED LIVE:
  `GET|PATCH /platform/auth/me` + `POST /platform/auth/change-password`
  (Platform; `PlatformAny`, hash never returned, name 1–200, new password
  8–200 stored as `sha256:…`, password-change audited) and `GET|PATCH
  /auth/me` + `POST /auth/change-password` (Auth; tenant-usability gated,
  roles + effective permissions included, hash never returned). Shell
  `/profile` + tenant-admin `/profile` pages (editable name, readonly email,
  related info, Change-password dialog) wired into nav. Fixed `sub`→
  NameIdentifier claim mapping in `ActorId`/tenant caller id (was 401).
  Verified live on Postgres :5433: me/patch/change-pw happy paths + 400/401
  guards on both APIs, hashes confirmed in DB, QA names restored; `tsc`
  clean + `vite build` clean on both SPAs; 69/69 tests.

## Done (this session)

- [x] P18 Must-have gap closure from the P10 audit (VERIFIED LIVE on Postgres :5433
  + 92/92 tests):
  - **Argon2id passwords (A1)** — new `Shared/SharedKernel/PasswordHasher.cs`
    (Argon2id, PHC-ish `argon2id$v=19$m=…`, fixed-time compare). Wired into
    Platform login + `PlatformUserEndpoints` and Auth login/profile/user-create.
    Dev seed `dev-stub` and tenant `null` hashes now **claim-on-first-login**:
    Development accepts any password once, stores Argon2id, enforces afterwards
    (legacy `sha256:` verified + transparently upgraded). Production fails closed
    (no stub). Password policy 8–200. New `POST /platform/users/{id}/reset-password`
    + activate/deactivate; users list no longer returns hashes. Docs updated
    (README/SETUP no longer claim "any password in v1" as a stub).
  - **Gateway hot-swap (A2)** — `GatewayStore.ValidatePersistSwapAsync` builds a
    candidate Ocelot config via `IInternalConfigurationCreator`, validates it,
    atomically persists, then swaps into `IInternalConfigurationRepository`
    (no process restart). `ReloadAsync` returns `(ok, error)`; admin reload
    surfaces `400 CONFIG_INVALID` and leaves the running config intact.
  - **Downstream URL (A3)** — `ResolveModuleDownstream` honors the published
    `ArtifactApiUrl` (request `baseUrl`) over the `:5200` fallback; install now
    routes to the module's real port (verified `:5201`).
  - **`GET /platform/modules/{id}` (A4)** — module detail + `versions[]`.
  - **Platform user lifecycle (A5)** — activate/deactivate/reset-password +
    `/auth/me` GET/PATCH + `/auth/change-password` (verified current pw).
  - **Auth resolve cache (A6)** — `PermissionVersionCache` keyed
    (tenant,user,version), 60s TTL, bumped on role/permission/seed mutations;
    resolve no longer hits the DB every call.
  - **Distributed install lock (A7)** — `InstallLock` layers a PG
    `pg_advisory_xact_lock` (deterministic SHA-256 key) under the in-process
    `SemaphoreSlim`, across install/upgrade/enable/disable/uninstall. **Fixed a
    regression:** the advisory-lock transaction must `Commit()` on dispose — EF
    rolls back a disposed un-committed transaction, which silently dropped the
    install row + audits while out-of-band side effects (gateway HTTP, tenant-DB
    seed) survived. Now commit-on-dispose (`LockHandle`).
  - **Lifecycle integration tests (A8)** — `tests/Platform.Application.Tests/`
    (was empty): 12 tests over a fake `IModuleLifecyclePorts` — install happy
    path, unknown tenant/module, disabled tenant, idempotent reinstall,
    dependency auto-install order, SR-5 FAILED-on-side-effect-failure, upgrade
    guards (not-installed / newer-only / missing-dep), `CanUninstall`
    DEPENDENCY_IN_USE. Plus 11 `PasswordHasher` tests.
  - **Compose completeness (A9)** — added `loyalty` (:5201), `loyalty-web`
    (:5174), `tenant-admin` (:5175) services + Dockerfiles for the two SPAs;
    `dpkeys` volume for Platform DataProtection keys; fixed all API Dockerfiles
    to COPY `ServiceDefaults` csproj before `dotnet restore`.
  - **Lifecycle metrics/logs (A10)** — `ModulePlatform.Lifecycle` meter
    (`module.install/upgrade.count`, `module.install.duration`) + structured
    `module.install tenant=… result=…` log lines.

## Done (this session)

- [x] P19 Phase 1 security hardening (gap follow-up; 107/107 tests):
  - **Fail-closed secrets** — `RequireSecret` (ServiceDefaults) for
    `Jwt:SecretKey` (all 4 APIs) + `InternalApi:Key` (Platform, Auth):
    non-Development startup aborts on missing / <32 chars / dev default;
    hardcoded fallbacks removed (`JwtMint.Secret` no longer defaults).
    Compose requires `JWT_SECRET` + `INTERNAL_API_KEY` (`.env.example`).
    Verified live: Production with no secret / dev secret / no internal key →
    exit with `Required secret …`; strong secrets → listening.
  - **Credential rate limit (FR-A5)** — gateway global limiter, 10/min per
    client IP on POST login/change-password (platform + tenant) →
    `429 RATE_LIMITED` + `Retry-After`. Verified live: 10×401 then 429;
    non-credential paths unaffected.
  - **LAST_ADMIN (edge case 12)** — `LastAdminGuard` in Auth on user
    deactivate, user-role unassign, role deactivate, `seed:revoke`; Platform
    uninstall now revokes seed *before* removing gateway routes and maps the
    refusal to `409 LAST_ADMIN`. 15 new unit tests (guard + secret policy).
    Live LAST_ADMIN check pending: Docker Desktop VM hit disk I/O errors
    (Postgres `pg_filenode.map`), rerun `/tmp/phase1_verify.sh`-style flow.
  - Docs: README/SETUP/.env.example, requirement §9.1 (stub-password text) +
    §12 (`LAST_ADMIN`, `RATE_LIMITED`).

## Done (this session)

- [x] P20 Sample test modules (`specs/001-sample-test-modules`): `sample1`/
  `sample2` APIs (:5202/:5203; `/api/v1/data` open, `/api/v1/protected`
  gated by `SAMPLE_ONE|TWO_DATA_PROTECTED` via Auth resolve, no module cache),
  validated manifests (`modules/sample*/manifest.yml`, seed role "Sample N
  Viewer"), federated remote pages (:5176/:5177) with permission-aware
  buttons, shared `SharedKernel/ModuleGuards`, Tenant Admin as Federation
  host (Home "Installed modules", nav, lazy remote routes), AppHost/compose/
  Dockerfiles. Tests: manifest validation + seed-preview parity + guard
  logic (117/117). Permission codes can't contain digits
  (`^[A-Z]+_[A-Z_]+$`), hence `SAMPLE_ONE/TWO`.

- [x] P21 Shell Module Catalog rework: "All modules" default view (registry
  cards: latest version, dependencies, Available/Deprecated, installed-tenant
  count), multi-tenant **Install to tenants…** dialog (per-tenant install API
  called sequentially, per-tenant result, version defaults to latest),
  **Register module** dialog (Admin; manifest.yml + API URL → existing
  publish API), tenant view lists only Installed/Disabled/Failed with
  lifecycle actions + Retry install, Maintainer read-only. Backend:
  `GET /platform/modules` now semver-sorted (`1.10.0` > `1.9.0` fix) with
  `versions[]`, `dependencies[]`, `installedTenantCount`, status as string;
  new `GET /platform/modules/{id}/installs`. 118/118 tests; shell tsc + build
  clean. Open risk R-GW1 documented (gateway state global).

## Next up

1. P10: continue the `verification_guidline.md` pass (black/gray/white-box) over
   the remaining §5–§14 requirements; the gap items above are verified.
2. **PENDING APPROVAL — gateway per-tenant gating** (fixes open risks
   **R-GW1** global module state and **R-GW2** unauthenticated
   `/api/v1/gateway/internal/module-routes`, CRITICAL). Proposal + 3 decisions
   (D1 push+reconcile, D2 version skew, D3 JWT at gateway):
   `docs/proposal-gateway-per-tenant-state.md`. Resume: get D1–D3 answered →
   implement G1–G5, P1–P3 → two-tenant matrix. Risk register:
   `docs/gap-closure-report.md` → "Known risks".
3. Gap Phase 2 (required): revocable sessions (`admin_sessions`, A-BR1/A-BR4),
   tenant menus CRUD + tenant-admin page, asymmetric JWT signing (RS256;
   Platform/Auth sign, others verify). Phase 3: OIDC RP login (needs IdP
   decision). Not required by spec (backlog): OCI packaging, SBOM/Cosign/SLSA,
   K8s hardening, policy-as-code, CloudEvents, OpenFeature.
   Known gap: the tenant bootstrap admin gets no `*Admin*` role, so it is not
   `tenant_admin` until someone assigns one. `:5121`/`:5432` occupied on this
   machine by other projects (use alt ports / 5433 override).
   Run verification after each phase.

## Notes / decisions

- Found during P20 (not fixed): (1) gateway module enabled/disabled state and
  uninstall route removal are global, not per tenant (`GatewayStore._modules`);
  (2) `GET /api/v1/auth/permissions/resolve` is anonymous and routed publicly by
  the gateway (`/api/v1/auth/{rest}`) — add to Phase 2 security; (3) the shell's
  remote loader (`<script>` + `window[scope]`) is incompatible with originjs ESM
  `remoteEntry.js`; Tenant Admin uses the `virtual:__federation__` runtime API
  instead. Loyalty's manifest `remoteUrl` path does not match its build output,
  so its page shows "Module unavailable" if installed.

- Ports: gateway :5100, platform :5121, auth :5122, loyalty :5201, shell :5173, remote :5174, tenant-admin :5175.
- `:5121` on this machine is occupied by another `LoyaltyOS.Platform.API`; smoke tests use `--no-launch-profile ASPNETCORE_URLS=http://localhost:51xx`.
- EF: physical PostgreSQL only (`ConnectionStrings:Catalog/Tenant` required, fail fast; no InMemory fallback).
- Gateway module install states are in-memory (restart loses them) until P7.
- Password verification uses **Argon2id** (`Shared/SharedKernel/PasswordHasher.cs`). Dev seed `dev-stub` and tenant `null` hashes are claim-on-first-login (Development only): first login stores Argon2id, Production fails closed. Legacy `sha256:` verifies + upgrades on login.
