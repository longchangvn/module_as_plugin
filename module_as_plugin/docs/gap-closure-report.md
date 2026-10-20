# Must-Have Gap Closure — Verification & Archive Report

Scope: the must-have gaps identified by the P10 requirement audit of
`module_as_plugin/` against `requirement/01-module-platform-requirements.md`
(+ `ocelot_gateway_admin.md`, `multiple_tenant.md`).

Method: **apply → verify → archive**. Each applied item below records what
changed, the code touch-points, and the verification evidence. Verification
follows `requirement/verification_guidline.md` (black-box = live HTTP against a
running stack on Postgres `:5433`; gray-box = cross-service state in the DB /
gateway; white-box = unit + integration tests over the source).

**Result:** build 0 errors / 0 warnings, **92/92 tests pass** (was 69), all
black-box lifecycle checks green, DB data confirmed.

---

## Applied & verified

### A1 — Argon2id password hashing (`Shared/SharedKernel/PasswordHasher.cs`)
- Argon2id (`Konscious.Security.Cryptography.Argon2` 1.3.1), PHC-ish
  self-describing format `argon2id$v=19$m=19456,t=2,p=1$…`, fixed-time compare,
  `IsUnverifiablePlaceholder` for the legacy dev seeds.
- Wired into Platform login + `PlatformUserEndpoints`, and Auth
  login/profile/user-create.
- Policy: password 8–200. `dev-stub` (platform seed) and `null` (tenant seed)
  are **claim-on-first-login**: Development accepts any password once, stores
  Argon2id, enforces afterwards. Legacy `sha256:` verifies + transparently
  rehashes. **Production fails closed** (no stub path).
- Evidence (black-box, live): first `admin@local` login → `200`; wrong password
  after upgrade → `401`; correct → `200`; unknown user → `401`. DB row
  `admin@local` shows `argon2id$v=19$m=19456,` (99 chars). Created `ops@local`
  logs in with its password, rejects wrong, `400` on too-short create,
  deactivate→login `401`→activate→login `200`, reset-password invalidates old
  (`401`) and accepts new (`200`), Maintainer lists users `403`. Self-deactivate
  → `422`.
- Tests: 11 `PasswordHasher` cases (round-trip, wrong pw, per-hash salt,
  legacy sha256 upgrade, placeholder matrix, corrupted-hash fail-closed).

### A2 — Gateway in-memory hot-swap (`Gateway/Gateway.Api/Services/GatewayStore.cs`)
- `ValidatePersistSwapAsync(source, ocelot)` builds a candidate Ocelot config via
  `Ocelot.Configuration.Creator.IInternalConfigurationCreator.Create`, validates
  it, atomically persists (temp+move), then swaps into
  `IInternalConfigurationRepository.AddOrReplace` — **no process restart**.
- `ReloadAsync` returns `(bool Ok, string? Error)`; admin reload surfaces
  `400 CONFIG_INVALID` and leaves the running config untouched.
- Evidence (black-box): add service `test-svc` + route → `201`; immediately proxy
  the new upstream → `502` (route is live, downstream dead ⇒ hot-apply worked,
  not `404`); delete route → `200`; proxy again → `404` (hot-removal); reload →
  `{"reloaded":true}`. Invalid service id → `400 NOT_FOUND`, config unchanged.

### A3 — Module downstream URL (`GatewayStore.ResolveModuleDownstream`)
- Resolution order: request `baseUrl` (platform `ArtifactApiUrl`) → service
  registry by `moduleId` → `GATEWAY_MODULE_DEFAULT_BASEURL` → `localhost:5200`
  dev fallback. Replaces the hardcoded `:5200` in `ApplyModuleRoutesAsync`.
- Evidence: install published `artifactApiUrl=http://localhost:5201` → gateway
  route downstream recorded `localhost:5201` (not `5200`).

