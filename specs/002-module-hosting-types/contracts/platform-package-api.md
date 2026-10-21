# Contract: Platform API changes

All responses use the existing envelope: success `{ "data": ... }`, error `{ "error": { "code", "message", "details"? } }`.

## POST /api/v1/platform/modules/package  (NEW)

- Auth: platform JWT, role `Admin` (Maintainer → `403 FORBIDDEN`). Audited `module.publish`.
- Body: `multipart/form-data`
  - `package` (file, required) — zip (≤ `ModulePackages:MaxPackageBytes`, default 50 MB)
  - `artifactApiUrl` (text, optional, remote only) — overrides manifest `service.baseUrl`
  - `artifactRemoteUrl` (text, optional)
- `201` → `{ "data": { "id": "sample3", "version": "1.0.0", "type": "plugin", "packageSha256": "ab12…" } }`
- `400 PACKAGE_INVALID` → `details: [{ "field": "package" | "<entry path>", "message": "..." }]` (path escape, disallowed root, limits, missing manifest, missing plugin assembly, missing remoteEntry)
- `400 VALIDATION_ERROR` → manifest field list (same as JSON publish)
- `400 VALIDATION_ERROR` + `details.field = "module.version"` → duplicate version
- `413` → body exceeds limit

## POST /api/v1/platform/modules  (existing, behaviour additions)

- `artifactApiUrl` omitted → manifest `service.baseUrl` is used.
- Manifest with `module.type: plugin` → `400 VALIDATION_ERROR` `"Plugin modules must be registered by package upload (POST /api/v1/platform/modules/package)."`

## GET /api/v1/platform/modules, GET /api/v1/platform/modules/{id}  (existing, additive)

- List items gain `type` (type of the latest version).
- Detail `versions[]` gain `type`, `packageSha256` (no path).

## GET /api/v1/tenant/frontend-bootstrap  (existing, behaviour)

- For plugin versions with empty `frontend.remoteUrl`: `remoteUrl = {PluginHost:PublicBaseUrl}/plugin-assets/{moduleId}/{version}/assets/remoteEntry.js`.

## Internal (X-Internal-Key, not routed by the gateway)

### GET /internal/modules/{id}/versions/{version}/package  (NEW)
- `200 application/zip` stream with header `X-Package-Sha256`; `404` unknown version or no package; `401` bad key.

### GET /internal/plugin-host/states  (NEW)
```json
{ "data": { "plugins": [
  { "moduleId": "sample3", "version": "1.0.0", "packageSha256": "ab12…",
    "tenants": [ { "tenantId": "tnt_demo", "enabled": true } ] }
] } }
```
Rule: one entry per (module, version) used by ≥ 1 install row of a `plugin` version; `enabled = Enabled && State ∈ {Installed, Installing}`.

### GET /internal/gateway/module-states  (existing, behaviour)
- `baseUrl` / `upstreams` now come from the version's hosting strategy (plugin → Module Host base URL, downstream prefixed `/plugins/{moduleId}`).
