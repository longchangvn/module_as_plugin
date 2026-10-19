# Research: Sample Test Modules

Feature: [spec.md](./spec.md) · Plan: [plan.md](./plan.md)

Each item lists the decision, why it was chosen, and the alternatives considered. All Technical Context unknowns are resolved here.

---

## R1. Where does "Home" live (which app hosts the module UIs)?

**Finding**: Since P14, `apps/shell` (:5173) is the **platform-operator** portal. Its `LoginPage` only calls `platformLogin`, and `useBootstrap` skips loading when there is no `tenant_id`. So **no tenant user can reach a module UI today**. Tenant users sign in at `apps/tenant-admin` (:5175), which already has a Home page but no Module Federation host.

**Decision**: Make **Tenant Admin (:5175)** the Module Federation host:
- `/` Home shows an "Installed modules" card list built from `GET /api/v1/tenant/frontend-bootstrap`.
- The top nav adds one link per bootstrap menu that has a `route`.
- Each bootstrap route renders the module's remote page.

**Rationale**: Tenant users already sign in there with a tenant JWT, and the bootstrap endpoint requires a tenant JWT (R10). It reuses the existing layout and session, and it respects the P14 login split.

**Alternatives considered**:
- Re-add tenant login to the shell: rejected because it reverses the P14 decision and mixes platform and tenant sessions in one app.
- A third app: rejected as an extra app with no benefit.

**Spec impact**: Correct the spec assumption "'Home' means … the existing platform portal" to "the Tenant Admin portal".

## R2. Loading remotes at runtime (Module Federation with `@originjs/vite-plugin-federation`)

**Finding**:
- `@originjs/vite-plugin-federation` emits `remoteEntry.js` as an **ES module** (`export { get, init }`).
- The shell's current loader injects it with a classic `<script>` and then reads `window[scope]`. That cannot work for ESM output.
- No live end-to-end remote load is recorded in PROGRESS.
- originjs remotes only work from a **build** (`vite build` + `vite preview`), not from `vite dev`.

**Decision**:
- **Host** (tenant-admin): add `@originjs/vite-plugin-federation` with `shared: ['react', 'react-dom']`. Register remotes dynamically through the plugin's runtime API from `virtual:__federation__`, using the bootstrap `scope` and `remoteUrl`:
  - `__federation_method_setRemote(scope, { url, format: 'esm', from: 'vite' })`
  - `__federation_method_getRemote(scope, exposes)`
  - `__federation_method_unwrapDefault`
  
  A placeholder `remotes` entry in the host config makes the plugin generate the virtual module.
- **Remotes**: `vite build` + `vite preview` on fixed ports, exposing `./Page`.
- **Spike first (T-spike)**: prove one remote renders a component using `useState` inside the host, with React shared as a singleton.

**Fallback if the spike fails** (shared React not resolved by the dynamic API in host dev mode): the remote exposes `./mount` as `mount(el, props) => unmount`, rendering with its own bundled React. The host renders a `<div ref>` and calls `mount`/`unmount`.
- Pros: isolated, no singleton coupling.
- Cons: a second React copy (~45 KB gz) per remote page.

**Alternatives considered**:
- `import(remoteUrl)` and calling `init({})`: rejected because without the host share scope the remote uses its own React, which breaks hooks.
- Switching to `@module-federation/vite`: a larger change across shell, loyalty and tenant-admin, and out of scope.

## R3. Gateway route shape for the sample APIs

**Decision**: Each manifest declares a single route:
- Upstream `/api/v1/{moduleId}/{rest}` → downstream `/api/v1/{rest}`.
- Methods `[GET]`, `auth: true`.

The module serves `GET /api/v1/data` and `GET /api/v1/protected`. Browser paths are therefore `/api/v1/sample1/data`, `/api/v1/sample1/protected`, and the same for sample2.

**Rationale**:
- The gateway guard derives the module from `segments[2]` (`GatewayHelpers.ModuleFromPath`), so `503 MODULE_DISABLED` and `404 UNKNOWN_MODULE_ROUTE` work with no gateway change.
- One `{rest}` catch-all avoids the Ocelot empty-placeholder edge case on bare paths.

**Alternatives considered**: One route per endpoint: more manifest noise and no benefit.

## R4. Server-side permission enforcement in the module API

**Finding**: Loyalty's sample endpoint checks tenant usability but **does not** check permissions. The spec (FR-008) requires enforcement.

**Decision**: Add a small shared helper in `SharedKernel` (`ModuleGuards`) that the sample APIs use:
1. Require the tenant claim, match any `X-Tenant-Id`, and check usability via Platform `/internal/tenants/{id}/status`. This is the same behaviour as Loyalty's helper.
2. `RequirePermissionAsync(code)` calls Auth `GET /api/v1/auth/permissions/resolve?tenantId&userId` on the **cluster-internal** Auth base URL:
   - Permission missing → `403 FORBIDDEN`.
   - Auth unreachable or non-2xx → `503 AUTHZ_UNAVAILABLE` (fail closed).

