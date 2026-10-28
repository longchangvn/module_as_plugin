# Reports

Reports module.

Plugin module scaffolded by `tools/create-module --type plugin`. Developer guide: [docs/module-developer-guide.md](../../docs/module-developer-guide.md).

There is no standalone API process: the shared **Module Host** (:5300) loads the plugin in-process
and serves its API under `/plugins/reports` and its UI under
`/plugin-assets/reports/<version>/` (`assets/remoteEntry.js`). The manifest leaves
`frontend.remoteUrl` empty so the platform points the shell at the Module Host.

| Part | Location |
|------|----------|
| Plugin (class library, `IModulePlugin`) | `src/Modules/Reports/Reports.Plugin` |
| Remote SPA (federation, built into the package) | `modules/reports/web` (local dev: :5180) |
| Manifest | `modules/reports/manifest.yml` |
| Parity test | `tests/SharedKernel.Tests/Modules/ReportsModuleTests.cs` |

Plugin endpoints (relative to `/plugins/reports`): `GET /healthz`, `GET /readyz`, `POST /internal/seed:preview`,
`GET /api/v1/data`, `GET /api/v1/protected`. Upgrades run `IModulePlugin.MigrateAsync`.
Through the gateway (:5100): `/api/v1/reports/data`, `/api/v1/reports/protected`.

## Build & package

```bash
dotnet build src/Modules/Reports/Reports.Plugin
cd modules/reports/web && npm install && npm run serve   # optional: UI alone on :5180
node tools/create-module/create-module.mjs package reports   # dist/modules/reports-1.0.0.zip
```

The package holds `manifest.yml`, `README.md`, `plugin/` (publish output without the host-provided
`SharedKernel` / `ModulePlatform.Plugin.Abstractions` assemblies) and `web/` (Vite build, relative base).

## Publish & install

```bash
node tools/create-module/create-module.mjs publish reports      # builds + uploads the package
node tools/create-module/create-module.mjs install reports --tenant <TENANT_ID>
```

Seeded on install:
- Permissions: `REPORTS_DATA_VIEW`, `REPORTS_DATA_PROTECTED`
- Role: **Reports Viewer** (holds both permissions)

## Test

1. Open Tenant Admin at http://localhost:5175 → **Home** → **Reports**.
2. "Get sample data" works for any signed-in user; "Get protected data" is disabled.
3. In **Users**, assign the **Reports Viewer** role to your user, sign in again → the protected button is enabled.
