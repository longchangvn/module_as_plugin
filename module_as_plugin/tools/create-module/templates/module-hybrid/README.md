# __DISPLAY_NAME__

__DESCRIPTION__

Hybrid module scaffolded by `tools/create-module --type hybrid`. Developer guide: `__PROJECT_DIR__/docs/module-developer-guide.md`.

- **UI**: built into the package (`web/`) and served by the shared **Module Host** (:5300) under
  `/plugin-assets/__MODULE_NAME__/<version>/` (`assets/remoteEntry.js`). The manifest leaves
  `frontend.remoteUrl` empty so the platform points the shell at the Module Host.
- **API**: a standalone ASP.NET Core process (:__API_PORT__). The gateway routes
  `/api/v1/__MODULE_NAME__/...` to the published `artifactApiUrl` (default: `service.baseUrl`, else the local port).

The module lives outside the platform project folder (`__PROJECT_DIR__/`), in `modules/__MODULE_NAME__/`:

| Part | Location |
|------|----------|
| API (standalone host) | `modules/__MODULE_NAME__/api/__PASCAL__.Api` |
| Remote SPA (federation, built into the package) | `modules/__MODULE_NAME__/web` (local dev: :__WEB_PORT__) |
| Manifest | `modules/__MODULE_NAME__/manifest.yml` |
| Parity test | `__PROJECT_DIR__/tests/SharedKernel.Tests/Modules/__PASCAL__ModuleTests.cs` |

API endpoints: `GET /healthz`, `GET /readyz`, `POST /internal/seed:preview`, `POST /internal/migrate`,
`GET /api/v1/data`, `GET /api/v1/protected`.
Through the gateway (:5100): `/api/v1/__MODULE_NAME__/data`, `/api/v1/__MODULE_NAME__/protected`.

## Run, package, publish, install (from `__PROJECT_DIR__/`)

```bash
dotnet run --project ../modules/__MODULE_NAME__/api/__PASCAL__.Api                 # API :__API_PORT__ (or via the Aspire AppHost)
node tools/create-module/create-module.mjs package __MODULE_NAME__                 # dist/modules/__MODULE_NAME__-1.0.0.zip (manifest + README + web/)
node tools/create-module/create-module.mjs publish __MODULE_NAME__ --password <pw> # builds + uploads the package (artifactApiUrl = local API port)
node tools/create-module/create-module.mjs install __MODULE_NAME__ --tenant <TENANT_ID> --password <pw>
```

Docker (build context = the folder that contains both `__PROJECT_DIR__/` and `modules/`):
`docker build -f modules/__MODULE_NAME__/api/__PASCAL__.Api/Dockerfile .`

Seeded on install:
- Permissions: `__PERM___DATA_VIEW`, `__PERM___DATA_PROTECTED`
- Role: **__DISPLAY_NAME__ Viewer** (holds both permissions)

## Test

1. Open Tenant Admin at http://localhost:5175 → **Home** → **__DISPLAY_NAME__**.
2. "Get sample data" works for any signed-in user; "Get protected data" is disabled.
3. In **Users**, assign the **__DISPLAY_NAME__ Viewer** role to your user, sign in again → the protected button is enabled.
