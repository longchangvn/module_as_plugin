# create-module

Zero-dependency Node (>= 18) CLI that scaffolds a platform module, wires it into the repo, and can
package, publish and install it. Two module types:

- **remote** (default): .NET 8 API with its own process + React/Vite federation remote + `manifest.yml`
  + parity test; wired into the solution, Aspire AppHost, `docker-compose.yml` and tests.
- **plugin**: .NET 8 class library implementing `IModulePlugin`, loaded in-process by the shared
  Module Host (:5300, API under `/plugins/<name>`, UI under `/plugin-assets/<name>/<version>/`);
  wired into the solution and tests only (no AppHost process, no compose service, `--api-port` ignored).

```bash
cd module_as_plugin
node tools/create-module/create-module.mjs new inventory --display-name "Inventory" -y
node tools/create-module/create-module.mjs new reports --type plugin -y
node tools/create-module/create-module.mjs package reports      # dist/modules/reports-1.0.0.zip
node tools/create-module/create-module.mjs publish reports      # plugin: builds + uploads the package
node tools/create-module/create-module.mjs install reports --tenant <TENANT_ID>
node tools/create-module/create-module.mjs list                 # NAME TYPE VERSION SCOPE API REMOTE
node tools/create-module/create-module.mjs help
```

- `templates/` is the source of truth (tokens like `__MODULE_NAME__`, `__PASCAL__`, `__PERM__`,
  `__PROJECT__` = `<Pascal>.Api` / `<Pascal>.Plugin`). `*.template` files are renamed on output
  (kept suffixed so IDEs don't restore them in place).
  - `api/` remote API project, `plugin/` plugin project, `module/` manifest + README + web remote,
    `module-plugin/` plugin overrides of `module/` files (manifest, README, `web/vite.config.ts` with a
    relative `base` so the build works from the Module Host sub-path; `web/Dockerfile` is not generated),
    `test/` parity test (both types).
- `package <name>`: zip with `manifest.yml` at the root (+ `README.md`). Plugins add `plugin/`
  (`dotnet publish -c Release`) and `web/` (`npm run build` output). Host-provided assemblies are removed
  from `plugin/`: `ModulePlatform.Plugin.Abstractions.*`, `SharedKernel.*`, and every library reachable
  only through them in `<Pascal>.Plugin.deps.json` (libraries the plugin references directly are kept);
  `deps.json` is pruned to match. `--skip-build` reuses existing builds (`dotnet publish --no-build`, `web/dist`).
- `publish <name>`: remote → JSON manifest publish (or `--package [file]` → upload); plugin → always
  `POST /api/v1/platform/modules/package` (multipart field `package`).
- Full guide: [docs/module-developer-guide.md](../../docs/module-developer-guide.md).
