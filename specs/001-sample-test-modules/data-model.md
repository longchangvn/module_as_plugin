# Data Model: Sample Test Modules

No new database tables. All persistent state reuses existing stores:
- **Catalog**: `module_versions`, `tenant_module_installs`, `tenant_audit`, gateway rules.
- **Tenant store**: `permissions`, `roles`, `role_permissions`, `user_roles`, `user_additional_permissions`, `menus`, written by the existing seed applier.

The sample APIs hold no data; their responses are generated in memory.

## Entities

### SampleModule (manifest-defined; one per module)

| Field | Sample 1 | Sample 2 | Rule |
|---|---|---|---|
| `module.name` | `sample1` | `sample2` | slug `^[a-z0-9-]{3,50}$`, immutable |
| `module.displayName` | Sample Module 1 | Sample Module 2 | 3–100 chars |
| `module.version` | `1.0.0` | `1.0.0` | semver; immutable per publish |
| `dependencies` | `[]` | `[]` | independent (FR-013) |
| `frontend.scope` | `sample1_remote` | `sample2_remote` | globally unique |
| `frontend.modulePrefix` | `/sample1` | `/sample2` | routes start with prefix |
| `frontend.routes[0]` | `/sample1` → `./Page`, `requiredPermission: null`, `menuId: sample1` | same with `2` | |
| `frontend.menus[0]` | `id: sample1`, `parentId: null`, `title: Sample Module 1`, `route: /sample1`, `requiredPermission: null` | same with `2` | menu id unique per tenant |
| `frontend.uiPermissions` | `[SAMPLE_ONE_DATA_PROTECTED]` | `[SAMPLE_TWO_DATA_PROTECTED]` | ⊆ seed permissions |
| `api.routes[0]` | `/api/v1/sample1/{rest}` → `/api/v1/{rest}`, `[GET]`, `auth: true` | same with `2` | |

Full manifests: [contracts/manifest-sample1.yml](./contracts/manifest-sample1.yml), [contracts/manifest-sample2.yml](./contracts/manifest-sample2.yml).

### Permission (seeded on install, removed on uninstall per R6)

| code | object | action | isMenuAccess | Purpose |
|---|---|---|---|---|
| `SAMPLE_ONE_DATA_VIEW` | `SAMPLE_ONE_DATA` | `VIEW` | true | catalog completeness; not required by UI |
| `SAMPLE_ONE_DATA_PROTECTED` | `SAMPLE_ONE_DATA` | `PROTECTED` | false | gates button 2 and `GET /protected` |
| `SAMPLE_TWO_DATA_VIEW` | `SAMPLE_TWO_DATA` | `VIEW` | true | same as above |
| `SAMPLE_TWO_DATA_PROTECTED` | `SAMPLE_TWO_DATA` | `PROTECTED` | false | same as above |

All codes match `^[A-Z]+_[A-Z_]+$` (R4) and are unique per tenant store.

### SeededRole

| name | isSystem | grants |
|---|---|---|
| Sample 1 Viewer | true | `SAMPLE_ONE_DATA_VIEW`, `SAMPLE_ONE_DATA_PROTECTED` |
| Sample 2 Viewer | true | `SAMPLE_TWO_DATA_VIEW`, `SAMPLE_TWO_DATA_PROTECTED` |

Neither name contains "Admin", so uninstalling never triggers `LAST_ADMIN`. There are no seed users and no `userRoles`; testers assign roles through Tenant Admin.

### SampleContent (response DTO, in memory)

| Field | Type | Notes |
|---|---|---|
| `module` | string | `sample1` / `sample2` |
| `version` | string | API version (`1.0.0`) |
| `kind` | `"sample"` \| `"protected"` | distinguishes the two buttons |
| `generatedAt` | ISO-8601 UTC string | server clock; proves a fresh call |
| `tenantId` | string | from the JWT; proves tenant context |
| `items` | `{ id, name, value }[]` | 3 illustrative records with module-distinct names |

### Button state (remote UI, per button)

```text
idle ──click──▶ loading ──2xx──▶ success(content)
                   │
                   └─non-2xx/network──▶ error(code, message)
success|error ──click──▶ loading        (button disabled while loading)
401 at any point ──▶ redirect /login
```

Protected button availability: `enabled = permissions.includes(<MODULE>_DATA_PROTECTED)`, evaluated once per page load from `GET /api/v1/auth/me`.

### Install lifecycle (existing state machine, unchanged)

`NOT_INSTALLED → INSTALLING → INSTALLED(enabled) ⇄ DISABLED → NOT_INSTALLED`. Each transition updates the bootstrap payload (nav entry), the gateway module state (503/404), and the tenant seed (permissions, role, menu).