**No module-side cache.** Auth already caches 60s with version-bump invalidation on every grant or revoke, so a revoke takes effect on the next call (SC-004).

**Alternatives considered**:
- Embedding permissions in the JWT: tokens would be stale until they expire (8h), which breaks the revoke scenario.
- A module-side 60s cache: delays revocation for no measurable gain at sample scale.
- Copying Loyalty's helper into each sample: duplicates code. Loyalty stays untouched per the spec, but can adopt the helper later.

## R5. UI permission check for the second button

**Decision**: On page load the remote calls `GET /api/v1/auth/me` (existing, via the gateway, tenant JWT) and reads `data.permissions`. The button is enabled only if the module's code is present. Otherwise it is disabled with `title`/hint text "Requires SAMPLE_ONE_DATA_PROTECTED". The check is re-read on reload.

**Rationale**:
- `/auth/me` already returns effective permissions (role ∪ direct, active only) for the caller, so no new endpoint is needed.
- The resolve endpoint is not used from the browser (see R9).

**Alternatives considered**: Passing permissions from the host as props: couples the host to module semantics, and the host does not hold permissions today.

## R6. How the remote gets the token and calls APIs

**Decision**: The remote runs inside the host page (same origin as tenant-admin). It reads the tenant JWT from `sessionStorage['access_token']` and the tenant id from `sessionStorage['tenant_id']`, matching the host's `api-client`. It calls **relative** `/api/v1/...` paths, which the host's Vite proxy or nginx forwards to the gateway.

- `401` → redirect to `/login`.
- `403`, `404`, `503` → show the envelope `error.message` inline.
- Each button keeps its own `idle | loading | success | error` state, and the button is disabled while loading (edge case: double-click).

**Alternatives considered**:
- A host-provided `api` prop: cleaner coupling, but it adds a host↔remote contract to maintain. The token is already in `sessionStorage` (SR-9 allows session storage).

## R7. Remote styling

**Decision**: Use plain, self-contained CSS in each remote (a small CSS module or inline styles), not Tailwind.

**Rationale**: The host's Tailwind v4 only scans host sources, so remote Tailwind classes would not be generated. Shipping a Tailwind build per remote is overkill for samples.

**Alternatives considered**: Rely on host classes: they would be missing at runtime.

## R8. Ports, names, codes

| Item | Sample Module 1 | Sample Module 2 |
|---|---|---|
| module name (slug) | `sample1` | `sample2` |
| display name | Sample Module 1 | Sample Module 2 |
| API project / port | `src/Modules/Sample1/Sample1.Api` / :5202 | `src/Modules/Sample2/Sample2.Api` / :5203 |
| remote / port | `modules/sample1/web` / :5176 | `modules/sample2/web` / :5177 |
| federation scope | `sample1_remote` | `sample2_remote` |
| route / menu | `/sample1` / `sample1` | `/sample2` / `sample2` |
| protected permission | `SAMPLE_ONE_DATA_PROTECTED` | `SAMPLE_TWO_DATA_PROTECTED` |
| open permission (menu, `isMenuAccess`) | `SAMPLE_ONE_DATA_VIEW` | `SAMPLE_TWO_DATA_VIEW` |
| seeded role | Sample 1 Viewer (both codes) | Sample 2 Viewer (both codes) |

The menu and route set `requiredPermission: null` so any tenant user sees the entry (FR-004). `*_DATA_VIEW` is seeded for catalog completeness but is not required, which keeps the open button open.

Manifest validation constraints satisfied:
- slug `^[a-z0-9-]{3,50}$` and not reserved
- unique scope
- route paths start with `modulePrefix`
- `uiPermissions` ⊆ seed permissions
- `remoteUrl` http is allowed in Development

## R9. Findings recorded, out of scope

1. **The gateway keeps module enabled/disabled state globally**, not per tenant (`GatewayStore._modules[moduleId]`). Disabling `sample1` for tenant A also returns 503 for tenant B. Testers should use one tenant, and the quickstart notes this. Fix candidate: per-tenant guard in the gateway.
2. **`GET /api/v1/auth/permissions/resolve` is anonymous** and is exposed through the gateway's `/api/v1/auth/{rest}` route, so anyone can enumerate any user's permissions. Add to the Phase 2 security list: require an internal key or a service identity, and stop routing it publicly.
3. The shell's remote loader (`<script>` + `window[scope]`) is incompatible with originjs ESM output. Out of scope here; the shell can adopt the tenant-admin loader later.
