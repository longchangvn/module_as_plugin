# Orders

Orders module.

Hybrid module scaffolded by `tools/create-module --type hybrid`. Developer guide: `module_as_plugin/docs/module-developer-guide.md`.

- **UI**: built into the package (`web/`) and served by the shared **Module Host** (:5300) under
  `/plugin-assets/orders/<version>/` (`assets/remoteEntry.js`). The manifest leaves
  `frontend.remoteUrl` empty so the platform points the shell at the Module Host.
- **API**: a standalone ASP.NET Core process (:5205). The gateway routes
  `/api/v1/orders/...` to the published `artifactApiUrl` (default: `service.baseUrl`, else the local port).

The module lives outside the platform project folder (`module_as_plugin/`), in `modules/orders/`:

| Part | Location |
|------|----------|
| API (standalone host) | `modules/orders/api/Orders.Api` |
| Remote SPA (federation, built into the package) | `modules/orders/web` (local dev: :5181) |
| Manifest | `modules/orders/manifest.yml` |
| Parity test | `module_as_plugin/tests/SharedKernel.Tests/Modules/OrdersModuleTests.cs` |

API endpoints: `GET /healthz`, `GET /readyz`, `POST /internal/seed:preview`, `POST /internal/migrate`,
`GET /api/v1/data`, `GET /api/v1/protected`.
Through the gateway (:5100): `/api/v1/orders/data`, `/api/v1/orders/protected`.

## Run, package, publish, install (from `module_as_plugin/`)

```bash
dotnet run --project ../modules/orders/api/Orders.Api                 # API :5205 (or via the Aspire AppHost)
node tools/create-module/create-module.mjs package orders                 # dist/modules/orders-1.0.0.zip (manifest + README + web/)
node tools/create-module/create-module.mjs publish orders --password <pw> # builds + uploads the package (artifactApiUrl = local API port)
node tools/create-module/create-module.mjs install orders --tenant <TENANT_ID> --password <pw>
```

Docker (build context = the folder that contains both `module_as_plugin/` and `modules/`):
`docker build -f modules/orders/api/Orders.Api/Dockerfile .`

Seeded on install:
- Permissions: `ORDERS_DATA_VIEW`, `ORDERS_DATA_PROTECTED`
- Role: **Orders Viewer** (holds both permissions)

## Test

1. Open Tenant Admin at http://localhost:5175 → **Home** → **Orders**.
2. "Get sample data" works for any signed-in user; "Get protected data" is disabled.
3. In **Users**, assign the **Orders Viewer** role to your user, sign in again → the protected button is enabled.