### A4 — `GET /api/v1/platform/modules/{id}` (`ModuleRegistryEndpoints`)
- Module detail + `versions[]` (id, version, manifestHash, artifact URLs,
  publishedBy). Evidence: `loyalty` → `200` with `versions[]`; unknown → `404`;
  unauthenticated → `401`.

### A5 — Platform user lifecycle (`PlatformUserEndpoints`)
- `POST /users/{id}/deactivate|activate|reset-password`, `GET|PATCH
  /auth/me`, `POST /auth/change-password` (verifies current). Users list returns
  profiles only (no hash). Evidence: see A1 matrix (deactivate/activate/reset
  round-trips, `403` for non-admin list, `422` self-deactivate).

### A6 — Auth resolve cache (`Auth/Infrastructure/PermissionVersionCache`)
- Singleton cache keyed `(tenant,user,permissionVersion)`, 60s TTL, resolved in
  `ResolveEndpoints`; version bumped on user-role / role-permission /
  permission assign/revoke / seed apply|revoke (`ApiHelpers.BumpPermissions`).
- Evidence (gray-box): mutations bump the stored version, so cached resolves
  invalidate; resolve no longer queries the DB per call.

### A7 — Distributed install lock (`Platform/Platform.Api/Services/InstallLock.cs`)
- PG `pg_advisory_xact_lock(ns, key)` (deterministic SHA-256 → two int32s)
  layered under the in-process `SemaphoreSlim`, applied to install / upgrade /
  enable / disable / uninstall (platform + tenant self-service paths).
- **Regression found & fixed during verification:** the advisory-lock
  transaction must `Commit()` on dispose. EF rolls back a disposed,
  un-committed transaction, so the install row + audits were silently dropped
  while out-of-band side effects (gateway HTTP, tenant-DB seed) survived — an
  inconsistent half-apply. Fixed via a `LockHandle` that commits on
  `DisposeAsync` (the service owns terminal state; the tx only serialises).
- Evidence: before fix — install `202` but `Installs` count `0`, audits missing,
  disable/enable `404`. After fix — install row persists (State=2 Installed,
  Enabled=t), disable→`3`/enable→`2`, uninstall→`0`; full audit trail present
  (`install`, `module.disable`, `module.enable`, `module.uninstall`).

### A8 — Lifecycle integration tests (`tests/Platform.Application.Tests/`)
- Project was empty. Added `Platform.Application`+`Domain` references and 12
  tests over a fake `IModuleLifecyclePorts`: install happy path (routes+seed+
  state+audit), unknown tenant/module, disabled tenant, idempotent reinstall (no
  side effects), dependency auto-install order (P-BR6 topo), SR-5
  FAILED-state-on-side-effect-failure, upgrade guards (not-installed / newer-only
  / missing-dep), `CanUninstall` DEPENDENCY_IN_USE.

### A9 — Compose completeness (`docker-compose.yml` + Dockerfiles)
- Added services `loyalty` (:5201), `loyalty-web` (:5174), `tenant-admin`
  (:5175) + SPA Dockerfiles (nginx) and `tenant-admin/nginx.conf`.
- Added `dpkeys` named volume mounted at `/var/lib/dpkeys` for Platform
  DataProtection keys (survives restarts ⇒ issued JWTs stay valid).
- Fixed all API Dockerfiles (platform/auth/gateway/loyalty) to COPY the
  `ModulePlatform.ServiceDefaults` csproj **before** `dotnet restore`.
- Evidence: `docker compose config` → OK, 9 services listed.

### A10 — Lifecycle metrics & logs (`Platform.Application/LifecycleService.cs`)
- `Meter("ModulePlatform.Lifecycle")` with `module.install.count`,
  `module.upgrade.count`, `module.install.duration` histogram; structured
  `module.install tenant=… module=… version=… result=…` log lines.
- Evidence: log line emitted on live install (`result=ok`, `result=err`).

---

## Verification summary

