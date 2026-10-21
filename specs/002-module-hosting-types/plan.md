# Implementation Plan: Module Hosting Types (Standalone + In-Process Plugin)

**Branch**: `002-module-hosting-types` | **Date**: 2026-10-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-module-hosting-types/spec.md`

## Summary

Introduce a pluggable **module type** concept (initially `remote` and `plugin`) behind two small strategy registries, so the install lifecycle, catalog and gateway stay unchanged when types are added. Add **package (zip) registration** to the Platform with shared, safe inspection. Add a new always-on **Module Host** (`PluginHost.Api`) that loads plugin versions into collectible, isolated `AssemblyLoadContext`s, exposes their endpoints through a hot-swappable `EndpointDataSource`, and dispatches each request to the version in the caller's **tenant shell**. The Platform drives the host with the same push + reconcile protocol it already uses for the Gateway. Remote modules gain a declared network zone with transport rules. The shell UI gains package upload and type badges; the CLI gains `--type plugin`, `package`, and package publish. Design decisions: [research.md](./research.md).

## Technical Context

**Language/Version**: C# 12 / .NET 8 (module APIs, Platform, Gateway, Module Host); AppHost .NET 9 (Aspire 9.5.2); TypeScript 5.7 / React 18 / Vite 5 (shell); Node ≥ 18 (CLI)

**Primary Dependencies**: ASP.NET Core minimal APIs + routing (`EndpointDataSource`, `MatcherPolicy`), `System.Runtime.Loader` (`AssemblyLoadContext`, `AssemblyDependencyResolver`), `System.IO.Compression`, EF Core 8 + Npgsql, YamlDotNet, Semver, Ocelot 25 (unchanged), xUnit

**Storage**: PostgreSQL catalog (`ModuleVersions` gains 3 columns); package files on disk under `ModulePackages:Root` (Platform) and extraction cache under `PluginHost:CacheRoot` (Module Host)

**Testing**: xUnit (`SharedKernel.Tests`, `Platform.Application.Tests`, new `PluginHost.Tests` incl. in-memory `TestServer`-free integration via `WebApplication` on a random port), `tsc --noEmit` + `vite build` (shell), CLI smoke runs

**Target Platform**: Linux containers / macOS dev; Aspire for local orchestration

**Project Type**: Multi-service web platform (control plane + gateway + module host + SPAs + CLI)

**Performance Goals**: Plugin activation (load + endpoint swap) < 10 s for a typical plugin (SC-003); request dispatch overhead negligible (one dictionary lookup + matcher policy)

**Constraints**: Zero downtime on plugin changes (SC-002); backward compatible manifests (SC-007); no gateway code change; secrets via existing `InternalApi:Key` / `Jwt:SecretKey`

**Scale/Scope**: Tens of tenants × tens of plugins per host; one host replica load-tested (assumption)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` is an unfilled template, so there are no ratified principles to gate on. Applied instead: repository `AGENTS.md` (root, `src/`, `apps/`) rules — reuse existing patterns (push + reconcile, `X-Internal-Key`, `ApiErrors.Problem`, minimal-API endpoint classes), server-side authorization, fail-closed checks, no secrets in code, tests for success/failure paths. **Status: PASS** (pre- and post-design).

## Architecture

```text
                 upload zip                         install/enable/disable/upgrade/uninstall
Admin ──► Platform.Api ── ModulePackage.Inspect ──► ModuleHostingRegistry ──► IModuleHostingStrategy
             │  (validate, store, catalog row)                               │            │
             │                                                RemoteHostingStrategy  PluginHostingStrategy
             │                                                 (no-op activate)      (IPluginHostClient push)
             │                                                               │            │
             ├──────────── gateway push (baseUrl, upstreams from strategy) ──┘            ▼
             │                                                            PluginHost.Api :5300
             ▼                                                             ├ TenantShellRegistry (snapshot swap)
         Gateway :5100 ── /api/v1/{module}/... ──► remote module API       ├ PluginRuntime per (module, version)
                         └─────────────────────► PluginHost /plugins/{module}/...  (collectible ALC + own DI)
                                                                           ├ PluginEndpointDataSource + matcher policy
                                                                           └ /plugin-assets/{module}/{version}/**
```

Request path for a plugin: browser → Gateway (JWT, per-tenant module gate unchanged) → Module Host `/plugins/{moduleId}/...` → `TenantShellMiddleware` (JWT tenant, header match, shell decision → 404/503, pins runtime, in-flight++) → routing (+ `PluginTenantMatcherPolicy`) → authorization → `PluginServicesMiddleware` (RequestServices = runtime scope) → plugin endpoint.

## Components Affected

