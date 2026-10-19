# Contract: Sample Module HTTP API

Applies identically to `sample1` (:5202) and `sample2` (:5203). `{m}` below means `sample1` or `sample2`, and `{M}` means `SAMPLE_ONE` or `SAMPLE_TWO`.

- **Browser paths** go through the gateway only: `/api/v1/{m}/…` (G-BR3).
- **Downstream paths** are what the module service serves: `/api/v1/…`.
- Envelope: `{ "data": … }` / `{ "error": { "code", "message", "details"?, "retryable"? } }` (SR-9).

## Template endpoints (M-BR7, §9.5)

| Method | Downstream path | Auth | Response |
|---|---|---|---|
| GET | `/healthz` | none | `200 { status: "ok", module: "{m}", version: "1.0.0" }` |
| GET | `/readyz` | none | `200 { ready: true, checks: { db: "n/a", dependencies: "ok" } }` |
| POST | `/internal/seed:preview` | internal | `200 { data: { roles: ["Sample N Viewer"], permissions: ["{M}_DATA_VIEW","{M}_DATA_PROTECTED"], menus: ["{m}"] } }` (must equal the manifest seed) |
| POST | `/internal/migrate` | internal | `{fromVersion,toVersion}` → `200 { data: { migrated: bool } }` (additive only) |

## Business endpoints

### `GET /api/v1/{m}/data` → downstream `GET /api/v1/data`

- **Auth**: tenant JWT required (`tenantId` and `sub`/`userId` claims). No permission.
- **200**:
  ```json
  { "data": {
      "module": "sample1", "version": "1.0.0", "kind": "sample",
      "generatedAt": "2026-10-19T08:00:00Z", "tenantId": "tnt_…",
      "items": [ { "id": "s1-1", "name": "Sample 1 · Alpha", "value": 10 },
                 { "id": "s1-2", "name": "Sample 1 · Beta",  "value": 20 },
                 { "id": "s1-3", "name": "Sample 1 · Gamma", "value": 30 } ] } }
  ```

### `GET /api/v1/{m}/protected` → downstream `GET /api/v1/protected`

- **Auth**: tenant JWT **and** effective permission `{M}_DATA_PROTECTED`, resolved per request via Auth `GET /api/v1/auth/permissions/resolve?tenantId&userId` on the cluster-internal URL `Auth:BaseUrl`, with no module-side cache.
- **200**: same shape with `"kind": "protected"` and item names suffixed `(protected)`.

## Errors (both business endpoints, checked in this order)

| Condition | Who answers | Status | `error.code` |
|---|---|---|---|
| Module uninstalled for platform | gateway | 404 | `UNKNOWN_MODULE_ROUTE` |
| Module disabled | gateway | 503 | `MODULE_DISABLED` |
| No or invalid JWT | module | 401 | (empty body, JwtBearer challenge) |
| JWT without `tenantId`, or `X-Tenant-Id` ≠ JWT tenant | module | 403 | `FORBIDDEN_TENANT` |
| Unknown tenant / tenant not active | module | 404 / 403 | `UNKNOWN_TENANT` / `TENANT_DISABLED` |
| Platform status unreachable | module | 503 | `AUTHZ_UNAVAILABLE` (`retryable: true`) |
| `/protected` without permission | module | 403 | `FORBIDDEN` (`details.requiredPermission`) |
| Auth resolve unreachable / non-2xx | module | 503 | `AUTHZ_UNAVAILABLE` (`retryable: true`, fail closed) |

## Configuration

| Key | Default (Development) | Notes |
|---|---|---|
| `Jwt:SecretKey` | dev default via `RequireSecret` | required outside Development |
| `Platform:BaseUrl` | `http://localhost:5121` | tenant status |
| `Auth:BaseUrl` | `http://localhost:5122` | permission resolve (cluster-internal) |
