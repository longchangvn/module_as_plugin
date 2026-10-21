# Contract: Module Host (PluginHost.Api, :5300)

Config: `Jwt:SecretKey`, `InternalApi:Key`, `Platform:BaseUrl` (default `http://localhost:5121`), `Auth:BaseUrl` (default `http://localhost:5122`), `PluginHost:CacheRoot` (default `./plugin-cache`), `PluginHost:DrainTimeoutSeconds` (30), `PluginHost:ReconcileIntervalMinutes` (5).

## Internal API (X-Internal-Key; called by the Platform)

### PUT /internal/plugins/{moduleId}/tenants/{tenantId}
Body: `{ "version": "1.0.0", "enabled": true, "packageSha256": "ab12…" }`
- Ensures runtime `moduleId@version` (download from Platform if not cached, verify SHA-256, extract, load, build endpoints) **then** swaps the tenant shell.
- `200 { "data": { "moduleId", "tenantId", "version", "enabled", "state": "ACTIVE" } }`
- `422 PLUGIN_LOAD_FAILED` (load/contract/hash failure; shell unchanged) · `502 PLUGIN_HOST_UNAVAILABLE` (package download failed) · `400 VALIDATION_ERROR` (bad ids/version) · `401`

### DELETE /internal/plugins/{moduleId}/tenants/{tenantId}
- Idempotent. `200 { "data": { "moduleId", "tenantId", "removed": true|false } }`. Unreferenced runtime drained then unloaded in the background.

### POST /internal/plugins/{moduleId}/versions/{version}/migrate
Body: `{ "fromVersion": "1.0.0", "toVersion": "1.1.0", "packageSha256": "…" }`
- Loads (pins) the target runtime and calls `IModulePlugin.MigrateAsync`. `200 { "data": { "migrated": true } }`, `422 PLUGIN_LOAD_FAILED`, `500` migrate threw (message).

### GET /internal/plugins
- Operator view: `{ "data": { "runtimes": [{ "moduleId", "version", "packageSha256", "state", "inFlight", "loadedAt", "tenants": [..] }], "tenants": { "<tenantId>": { "<moduleId>": { "version", "enabled" } } } } }`

## Public paths (reached through the Gateway)

### /plugins/{moduleId}/{**rest}
- Requires `Authorization: Bearer <tenant JWT>`; optional `X-Tenant-Id` must equal the token tenant.
- `401 UNAUTHORIZED` no/invalid token · `403 FORBIDDEN_TENANT` missing tenant claim or header mismatch · `404 UNKNOWN_MODULE_ROUTE` not in tenant shell · `503 MODULE_DISABLED` disabled for tenant · otherwise dispatched to the tenant's runtime (unmatched path inside the plugin → `404`).

### GET /plugin-assets/{moduleId}/{version}/{**path}
- Anonymous, CORS `*`, serves files from the runtime's extracted `web/` folder (path normalized; no escape). `404` when version not cached or file missing. `Cache-Control: public, max-age=31536000, immutable` except `remoteEntry.js` (`no-cache`).

### GET /healthz, GET /alive, GET /health
- Host health (ServiceDefaults); unaffected by plugin changes.