### Backend — SharedKernel (`src/Shared/SharedKernel/`)
- `ManifestModels.cs`: `ManifestModule.Type`, `ManifestService.Network/BaseUrl`, `ManifestFile.Plugin` (`ManifestPlugin { Assembly, EntryType, Contract }`), helpers `ModuleTypeNames`.
- New `ModuleTypes.cs`: `IModuleTypeRules`, `RemoteModuleTypeRules`, `PluginModuleTypeRules`, `ModuleTypeRegistry` (default + `Register` for extension).
- `ManifestValidator.cs`: type lookup + delegation; remote-only `remoteUrl` requirement moved into remote rules.
- New `ModulePackage.cs`: `ModulePackageOptions`, `ModulePackage.Inspect(Stream)` → `Result<ModulePackageInfo>`, `ModulePackage.ExtractTo(zipPath, dir, options)`.
- `ErrorCodes.cs`: `PACKAGE_INVALID`, `PLUGIN_LOAD_FAILED`, `PLUGIN_HOST_UNAVAILABLE`.

### Backend — Plugin contract (new `src/Shared/ModulePlatform.Plugin.Abstractions/`)
- `IModulePlugin`, `PluginContext`, `PluginMigration`, `PluginContract`.

### Backend — Platform (`src/Platform/`)
- Domain `Entities.cs`: `ModuleVersion.ModuleType`, `PackageSha256`, `PackagePath`.
- Infrastructure: EF config default + migration `ModuleHostingTypes`.
- Application: new `ModuleHosting.cs` — `IModuleHostingStrategy`, `GatewayTarget`, `ModuleHostingRegistry`, `RemoteHostingStrategy`, `PluginHostingStrategy`, `IPluginHostClient`, `PluginHostOptions`.
- Api `Services/EfLifecyclePorts.cs`: route gateway target + activation through the registry; migrate through strategy.
- Api new `Services/ModulePackageStore.cs`, `Services/HttpPluginHostClient.cs`.
- Api `Endpoints/ModuleRegistryEndpoints.cs`: shared `PublishVersionAsync`; JSON publish rejects plugins and defaults `artifactApiUrl` from `service.baseUrl`; new `POST /api/v1/platform/modules/package` (multipart); catalog list/detail expose `type`.
- Api `Endpoints/InternalEndpoints.cs`: gateway snapshot uses strategies; new `GET /internal/modules/{id}/versions/{version}/package`, `GET /internal/plugin-host/states`.
- Api `Endpoints/BootstrapEndpoints.cs`: derived plugin `remoteUrl`.
- Api `Program.cs`: DI for registry/strategies/options/client/store; antiforgery-free form handling; request size limit for package endpoint.

### Backend — Module Host (new `src/PluginHost/PluginHost.Api/`)
- `Program.cs`, `Loading/PluginLoadContext.cs`, `Runtime/PluginRuntime.cs`, `Runtime/PluginRuntimeFactory.cs`, `Shells/TenantShellRegistry.cs`, `Routing/PluginEndpointDataSource.cs`, `Routing/PluginTenantMatcherPolicy.cs`, `Middleware/TenantShellMiddleware.cs`, `Middleware/PluginServicesMiddleware.cs`, `Services/PlatformPackageClient.cs`, `Services/ShellReconciler.cs`, `Endpoints/InternalEndpoints.cs`, `Endpoints/AssetEndpoints.cs`, `Dockerfile`, `appsettings*.json`, `Properties/launchSettings.json` (:5300).

### Backend — Gateway
- No code change (plugin routes are regular per-tenant module routes whose base URL is the Module Host).

### Frontend — Shell (`apps/shell/`)
- `src/components/RegisterModuleDialog.tsx`: "Upload package" (zip, multipart) and "Paste manifest" modes.
- `src/lib/api-client.ts`: multipart helper (no JSON content-type).
- `src/components/RegistryModuleCard.tsx` (+ types): module type badge.

### Orchestration
- `src/Aspire/ModulePlatform.AppHost/`: `PluginHost.Api` resource (:5300) + env for Platform (`PluginHost__BaseUrl`, `PluginHost__PublicBaseUrl`).
- `docker-compose.yml`: `plugin-host` service + Platform env + volumes for packages/cache.
- `ModulePlatform.sln`: new projects.

### CLI (`module_as_plugin/tools/create-module/`)
- `create-module.mjs`: `--type`, plugin plan/wiring, `package`, package publish, `list` TYPE, `remove` for plugin layout.
- `templates/plugin/` (plugin project + test), `templates/module-plugin/` overrides (manifest, README) reusing `templates/module/web`.

### Sample
- `sample3` plugin module generated with the CLI (dogfood + E2E target).

## API Changes

See [contracts/](./contracts/): `platform-package-api.md` (new upload + internal endpoints), `plugin-host-api.md` (Module Host internal + public paths), `plugin-contract.md` (`IModulePlugin`), `manifest-v1-types.md` (manifest additions + validation), `cli.md`.

Backward compatibility: all existing routes keep their contracts; responses only gain fields (`type`). Existing manifests validate unchanged (defaults).

## Database Changes

Additive migration on `ModuleVersions`: `ModuleType text not null default 'remote'`, `PackageSha256 text null`, `PackagePath text null`. No data backfill needed; no index change. Rollback: drop the three columns (no other table references them).

## Security Considerations