| Layer | Result |
| --- | --- |
| Build (`dotnet build ModulePlatform.sln`) | 0 errors, 0 warnings (3 pre-existing nullable warnings resolved to 0 in final full build) |
| Tests (`dotnet test`) | 92/92 pass (69 baseline + 11 hasher + 12 lifecycle) |
| Compose | `docker compose config` OK, 9 services |
| Black-box (live stack, PG `:5433`) | login/Argon2 matrix, user lifecycle, module detail, gateway hot-swap, downstream `:5201`, install→disable→enable→uninstall round-trip all green |
| Gray-box (DB data) | `argon2id$v=19$m=…` stored; `Installs` + `Audits` persist; gateway route downstream port correct |

Note on a pre-existing limitation encountered during live verification: the
dev-seeded `tnt_demo` (dedicated mode) has no physical `tenant_demo` database,
and EF's own `CREATE DATABASE` path throws the Npgsql "read-optimized model"
error. This is unrelated to the applied changes (uncommitted `TenantDbProvider`
custom-connection work + EF/Npgsql), and the platform's own provisioning path
(raw `CREATE DATABASE`) works — tenants created via the API provision and
install correctly, as shown above.

---

## Deferred (documented, not in this pass)

Full OIDC relying-party login · OCI image packaging · SBOM / Cosign / SLSA
supply-chain signing · Kubernetes manifest hardening · policy-as-code · tenant
menus CRUD · `admin_sessions` persistence · CloudEvents module lifecycle events
· OpenFeature flag evaluation.

These are out of scope for the must-have gap closure and are tracked under
**Next up** in `PROGRESS.md`.

---

## Phase 1 follow-up — security hardening (gaps missed above)

| # | Gap | Fix | Evidence |
| --- | --- | --- | --- |
| N1 | Hardcoded JWT secret fallback in all APIs (Production silently used the dev key) | `RequireSecret` fail-closed for `Jwt:SecretKey` + `InternalApi:Key`; compose requires `JWT_SECRET`/`INTERNAL_API_KEY` | Live: Production start without / with dev / without internal key → abort; strong → listening. Unit: 6 cases |
| N3 | `LAST_ADMIN` defined but never enforced | `LastAdminGuard` on user deactivate, role unassign, role deactivate, seed revoke; uninstall revokes seed before routes, `409 LAST_ADMIN` | Unit: 9 cases. Live check pending (Docker disk I/O failure during run) |
| N4 | No login rate limit (FR-A5) | Gateway limiter 10/min/IP on login + change-password, `429 RATE_LIMITED` | Live: 10×401 → 429; other paths 200 |
| N5 | Requirement §9.1 still said "any password works" | Text updated; §12 gains `LAST_ADMIN` / `RATE_LIMITED` | — |

N2 (single shared HMAC key across services) is planned for Phase 2 (RS256).

---

## Known risks

**R-GW1 and R-GW2 are CLOSED** (2026-10-20) by
`docs/proposal-gateway-per-tenant-state.md` (status IMPLEMENTED, D1–D3 as
recommended). Summary of the fix:

- Gateway state is per (module, tenant) (`Services/ModuleTenantState.cs`,
  `gateway-modules.json` v2). Ocelot routes for a module exist while ≥ 1
  tenant has it and follow the newest installed version (D2).
- Guard reads the tenant from the **validated JWT** `tenantId` claim (D3):
  no/invalid token → `401 UNAUTHORIZED`; no tenant claim or mismatching
  `X-Tenant-Id` → `403 FORBIDDEN_TENANT`; not installed for that tenant →
  `404 UNKNOWN_MODULE_ROUTE`; disabled for that tenant → `503 MODULE_DISABLED`.
