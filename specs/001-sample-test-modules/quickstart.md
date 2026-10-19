# Quickstart: Validate the Sample Test Modules

End-to-end manual validation for [spec.md](./spec.md). Contracts: [sample-module-api.md](./contracts/sample-module-api.md), [remote-ui.md](./contracts/remote-ui.md). All commands run from `module_as_plugin/`.

## Prerequisites

- Postgres up (Aspire `:5434`, or the standalone container `:5433`) and the core stack running in Development: gateway :5100, platform :5121, auth :5122, tenant-admin :5175.
- A tenant with a bootstrap admin you can sign in as (SETUP §3.2–3.3). Use **one** tenant for this test: gateway module state is global (research R9.1).
- A second tenant user **without** any Sample role (create one in Tenant Admin → Users).

## 1. Start the sample modules

```bash
dotnet run --project src/Modules/Sample1/Sample1.Api          # :5202
dotnet run --project src/Modules/Sample2/Sample2.Api          # :5203
(cd modules/sample1/web && npm install && npm run serve)      # build + preview :5176
(cd modules/sample2/web && npm install && npm run serve)      # build + preview :5177
curl -s localhost:5202/healthz; curl -s localhost:5203/healthz # {"status":"ok","module":"sample1|2",...}
curl -sI localhost:5176/assets/remoteEntry.js | head -1        # HTTP/1.1 200
```

Under Aspire, all four start with the AppHost.

## 2. Publish both modules (platform Admin token)

```bash
for m in sample1 sample2; do
  curl -s -X POST localhost:5100/api/v1/platform/modules \
    -H "Authorization: Bearer $PLATFORM_TOKEN" -H 'Content-Type: application/json' \
    -d "{\"manifestYaml\": $(python3 -c "import json;print(json.dumps(open('modules/$m/manifest.yml').read()))"), \"artifactApiUrl\": \"http://localhost:520$([ $m = sample1 ] && echo 2 || echo 3)\"}"
done
```

**Expect**: `201` twice. The modules appear in the shell Catalog as `Available`.

## 3. Validation scenarios

| # | Action | Expected (spec ref) |
|---|---|---|
| 1 | Sign in to Tenant Admin `:5175` as the bootstrap admin, open Home | No Sample entries; "No modules installed" (US1-1) |
| 2 | Install `sample1` for the tenant (shell Catalog or `POST /api/v1/platform/tenants/$TID/modules/sample1/install`), reload Home | "Sample Module 1" card plus nav link; Open → page with name, version, two buttons (US1-2, FR-002/003) |
| 3 | Click **Get sample data** | Loading, then JSON with `module: sample1`, `kind: sample`, fresh `generatedAt`, 3 items, in < 2 s (US2-1, SC-003) |
| 4 | Look at **Get protected data** (no Sample role yet) | Disabled; hint "Requires SAMPLE_ONE_DATA_PROTECTED" (US3-1) |
| 5 | `curl localhost:5100/api/v1/sample1/protected -H "Authorization: Bearer $TENANT_TOKEN"` | `403 FORBIDDEN` with `details.requiredPermission` (US3-3, FR-008) |
| 6 | Tenant Admin → Users → yourself → assign role **Sample 1 Viewer**, reload module page | Button enabled; click → JSON with `kind: protected` (US3-2) |
| 7 | Remove the role, reload; repeat the curl from #5 | Button disabled again; curl `403` immediately, no cache delay (US3-4, SC-004) |
| 8 | Install `sample2`; grant only **Sample 2 Viewer** | Module 2 protected button enabled, Module 1 disabled; each page's data names its own module (US2-3, US4-1) |
| 9 | Disable `sample1`, then click its buttons on the open page | `MODULE_DISABLED` message; after reload, the nav entry is gone; Module 2 unaffected (US1-3, edge case) |
| 10 | Enable `sample1`, reload | Entry and data return, no reinstall (US1-3) |
| 11 | Uninstall `sample1` | Entry gone; `/api/v1/sample1/data` → `404 UNKNOWN_MODULE_ROUTE`; `SAMPLE_ONE_*` permissions and "Sample 1 Viewer" removed (Tenant Admin → Permissions/Roles); Module 2 fully working (US1-4, US4-2, FR-012, SC-005) |
| 12 | Stop the `sample2` preview server (:5177) and reload its route | "Module unavailable" card; Home and the other pages still work (edge case) |
| 13 | Stop `Sample2.Api` (:5203) and click Get sample data | Inline error (gateway `502`/upstream failure), page stays usable (US2-2) |
| 14 | Sign in as a user of a **different** tenant and call `/api/v1/sample2/data` with header `X-Tenant-Id: <first tenant>` | `403 FORBIDDEN_TENANT` (edge case) |

## 4. Automated checks

```bash
dotnet build ModulePlatform.sln --nologo -v q      # 0 errors
dotnet test ModulePlatform.sln --nologo -v q       # all pass, incl. sample manifest validation + ModuleGuards tests
(cd apps/tenant-admin && npx tsc --noEmit && npx vite build)
(cd modules/sample1/web && npx tsc --noEmit) && (cd modules/sample2/web && npx tsc --noEmit)
```

## Cleanup

Uninstall both modules from the tenant. Registry versions are immutable and stay published, so re-running step 2 returns a duplicate-version error, which is expected.