- Package upload Admin-only (`PlatformAny` + role check), audited; size-limited request body; zip-slip/entry-root/size/entry-count checks before any write; SHA-256 stored and re-verified by the host before load.
- Internal endpoints (package download, host state, host push) require `X-Internal-Key` (fixed-time compare), are not routed by the gateway.
- Module Host validates JWT (same signing key), requires `tenantId` claim, enforces header match (R9), fails closed on shell miss; plugins still call `ModuleGuards` for tenant usability + permission (fail closed).
- Plugins are trusted code (assumption); untrusted modules must use the remote type. Plugin assets endpoint serves only files inside the extracted `web/` root (path normalization).
- Remote zones: https enforced for cross-cluster/public outside Development.

## Error Handling

- Registration: `400 VALIDATION_ERROR` (manifest field list) / `400 PACKAGE_INVALID` (file-level reasons) / `413` oversize / `400` duplicate version.
- Lifecycle: host push failure → install `FAILED` + `502 PROVISION_FAILED` retryable (existing mapping); `PLUGIN_LOAD_FAILED` message propagated.
- Host request path: `401 UNAUTHORIZED`, `403 FORBIDDEN_TENANT`, `404 UNKNOWN_MODULE_ROUTE`, `503 MODULE_DISABLED` (same envelope as gateway).

## Observability

- Structured logs: `plugin.load|unload|activate|deactivate|load_failed module= version= tenant= in=Ns`.
- Meter `ModulePlatform.PluginHost`: `plugin.load.count{result}`, `plugin.load.duration`, gauge-like log of loaded runtimes; `GET /internal/plugins` read-only operator view (FR-030).
- Existing lifecycle meters unchanged.

## Testing Strategy

- **SharedKernel.Tests**: type registry (default remote, unknown type lists supported, custom type registration), plugin rules (assembly required/format, contract), remote network rules (https for cross-cluster/public outside dev), package inspection (valid remote/plugin, missing manifest, path escape, disallowed root, oversize, entry count, missing plugin dll, missing remoteEntry when needed), safe extraction.
- **Platform.Application.Tests**: hosting registry resolution + unknown type; remote/plugin `GatewayTarget`; plugin activate/deactivate ordering via fake `IPluginHostClient`; fake third type through `ModuleHostingRegistry` (SC-006).
- **PluginHost.Tests**: load sample plugin into isolated ALC (different `Assembly` instance than default), contract mismatch refused, tenant shell decisions (not installed / disabled / enabled), side-by-side versions per tenant, runtime unload when unreferenced (WeakReference), end-to-end HTTP through a running host (`/plugins/sample3/api/v1/data` for tenant A 200 vs tenant B 404, during repeated activate/deactivate of another tenant no failures).
- **Frontend**: `tsc --noEmit`, `vite build`.
- **CLI**: `new --type plugin --dry-run`, real scaffold of `sample3`, `package`, `list`, `remove` round trip on a throwaway module.
- **Regression**: full `dotnet test`, existing sample manifests validate.

## Migration Considerations

- Deploy order: Platform (migration + new endpoints) → Module Host → shell UI. Gateway untouched. Old Platform with new manifests: unknown keys ignored, so no break during rollout.
- Existing catalog rows default to `remote`.

## Backward Compatibility

- Manifests without `module.type` → `remote`; `service.network` default `same-cluster`; JSON publish unchanged for remote modules.
- Bootstrap payload shape unchanged (only `remoteUrl` derived for plugins).

## Project Structure

### Documentation (this feature)

```text
specs/002-module-hosting-types/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── manifest-v1-types.md
│   ├── platform-package-api.md
│   ├── plugin-host-api.md
│   ├── plugin-contract.md
│   └── cli.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
module_as_plugin/
├── src/Shared/SharedKernel/                    # ManifestModels, ModuleTypes, ManifestValidator, ModulePackage, ErrorCodes
├── src/Shared/ModulePlatform.Plugin.Abstractions/   # NEW plugin contract
├── src/Platform/Platform.Domain|Infrastructure|Application|Api/   # type columns, strategies, package endpoints
├── src/PluginHost/PluginHost.Api/              # NEW Module Host (:5300)
├── src/Modules/Sample3/Sample3.Plugin/         # NEW sample plugin (CLI-generated)
├── modules/sample3/                            # manifest + web (CLI-generated)
├── src/Aspire/ModulePlatform.AppHost/          # plugin-host resource
├── apps/shell/src/components/RegisterModuleDialog.tsx, RegistryModuleCard.tsx, lib/api-client.ts
├── tools/create-module/                        # --type plugin, package, publish --package
├── tests/SharedKernel.Tests/, tests/Platform.Application.Tests/, tests/PluginHost.Tests/ (NEW)
└── docs/module-developer-guide.md, README.md, SETUP.md
```

**Structure Decision**: Extend the existing multi-project solution; one new service project (Module Host), one new contract library, one new test project. Follows the existing Clean Architecture split in Platform (strategies + ports in Application, HTTP adapters in Api).

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| New service (Module Host) | In-process plugins need a host that is not the control plane | Loading into Platform.Api exposes catalog/tenant secrets to plugin code and couples plugin faults to registry/login outages |
| New contract assembly | Plugins must compile against a stable, minimal contract that the host shares | Referencing Platform/host projects would leak internals and break type identity across load contexts |
