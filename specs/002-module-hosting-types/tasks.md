---

description: "Task list for Module Hosting Types (standalone + in-process plugin)"
---

# Tasks: Module Hosting Types (Standalone + In-Process Plugin)

**Input**: Design documents from `/specs/002-module-hosting-types/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included. The plan's Testing Strategy and success criteria SC-002/SC-005/SC-006 call for automated tests.

**Organization**: Tasks are grouped by user story. All paths are relative to `module_as_plugin/` unless they start with `specs/`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1..US6 from spec.md

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Create contract library `src/Shared/ModulePlatform.Plugin.Abstractions/ModulePlatform.Plugin.Abstractions.csproj` (net8.0, `FrameworkReference Microsoft.AspNetCore.App`) and add it to `ModulePlatform.sln`
- [X] T002 [P] Create Module Host project `src/PluginHost/PluginHost.Api/PluginHost.Api.csproj` (net8.0 web; refs SharedKernel, ServiceDefaults, Plugin.Abstractions; JwtBearer 8.0.11) with `Properties/launchSettings.json` (:5300), `appsettings.json`, `appsettings.Development.json` and add to `ModulePlatform.sln`
- [X] T003 [P] Create test project `tests/PluginHost.Tests/PluginHost.Tests.csproj` (xunit 2.9.2, refs PluginHost.Api) and add to `ModulePlatform.sln`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Contracts every story depends on (manifest types, package inspection, plugin contract, strategy registry, schema).

- [X] T004 Add `ManifestModule.Type` (default "remote"), `ManifestService.Network` (default "same-cluster") / `BaseUrl`, `ManifestFile.Plugin` (`ManifestPlugin { Assembly, EntryType, Contract = "1.0" }`) in `src/Shared/SharedKernel/ManifestModels.cs`
- [X] T005 Implement `IModuleTypeRules`, `RemoteModuleTypeRules`, `PluginModuleTypeRules`, `ModuleTypeRegistry` (default remote+plugin, `Register`, `Supported`) in `src/Shared/SharedKernel/ModuleTypes.cs` per contracts/manifest-v1-types.md (assembly regex `^[A-Za-z0-9][A-Za-z0-9._-]*\.dll$`, contract "1.0", network `same-cluster|cross-cluster|public`, https for non same-cluster outside Development, plugin forbids `service.baseUrl`)
- [X] T006 Delegate type validation from `src/Shared/SharedKernel/ManifestValidator.cs` (unknown type → "Unknown module type '{t}'. Supported: …"; move remote-only `remoteUrl` requirement into remote rules; plugin `remoteUrl` optional)
- [X] T007 [P] Implement `ModulePackageOptions` (50 MB / 200 MB / 2,000 entries), `ModulePackageInfo`, `ModulePackage.Inspect(Stream, options, isDevelopment)` and `ModulePackage.ExtractTo(zipPath, targetDir, options)` in `src/Shared/SharedKernel/ModulePackage.cs` (roots `manifest.yml|README.md|plugin/|web/`, reject absolute/drive/`..`, SHA-256 of bytes, plugin assembly present, remoteEntry required for plugin with routes and empty remoteUrl)
- [X] T008 [P] Add `PackageInvalid = "PACKAGE_INVALID"`, `PluginLoadFailed = "PLUGIN_LOAD_FAILED"`, `PluginHostUnavailable = "PLUGIN_HOST_UNAVAILABLE"` to `src/Shared/SharedKernel/ErrorCodes.cs`
- [X] T009 [P] Implement `IModulePlugin`, `PluginContext`, `PluginMigration`, `PluginContract` in `src/Shared/ModulePlatform.Plugin.Abstractions/IModulePlugin.cs` per contracts/plugin-contract.md
- [X] T010 Add `ModuleType` (default "remote"), `PackageSha256`, `PackagePath` to `ModuleVersion` in `src/Platform/Platform.Domain/Entities.cs`, configure default in `src/Platform/Platform.Infrastructure/CatalogDbContext.cs`, and generate migration `ModuleHostingTypes` in `src/Platform/Platform.Infrastructure/Migrations/`
- [X] T011 Implement `IModuleHostingStrategy`, `GatewayTarget`, `GatewayUpstream`, `ModuleVersionRef`, `ModuleHostingRegistry`, `RemoteHostingStrategy`, `PluginHostingStrategy`, `IPluginHostClient`, `PluginHostOptions` in `src/Platform/Platform.Application/ModuleHosting.cs` (plugin target: host base URL + downstream prefixed `/plugins/{moduleId}`)
- [X] T012 [P] Tests for type registry, plugin/remote rules and backward compatibility (existing sample manifests) in `tests/SharedKernel.Tests/ModuleTypeTests.cs`
- [X] T013 [P] Tests for package inspection + safe extraction (valid remote/plugin, missing manifest, unknown type, missing assembly, `../`, absolute path, disallowed root, oversize, entry count, missing remoteEntry) in `tests/SharedKernel.Tests/ModulePackageTests.cs`
- [X] T014 [P] Tests for `ModuleHostingRegistry` + remote/plugin `GatewayTarget` in `tests/Platform.Application.Tests/ModuleHostingTests.cs`

**Checkpoint**: manifests carry types, packages can be inspected, strategies resolve.

---

## Phase 3: User Story 1 - Register a module by uploading a package (P1) 🎯 MVP

**Goal**: Admin uploads a zip; type detected; validated; stored; listed with type.

**Independent Test**: upload valid remote + plugin packages → catalog shows both types; malformed packages → `400` with no stored files.

- [X] T015 [US1] Implement `ModulePackageStore` (save `{root}/{id}/{version}/{sha256}-{nonce}.zip` via temp + move — unique per upload, open read stream, delete only its own file on failure; `ModulePackages:Root` default `./module-packages`; reject resolved paths outside root) in `src/Platform/Platform.Api/Services/ModulePackageStore.cs`
- [X] T016 [US1] Refactor publish into shared `PublishVersionAsync` and add `POST /api/v1/platform/modules/package` (multipart, Admin, size limit, `PACKAGE_INVALID` details, duplicate version, audit) in `src/Platform/Platform.Api/Endpoints/ModuleRegistryEndpoints.cs`; JSON publish rejects plugin manifests and defaults `artifactApiUrl` from `service.baseUrl`
- [X] T017 [US1] Expose `type` on `GET /api/v1/platform/modules` and `type`/`packageSha256` on version detail in `src/Platform/Platform.Api/Endpoints/ModuleRegistryEndpoints.cs`
- [X] T018 [US1] Register `ModulePackageStore`, `ModulePackageOptions`, form limits in `src/Platform/Platform.Api/Program.cs`
- [X] T019 [P] [US1] Shell: multipart helper in `apps/shell/src/lib/api-client.ts`; "Upload package" / "Paste manifest" modes with inline errors in `apps/shell/src/components/RegisterModuleDialog.tsx`; type badge in `apps/shell/src/components/RegistryModuleCard.tsx` (+ `type` in the registry module type)

**Checkpoint**: US1 independently demonstrable via curl/shell.

---

## Phase 4: User Story 2 - Install a plugin for one tenant without affecting others (P1)

**Goal**: Module Host loads plugin versions once, tenant shells gate access, zero downtime.

**Independent Test**: tenant A 200 / tenant B 404 for the plugin API; other traffic unaffected during changes.

- [X] T020 [P] [US2] Implement `PluginLoadContext` (collectible; host-provided assemblies → default context; private deps via `AssemblyDependencyResolver`) in `src/PluginHost/PluginHost.Api/Loading/PluginLoadContext.cs`
- [X] T021 [P] [US2] Implement `PluginEndpointDataSource` (swap + change token) and `PluginEndpointMetadata` in `src/PluginHost/PluginHost.Api/Routing/PluginEndpointDataSource.cs`, and `PluginTenantMatcherPolicy` in `src/PluginHost/PluginHost.Api/Routing/PluginTenantMatcherPolicy.cs`
- [X] T022 [US2] Implement `PluginRuntime` (in-flight counter, drain, dispose + ALC unload) and `PluginRuntimeFactory` (load entry type, contract check, build runtime DI from shared host services, map endpoints under `/plugins/{moduleId}`) in `src/PluginHost/PluginHost.Api/Runtime/`
- [X] T023 [US2] Implement `TenantShellRegistry` (immutable snapshot, single writer, load-before-swap, `Decide`, `Apply`, `Remove`, `Replace` for reconcile, release unreferenced runtimes after drain) in `src/PluginHost/PluginHost.Api/Shells/TenantShellRegistry.cs`
- [X] T024 [US2] Implement `PlatformPackageClient` (download package with `X-Internal-Key`, verify SHA-256, extract via `ModulePackage.ExtractTo` into `PluginHost:CacheRoot/{id}/{version}`) in `src/PluginHost/PluginHost.Api/Services/PlatformPackageClient.cs`
- [X] T025 [US2] Implement `TenantShellMiddleware` (JWT tenant, header match, 401/403/404/503 envelopes, pin runtime + in-flight) and `PluginServicesMiddleware` (RequestServices = runtime scope) in `src/PluginHost/PluginHost.Api/Middleware/`
- [X] T026 [US2] Implement internal endpoints (`PUT/DELETE /internal/plugins/{moduleId}/tenants/{tenantId}`, `POST .../versions/{version}/migrate`, `GET /internal/plugins`) with `X-Internal-Key` in `src/PluginHost/PluginHost.Api/Endpoints/InternalEndpoints.cs`
- [X] T027 [US2] Implement `/plugin-assets/{moduleId}/{version}/{**path}` (anonymous, CORS, cache headers, no path escape) in `src/PluginHost/PluginHost.Api/Endpoints/AssetEndpoints.cs`
- [X] T028 [US2] Implement `ShellReconciler` hosted service (startup + every `ReconcileIntervalMinutes` from `GET /internal/plugin-host/states`, skip snapshot older than a newer push) in `src/PluginHost/PluginHost.Api/Services/ShellReconciler.cs`
- [X] T029 [US2] Compose pipeline (auth → shell middleware → routing → authorization → plugin services → endpoints; data source + matcher policy registration; named http clients; meter + structured logs) in `src/PluginHost/PluginHost.Api/Program.cs` and add `src/PluginHost/PluginHost.Api/Dockerfile`
- [X] T030 [US2] Platform: `HttpPluginHostClient` (PUT/DELETE/migrate with `X-Internal-Key`, map 422/5xx to exceptions with host message) in `src/Platform/Platform.Api/Services/HttpPluginHostClient.cs`; register registry/strategies/options/client in `src/Platform/Platform.Api/Program.cs`
- [X] T031 [US2] Route lifecycle side effects through strategies in `src/Platform/Platform.Api/Services/EfLifecyclePorts.cs` (activate before gateway push when enabled; on gateway push failure after activation best-effort deactivate (spec edge case); gateway disable before host disable; gateway remove before deactivate, host deactivate failure on uninstall logged as warning and left to reconcile; migrate via strategy)
- [X] T032 [US2] Add `GET /internal/modules/{id}/versions/{version}/package` and `GET /internal/plugin-host/states`; make `GET /internal/gateway/module-states` use strategies in `src/Platform/Platform.Api/Endpoints/InternalEndpoints.cs`
- [X] T033 [US2] Derive plugin `remoteUrl` from `PluginHost:PublicBaseUrl` in `src/Platform/Platform.Api/Endpoints/BootstrapEndpoints.cs`
- [X] T034 [P] [US2] Test plugin fixture project `tests/PluginHost.Tests.FixturePlugin/` (implements `IModulePlugin`, `/api/v1/data` returns version, Private=false refs) used by PluginHost.Tests
- [X] T035 [US2] Tests: ALC isolation + contract mismatch + unload + shared assembly (Plugin.Abstractions/SharedKernel) resolved from the host even when a copy sits in the plugin folder in `tests/PluginHost.Tests/PluginLoadingTests.cs`; shell decisions + side-by-side + release in `tests/PluginHost.Tests/TenantShellRegistryTests.cs`; HTTP end-to-end (tenant A 200 / B 404 / disabled 503 / header mismatch 403; no failures for tenant B while A toggles) in `tests/PluginHost.Tests/PluginHostHttpTests.cs`
- [X] T036 [P] [US2] Tests: plugin strategy ordering (activate before gateway, remove gateway before deactivate) with fakes in `tests/Platform.Application.Tests/ModuleHostingTests.cs`
- [X] T037 [US2] Wire Module Host into `src/Aspire/ModulePlatform.AppHost/Program.cs` + `ModulePlatform.AppHost.csproj` (:5300, Platform env `PluginHost__BaseUrl`, `PluginHost__PublicBaseUrl`) and `docker-compose.yml` (`plugin-host` service, volumes `pluginpackages`, `plugincache`)

**Checkpoint**: plugin installable per tenant end to end.

---

## Phase 5: User Story 3 - Upgrade a plugin per tenant (P2)

**Goal**: Side-by-side versions per tenant, rollback on load failure.

**Independent Test**: A on v1.1.0, B on v1.0.0, each served by its own version.

- [X] T038 [US3] Ensure upgrade path calls host migrate then activate for the new version (tenant stays on old version if load fails) in `src/Platform/Platform.Application/ModuleHosting.cs` (`PluginHostingStrategy.MigrateAsync`) and `src/Platform/Platform.Api/Services/EfLifecyclePorts.cs` (`RunMigrateHookAsync`)
- [X] T039 [US3] Test side-by-side routing per tenant (two fixture versions) and failed-upgrade keeps previous runtime in `tests/PluginHost.Tests/TenantShellRegistryTests.cs`

---

## Phase 6: User Story 4 - Standalone modules declare how they are reached (P2)

**Goal**: Network zones + transport rules for remote modules.

**Independent Test**: zone/https validation cases; legacy manifests default to same-cluster.

- [X] T040 [US4] Tests for network zone validation (default, invalid zone, https rule outside Development, Development allows http) in `tests/SharedKernel.Tests/ModuleTypeTests.cs`
- [X] T041 [US4] Use `service.baseUrl` as default artifact URL for JSON and package publish (covered by T016) and document the zone matrix in `docs/module-developer-guide.md`

---

## Phase 7: User Story 5 - Developer tooling for both module types (P2)

**Goal**: CLI scaffolds/packages/publishes both types.

**Independent Test**: `new --type plugin` → `package` → `publish` → `install` works for sample3.

- [X] T042 [P] [US5] Plugin templates: `tools/create-module/templates/plugin/__PASCAL__.Plugin.csproj.template`, `__PASCAL__Plugin.cs`, `Models/Requests.cs`; plugin manifest + README overrides in `tools/create-module/templates/module-plugin/`; plugin parity test `tools/create-module/templates/test-plugin/__PASCAL__ModuleTests.cs`
- [X] T043 [US5] CLI: `--type`, plugin plan/wiring (sln + tests only), `package`, `publish --package` (multipart via fetch/FormData), `list` TYPE column, `remove` both layouts, help text in `tools/create-module/create-module.mjs`; update `tools/create-module/README.md`
- [X] T044 [US5] Generate sample plugin module `sample3` with the CLI (`src/Modules/Sample3/Sample3.Plugin/`, `modules/sample3/`, `tests/SharedKernel.Tests/Modules/Sample3ModuleTests.cs`) and verify `package` output excludes shared assemblies

---

## Phase 8: User Story 6 - Add a new module type with minimal change (P3)

- [X] T045 [US6] Test registering a fake third type in both registries and running `ModuleLifecycleService.InstallAsync` + strategy activation without lifecycle changes in `tests/Platform.Application.Tests/ModuleHostingTests.cs` (SC-006)

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T046 [P] Update `docs/module-developer-guide.md` (module types, plugin authoring, packaging, Module Host, tenant shell design, zone matrix, CLI commands, troubleshooting)
- [X] T047 [P] Update `README.md` (layout, ports :5300, module types, package upload) and `SETUP.md` (Module Host config/env)
- [X] T048 Full verification: `dotnet build`, `dotnet test`, shell `tsc --noEmit` + `vite build`, CLI round trip, quickstart E2E smoke against running Platform + Gateway + Module Host where feasible
- [ ] T049 Final diff review (no secrets/debug/temp files; generated artifacts intentional)

---

## Dependencies & Execution Order

- Setup (T001–T003) → Foundational (T004–T014) → stories.
- US1 (T015–T019) depends on T004–T010. T019 (frontend) only needs the API contract → parallel with T015–T018.
- US2 depends on T009 (contract), T007 (extract), T011 (strategies), T010 (columns). Module Host tasks T020–T029 + T034–T035 are one workstream (`src/PluginHost/**`, `tests/PluginHost.Tests*/**`); Platform tasks T030–T033, T036 another; T037 after both.
- US3 depends on US2. US4 depends on T005/T016. US5 depends on T009 (template references contract) and the manifest contract; T044 after T037 and T043. US6 depends on T011.
- Polish last.

## Parallel Execution (subagent workstreams)

| Workstream | Tasks | Owned paths |
|------------|-------|-------------|
| Primary agent (backend core + integration) | T001, T004–T018, T030–T033, T036–T038, T040–T041, T044–T045, T048–T049 | `src/Shared/**`, `src/Platform/**`, `tests/SharedKernel.Tests/**`, `tests/Platform.Application.Tests/**`, AppHost, compose, sln |
| Module Host agent | T002–T003, T020–T029, T034–T035, T039 | `src/PluginHost/**`, `tests/PluginHost.Tests/**`, `tests/PluginHost.Tests.FixturePlugin/**` |
| Frontend agent | T019 | `apps/shell/**` |
| CLI agent | T042–T043 | `tools/create-module/**` |
| Docs (after converge) | T046–T047 | `docs/**`, `README.md`, `SETUP.md` |

## Implementation Strategy

MVP = Foundational + US1 (package registration) + US2 (per-tenant plugin install). Then US3 (side-by-side upgrade), US4 (zones), US5 (CLI), US6 (extensibility test), Polish.
