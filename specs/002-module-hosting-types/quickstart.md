# Quickstart: Validate Module Hosting Types

Run from `module_as_plugin/`. Contracts: [platform-package-api](./contracts/platform-package-api.md), [plugin-host-api](./contracts/plugin-host-api.md), [manifest](./contracts/manifest-v1-types.md), [CLI](./contracts/cli.md).

## Prerequisites

- Core stack in Development (Aspire: `dotnet run --project src/Aspire/ModulePlatform.AppHost`), which now also starts the Module Host on `:5300`.
- Two active tenants (e.g. `tnt_demo` + one created in the shell) each with an admin you can sign in as.
- `PLATFORM_PASSWORD` set for `admin@local`.

## 1. Automated checks

```bash
dotnet build ModulePlatform.sln
dotnet test ModulePlatform.sln          # SharedKernel (types, package), Platform.Application (strategies), PluginHost (ALC, shells, HTTP)
(cd apps/shell && npx tsc --noEmit && npx vite build)
```

## 2. Package + register a plugin (US1, US5)

```bash
node tools/create-module/create-module.mjs list                     # sample3 shows TYPE=plugin
node tools/create-module/create-module.mjs package sample3          # dist/modules/sample3-1.0.0.zip
unzip -l dist/modules/sample3-1.0.0.zip                             # manifest.yml, plugin/Sample3.Plugin.dll, web/assets/remoteEntry.js; no SharedKernel.dll
node tools/create-module/create-module.mjs publish sample3          # 201 type=plugin packageSha256=...
```

Expect the catalog (shell `/modules`) to show **Sample Module 3** with a `plugin` badge. Re-publishing returns the duplicate-version error.

## 3. Install for one tenant only (US2)

```bash
node tools/create-module/create-module.mjs install sample3 --tenant tnt_demo
curl -s localhost:5300/internal/plugins -H "X-Internal-Key: dev-internal-key"   # runtime sample3@1.0.0, tenant tnt_demo
```

| # | Action | Expected |
|---|--------|----------|
| 1 | Tenant A token: `GET :5100/api/v1/sample3/data` | `200`, `module: sample3` (SC-003) |
| 2 | Tenant B token: same call | `404 UNKNOWN_MODULE_ROUTE` |
| 3 | Tenant Admin `:5175` as tenant A → Home | "Sample Module 3" card + page loads from `:5300/plugin-assets/...` |
| 4 | Install for B | Both 200; `/internal/plugins` still one runtime |
| 5 | Disable for A | A `503 MODULE_DISABLED`, B `200` |
| 6 | Uninstall for A, then B | after last uninstall `/internal/plugins` shows no runtime |
| 7 | While looping `GET :5100/api/v1/sample1/data` and `:5300/healthz`, repeat 4–6 | 0 failures (SC-002) |

## 4. Side-by-side upgrade (US3)

Bump `modules/sample3/manifest.yml` + `ModuleEndpoints.Version`-equivalent constant to `1.1.0`, `package` + `publish`, then upgrade A only:

```bash
curl -s -X POST localhost:5100/api/v1/platform/tenants/$A/modules/sample3/upgrade -H "Authorization: Bearer $PLATFORM_TOKEN" -H 'Content-Type: application/json' -d '{"toVersion":"1.1.0"}'
```

A's data reports `version: 1.1.0`, B's `1.0.0`; `/internal/plugins` shows both runtimes until B moves.

## 5. Unsafe packages rejected (US1, SC-005)

Covered by `SharedKernel.Tests` (`ModulePackageTests`): missing manifest, unknown type, missing plugin assembly, `../` entry, absolute path, disallowed root, oversize, too many entries. Manual: `curl -F package=@bad.zip ...` → `400 PACKAGE_INVALID` with entry-level details; `module-packages/` gains no files.

## 6. Remote zones (US4)

Publish a remote manifest with `service.network: public` and `baseUrl: http://...` against a non-Development Platform → `400 VALIDATION_ERROR` on `service.baseUrl`. Existing loyalty/sample1/sample2 publish/install unchanged (SC-007).

## 7. Restart self-heal (FR-021)

Stop the Module Host, delete `plugin-cache/`, start it again: within the first reconcile, `/internal/plugins` lists the same runtimes/tenants and tenant A's calls succeed.
