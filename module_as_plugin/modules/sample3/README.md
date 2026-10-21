# Sample Module 3

Test plugin module - loaded in-process by the Module Host; open + permission-gated data.

Plugin module scaffolded by `tools/create-module --type plugin`. Developer guide: [docs/module-developer-guide.md](../../docs/module-developer-guide.md).

There is no standalone API process: the shared **Module Host** (:5300) loads the plugin in-process
and serves its API under `/plugins/sample3` and its UI under
`/plugin-assets/sample3/<version>/` (`assets/remoteEntry.js`). The manifest leaves
`frontend.remoteUrl` empty so the platform points the shell at the Module Host.

| Part | Location |
|------|----------|
| Plugin (class library, `IModulePlugin`) | `src/Modules/Sample3/Sample3.Plugin` |
| Remote SPA (federation, built into the package) | `modules/sample3/web` (local dev: :5178) |
| Manifest | `modules/sample3/manifest.yml` |
| Parity test | `tests/SharedKernel.Tests/Modules/Sample3ModuleTests.cs` |

Plugin endpoints (relative to `/plugins/sample3`): `GET /healthz`, `GET /readyz`, `POST /internal/seed:preview`,
`GET /api/v1/data`, `GET /api/v1/protected`. Upgrades run `IModulePlugin.MigrateAsync`.
Through the gateway (:5100): `/api/v1/sample3/data`, `/api/v1/sample3/protected`.

## Build & package

```bash
dotnet build src/Modules/Sample3/Sample3.Plugin
cd modules/sample3/web && npm install && npm run serve   # optional: UI alone on :5178
node tools/create-module/create-module.mjs package sample3   # dist/modules/sample3-1.0.0.zip
```

The package holds `manifest.yml`, `README.md`, `plugin/` (publish output without the host-provided
`SharedKernel` / `ModulePlatform.Plugin.Abstractions` assemblies) and `web/` (Vite build, relative base).

## Publish & install

```bash
node tools/create-module/create-module.mjs publish sample3      # builds + uploads the package
node tools/create-module/create-module.mjs install sample3 --tenant <TENANT_ID>
```

Seeded on install:
- Permissions: `SAMPLE_THREE_DATA_VIEW`, `SAMPLE_THREE_DATA_PROTECTED`
- Role: **Sample Module 3 Viewer** (holds both permissions)

## Test

1. Open Tenant Admin at http://localhost:5175 → **Home** → **Sample Module 3**.
2. "Get sample data" works for any signed-in user; "Get protected data" is disabled.
3. In **Users**, assign the **Sample Module 3 Viewer** role to your user, sign in again → the protected button is enabled.
