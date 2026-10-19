# Contract: Sample Module Remote UI and Tenant Admin Host

## Remote (each sample module)

| Item | Value |
|---|---|
| Federation `name` (scope) | `sample1_remote` / `sample2_remote` |
| Entry | `http://localhost:{5176\|5177}/assets/remoteEntry.js` (ESM, from `vite build` + `vite preview`) |
| Exposes | `./Page` → default export: React component with no props |
| Fallback expose (only if R2 spike fails) | `./mount` → `mount(el: HTMLElement) => () => void` |
| Shared | `react`, `react-dom` (singletons with host) |
| Styling | self-contained CSS, no host classes |

### Page behaviour (FR-003, FR-004, FR-007, FR-010)

- Header: display name and version.
- **Get sample data**: always enabled when idle. Calls `GET /api/v1/{m}/data`. Shows a loading state, then pretty-printed JSON (`JSON.stringify(data, null, 2)`) or an inline error `"{code}: {message}"`.
- **Get protected data**: on mount, calls `GET /api/v1/auth/me` and checks `data.permissions` for `{M}_DATA_PROTECTED`.
  - Absent: the button is disabled, with hint text and a `title` of `Requires {M}_DATA_PROTECTED`.
  - Present: enabled. Calls `GET /api/v1/{m}/protected`, with the same states as the first button.
  - `/auth/me` failure: the button stays disabled and the hint shows "Permission check failed".
- Requests send `Authorization: Bearer <sessionStorage.access_token>` and `X-Tenant-Id: <sessionStorage.tenant_id>`, using relative URLs.
- A `401` from any call → `window.location.assign('/login')`.
- Each button is disabled while its own request is in flight.

## Host: Tenant Admin (:5175)

| Concern | Contract |
|---|---|
| Source of truth | `GET /api/v1/tenant/frontend-bootstrap` (tenant JWT) → `modules[]` with `scope, remoteUrl, routes[], menus[]` (SR-6b). Never `manifest.yml` (R10). |
| Home `/` | Existing counts/permissions plus an **Installed modules** section: one card per bootstrap module (display title from its top-level menu, version, "Open" link to its first route). Empty state: "No modules installed. Ask a platform admin to install one from the Catalog." |
| Nav | One top-nav link per bootstrap menu that has a `route`, after the core links. |
| Routes | For each `module.routes[]`: `<Route path={path}>` → remote page inside a per-module error boundary showing "Module unavailable" on load or render failure (edge case). |
| Loading | Remotes are registered after bootstrap and the component is lazy-loaded only when its route is visited (NFR2). |
| Refresh | Bootstrap is fetched on login and page load. Install, enable, disable and uninstall show up after a reload. |
| Federation | Host `name: 'tenant_admin'`, `shared: ['react','react-dom']`, dynamic remotes via the `virtual:__federation__` API (research R2). |
