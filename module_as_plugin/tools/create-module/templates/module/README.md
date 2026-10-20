# __DISPLAY_NAME__

__DESCRIPTION__

Scaffolded by `tools/create-module`. Developer guide: [docs/module-developer-guide.md](../../docs/module-developer-guide.md).

| Part | Location | Port |
|------|----------|------|
| API | `src/Modules/__PASCAL__/__PASCAL__.Api` | __API_PORT__ |
| Remote SPA (federation) | `modules/__MODULE_NAME__/web` | __WEB_PORT__ (`http://localhost:__WEB_PORT__/assets/remoteEntry.js`) |
| Manifest | `modules/__MODULE_NAME__/manifest.yml` | — |
| Parity test | `tests/SharedKernel.Tests/Modules/__PASCAL__ModuleTests.cs` | — |

API endpoints: `GET /healthz`, `GET /readyz`, `POST /internal/seed:preview`, `POST /internal/migrate`, `GET /api/v1/data`, `GET /api/v1/protected`.
Through the gateway (:5100): `/api/v1/__MODULE_NAME__/data`, `/api/v1/__MODULE_NAME__/protected`.

## Run

Aspire (`dotnet run --project src/Aspire/ModulePlatform.AppHost`) starts both parts. Standalone:

```bash
dotnet run --project src/Modules/__PASCAL__/__PASCAL__.Api
cd modules/__MODULE_NAME__/web && npm install && npm run serve   # build + vite preview on :__WEB_PORT__
```

## Publish & install

```bash
node tools/create-module/create-module.mjs publish __MODULE_NAME__
node tools/create-module/create-module.mjs install __MODULE_NAME__ --tenant <TENANT_ID>
```

Seeded on install:
- Permissions: `__PERM___DATA_VIEW`, `__PERM___DATA_PROTECTED`
- Role: **__DISPLAY_NAME__ Viewer** (holds both permissions)

## Test

1. Open Tenant Admin at http://localhost:5175 → **Home** → **__DISPLAY_NAME__**.
2. "Get sample data" works for any signed-in user; "Get protected data" is disabled.
3. In **Users**, assign the **__DISPLAY_NAME__ Viewer** role to your user, sign in again → the protected button is enabled.
