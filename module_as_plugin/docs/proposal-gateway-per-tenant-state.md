# Proposal: Per-tenant module gating at the gateway (fixes R-GW1, R-GW2)

**Status: IMPLEMENTED (2026-10-20)** — D1–D3 approved as recommended. Deviations: the
snapshot shape is `{ modules: [{ moduleId, versions:[{version, baseUrl, upstreams}], tenants:[{tenantId, enabled, version}] }] }`
so the gateway can fall back to an older version's routes when the newest tenant
uninstalls; unauthenticated module requests get `401 UNAUTHORIZED` (not 403); pushes also
confine upstreams to `/api/v1/{moduleId}/`. Results: `docs/gap-closure-report.md` → "Known risks".
Risk register: `docs/gap-closure-report.md` → "Known risks". Tracked in `PROGRESS.md` → Next up.

## 1. Problem

### R-GW1: module state is global (HIGH)
- `Gateway.Api/Services/GatewayStore` keeps **one state per module**
  (`_modules[moduleId] = Enabled | Disabled`, persisted in `gateway-modules.json`)
  and **one set of Ocelot routes per module** (`Metadata.managedBy = module:{id}`).
- Platform passes `tenantId` into `IModuleLifecyclePorts.ApplyGatewayRoutesAsync` /
  `RemoveModuleRoutesAsync`, but `EfLifecyclePorts` **drops it** when calling the
  gateway (`POST /api/v1/gateway/internal/module-routes { moduleId, enabled, remove, baseUrl }`).
- The proxy guard (`Gateway.Api/Program.cs`) checks `GetModuleState(moduleId)` only — no tenant.
- Platform `GatewayRules` (catalog) is per module; `RemoveModuleRoutesAsync` deletes all rows
  for the module when **one** tenant uninstalls.

Effects: disable in tenant A → 503 for everyone; install/enable in B → re-enabled for A;
uninstall in A → 404 for everyone. Violates G-BR5, NFR4, spec 001 SC-005. Authorization is
not affected (module APIs enforce tenant + permission per request).

### R-GW2: unauthenticated internal route API (CRITICAL)
`POST /api/v1/gateway/internal/module-routes` (`Gateway.Api/Endpoints/AdminEndpoints.cs`) has
**no authentication** and is reachable on the public gateway port (:5100). Anyone can disable or
remove any module, or re-point a module's routes to an arbitrary host via `baseUrl` — capturing
traffic including bearer tokens.

## 2. Proposed design: per-tenant gating, push + reconcile

### Gateway
| # | Change |
|---|---|
| G1 | **State model** per module: `{ moduleId, upstreams[], baseUrl, version, tenants: { tenantId → enabled } }`, persisted in a v2 `gateway-modules.json`. Ocelot routes exist while ≥ 1 tenant has the module installed; removed when the last tenant uninstalls. |
| G2 | **Guard** for `/api/v1/{moduleId}/…`: tenant = `tenantId` claim from a **validated** JWT (JwtBearer already configured in the gateway). `X-Tenant-Id` present and different → `403 FORBIDDEN_TENANT`; no tenant / platform JWT → `403 FORBIDDEN_TENANT` (R9). Tenant not in `tenants` → `404 UNKNOWN_MODULE_ROUTE`; `enabled = false` → `503 MODULE_DISABLED`; else proxy. |
| G3 | **Internal API, `X-Internal-Key` required** (same `InternalApi:Key` as platform↔auth, fixed-time compare, already fail-closed via `RequireSecret`): `PUT /api/v1/gateway/internal/modules/{moduleId}/tenants/{tenantId}` `{ enabled, version, baseUrl, upstreams }`; `DELETE /api/v1/gateway/internal/modules/{moduleId}/tenants/{tenantId}`; `PUT /api/v1/gateway/internal/modules:snapshot` (full replace). **Remove** the old `/internal/module-routes`. Gateway gains `builder.RequireSecret("InternalApi:Key", …)`; compose/AppHost pass `InternalApi__Key` to the gateway. |
| G4 | **Reconcile**: background service pulls the Platform snapshot (P2) on startup and every 5 min, replaces state, logs + counts drift. Heals failed pushes and migrates the old v1 file format. |
| G5 | Pure, unit-testable `ModuleTenantState` (upsert / remove / decide), separate from file + Ocelot I/O in `GatewayStore`. |

### Platform
| # | Change |
|---|---|
| P1 | `EfLifecyclePorts.ApplyGatewayRoutesAsync` / `RemoveModuleRoutesAsync` call the per-tenant endpoints with `tenantId` + `X-Internal-Key`. |
| P2 | New `GET /internal/gateway/module-states` (`X-Internal-Key`): built from `Installs` (state ≠ NotInstalled) + `ModuleVersions` (`ArtifactApiUrl`, manifest `api.routes`) → `[{ moduleId, version, baseUrl, upstreams[], tenants:[{ tenantId, enabled }] }]`. Not routed by the gateway. |
| P3 | `GatewayRules` rows removed only when the **last** tenant uninstalls (query `Installs`). No migration. |

### Tests / verification
- Unit: `ModuleTenantState` — allow, disabled, unknown, last-tenant removal, routes kept while ≥1 tenant, header/claim mismatch.
- Live two-tenant matrix (Aspire): disable A → B still 200; install B → A stays 503; uninstall A → B still 200; gateway restart keeps state; reconcile repairs a manually corrupted `gateway-modules.json`; internal API without key → 401, wrong key → 401.
- Docs: close R-GW1/R-GW2 in the risk register; drop "test with one tenant" notes (README, quickstart, spec 001 research R9.1).

### Files expected to change
`src/Gateway/Gateway.Api/{Program.cs, Services/GatewayStore.cs, Services/ModuleTenantState.cs (new), Services/ModuleStateReconciler.cs (new), Endpoints/AdminEndpoints.cs, Endpoints/GatewayHelpers.cs, Models/Requests.cs}`,
`src/Platform/Platform.Api/{Services/EfLifecyclePorts.cs, Endpoints/InternalEndpoints.cs}`,
`src/Aspire/ModulePlatform.AppHost/Program.cs`, `docker-compose.yml`, `tests/…` (new gateway state tests),
`README.md`, `PROGRESS.md`, `docs/gap-closure-report.md`.

## 3. Decisions pending approval

| # | Question | Recommended | Alternative |
|---|---|---|---|
| D1 | How state reaches the gateway | **Push + 5-min reconcile** — immediate effect; gateway keeps serving if Platform is down (NFR6) | Pull per request from Platform with ~30 s cache — less gateway code, up to 30 s lag, module traffic fails when Platform is down past the cache |
| D2 | Tenants on different versions of one module (one downstream per module) | **Route to the newest installed version's `baseUrl`**; module services must stay backward compatible (SR-8 additive upgrades) | Block install/upgrade to a version different from other tenants. True per-tenant version routing = requirement Open Question 4, out of scope |
| D3 | Tenant identity at the gateway | **Validate JWT at the gateway** (one signature check per request) | Trust `X-Tenant-Id` — forgeable for the gateway's gating; module APIs still enforce |

## 4. How to resume
1. Approve or amend D1–D3 (record the answers in §3).
2. Implement G1–G5, P1–P3 in that order (G3 alone closes R-GW2 and can ship first if urgent).
3. Run the live two-tenant matrix, update the risk register, mark this proposal **IMPLEMENTED**.
