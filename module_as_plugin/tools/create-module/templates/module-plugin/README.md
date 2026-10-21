# __DISPLAY_NAME__

__DESCRIPTION__

Plugin module scaffolded by `tools/create-module --type plugin`. Developer guide: [docs/module-developer-guide.md](../../docs/module-developer-guide.md).

There is no standalone API process: the shared **Module Host** (:5300) loads the plugin in-process
and serves its API under `/plugins/__MODULE_NAME__` and its UI under
`/plugin-assets/__MODULE_NAME__/<version>/` (`assets/remoteEntry.js`). The manifest leaves
`frontend.remoteUrl` empty so the platform points the shell at the Module Host.

| Part | Location |
|------|----------|
| Plugin (class library, `IModulePlugin`) | `src/Modules/__PASCAL__/__PASCAL__.Plugin` |
| Remote SPA (federation, built into the package) | `modules/__MODULE_NAME__/web` (local dev: :__WEB_PORT__) |
| Manifest | `modules/__MODULE_NAME__/manifest.yml` |
| Parity test | `tests/SharedKernel.Tests/Modules/__PASCAL__ModuleTests.cs` |

Plugin endpoints (relative to `/plugins/__MODULE_NAME__`): `GET /healthz`, `GET /readyz`, `POST /internal/seed:preview`,
`GET /api/v1/data`, `GET /api/v1/protected`. Upgrades run `IModulePlugin.MigrateAsync`.
Through the gateway (:5100): `/api/v1/__MODULE_NAME__/data`, `/api/v1/__MODULE_NAME__/protected`.

## Build & package

```bash
dotnet build src/Modules/__PASCAL__/__PASCAL__.Plugin
cd modules/__MODULE_NAME__/web && npm install && npm run serve   # optional: UI alone on :__WEB_PORT__
node tools/create-module/create-module.mjs package __MODULE_NAME__   # dist/modules/__MODULE_NAME__-1.0.0.zip
```

The package holds `manifest.yml`, `README.md`, `plugin/` (publish output without the host-provided
`SharedKernel` / `ModulePlatform.Plugin.Abstractions` assemblies) and `web/` (Vite build, relative base).

## Publish & install

```bash
node tools/create-module/create-module.mjs publish __MODULE_NAME__      # builds + uploads the package
node tools/create-module/create-module.mjs install __MODULE_NAME__ --tenant <TENANT_ID>
```

Seeded on install:
- Permissions: `__PERM___DATA_VIEW`, `__PERM___DATA_PROTECTED`
- Role: **__DISPLAY_NAME__ Viewer** (holds both permissions)

## Test

1. Open Tenant Admin at http://localhost:5175 → **Home** → **__DISPLAY_NAME__**.
2. "Get sample data" works for any signed-in user; "Get protected data" is disabled.
3. In **Users**, assign the **__DISPLAY_NAME__ Viewer** role to your user, sign in again → the protected button is enabled.
