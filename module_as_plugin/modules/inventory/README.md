# Inventory

Inventory module.

Scaffolded by `tools/create-module`. Developer guide: [docs/module-developer-guide.md](../../docs/module-developer-guide.md).

| Part | Location | Port |
|------|----------|------|
| API | `src/Modules/Inventory/Inventory.Api` | 5204 |
| Remote SPA (federation) | `modules/inventory/web` | 5179 (`http://localhost:5179/assets/remoteEntry.js`) |
| Manifest | `modules/inventory/manifest.yml` | — |
| Parity test | `tests/SharedKernel.Tests/Modules/InventoryModuleTests.cs` | — |

API endpoints: `GET /healthz`, `GET /readyz`, `POST /internal/seed:preview`, `POST /internal/migrate`, `GET /api/v1/data`, `GET /api/v1/protected`.
Through the gateway (:5100): `/api/v1/inventory/data`, `/api/v1/inventory/protected`.

## Run

Aspire (`dotnet run --project src/Aspire/ModulePlatform.AppHost`) starts both parts. Standalone:

```bash
dotnet run --project src/Modules/Inventory/Inventory.Api
cd modules/inventory/web && npm install && npm run serve   # build + vite preview on :5179
```

## Publish & install

```bash
node tools/create-module/create-module.mjs publish inventory
node tools/create-module/create-module.mjs install inventory --tenant <TENANT_ID>
```

Seeded on install:
- Permissions: `INVENTORY_DATA_VIEW`, `INVENTORY_DATA_PROTECTED`
- Role: **Inventory Viewer** (holds both permissions)

## Test

1. Open Tenant Admin at http://localhost:5175 → **Home** → **Inventory**.
2. "Get sample data" works for any signed-in user; "Get protected data" is disabled.
3. In **Users**, assign the **Inventory Viewer** role to your user, sign in again → the protected button is enabled.