- `POST /api/v1/gateway/internal/module-routes` is **removed**. Replaced by
  `PUT|DELETE /api/v1/gateway/internal/modules/{moduleId}/tenants/{tenantId}`,
  `PUT /api/v1/gateway/internal/modules:snapshot` and `GET /api/v1/gateway/internal/modules`,
  all requiring `X-Internal-Key` (`InternalApi:Key`, fail-closed `RequireSecret`,
  fixed-time compare). Pushes are validated: path-safe ids, http(s) `baseUrl`,
  upstreams confined to `/api/v1/{moduleId}/` (no shadowing platform/auth routes).
- Reconcile (D1): `ModuleStateReconciler` pulls Platform
  `GET /internal/gateway/module-states` (`X-Internal-Key`) at startup and every
  5 min (`ModuleReconcile:IntervalSeconds`; 15 s retry while the Platform is
  down), replaces state, logs drift; skips a snapshot if a push landed during
  the fetch. Legacy v1 / corrupt state files are rebuilt this way.
- Platform: `EfLifecyclePorts` pushes per tenant with `X-Internal-Key`;
  `GatewayRules` rows are removed only when the last tenant uninstalls.

Verification: `tests/Gateway.Api.Tests` (39 unit tests). I ran the live matrix
against isolated gateway and platform copies on :5110/:5131. Results: without a
key or with a wrong key, every internal call → 401. The old endpoint → 404. An
upstream squatting `/api/v1/platform` → 400. Disabling A → B is still proxied.
Pushing B again → A stays 503. Uninstalling A → B is still proxied and the
route stays. No token → 401, platform JWT → 403, forged header → 403. After a
restart the state is kept and reconciled. A v1 or corrupted file is rebuilt
from the Platform.

Residual: a lifecycle step that fails after its gateway push (e.g. seed failure)
leaves the gateway ahead of the catalog until the next reconcile (≤ 5 min);
tenants on older versions share the newest version's downstream (modules must
stay backward compatible, SR-8).

### R-GW1 — Gateway module state is global, not per tenant (HIGH) — CLOSED

**What**: `Gateway.Api/Services/GatewayStore` keeps one enabled/disabled state
per module (`_modules[moduleId]`) and one set of Ocelot routes per module, with
no tenant dimension. The platform lifecycle pushes that state on every
install / enable / disable / uninstall for *any* tenant.

**Impact** (violates G-BR5, SC-005 of `specs/001-sample-test-modules`, NFR4 isolation):
- Disable module X for tenant A → gateway returns `503 MODULE_DISABLED` for
  **every** tenant using X.
- Install or enable X for tenant B → X is **re-enabled at the gateway for
  tenant A** even though A disabled it (catalog still says DISABLED).
- Uninstall X for one tenant → routes removed → `404 UNKNOWN_MODULE_ROUTE`
  for **all** tenants that still have X installed.
- The multi-tenant install in the shell Catalog makes this easier to hit.

**Mitigations until fixed**: module APIs still enforce tenant + permission
per request (no data leak across tenants — the impact is availability /
wrong enabled state, not authorization); the catalog DB remains the source
of truth per tenant; test with one tenant per module, or avoid
disable/uninstall while other tenants use the module.

**Planned fix**: per-tenant gating in the gateway (push + reconcile).
Full design, file list, test matrix and the **3 decisions pending
approval**: `docs/proposal-gateway-per-tenant-state.md` (status: PROPOSED).

### R-GW2 — Unauthenticated gateway internal route API (CRITICAL) — CLOSED

**What**: `POST /api/v1/gateway/internal/module-routes`
(`Gateway.Api/Endpoints/AdminEndpoints.cs`) has no authentication and is
reachable on the public gateway port (:5100).

**Impact**: anyone can disable/remove any module's routes, or re-point a
module's downstream to an arbitrary host via `baseUrl`, capturing traffic
including bearer tokens.

**Mitigation until fixed**: do not expose the gateway beyond local dev.

**Planned fix**: replace it with `X-Internal-Key`-protected per-tenant
endpoints — item G3 of `docs/proposal-gateway-per-tenant-state.md`
(can ship first, independently of R-GW1).
