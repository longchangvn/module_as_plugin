# Research: Module Hosting Types

All unknowns from the plan's Technical Context are resolved below. Format: Decision / Rationale / Alternatives considered.

## R1. Where do plugins run? ("same host as current running")

- **Decision**: A new, always-on **Module Host** service (`src/PluginHost/PluginHost.Api`, port 5300) loads plugins in-process and serves every tenant. The control plane (Platform), Auth and the Gateway never load plugin code.
- **Rationale**: Plugins are in-process, unsandboxed code. Loading them into Platform.Api would put catalog credentials, tenant connection secrets and Data Protection keys in the same process as third-party code, and a plugin crash/leak would take down registry, login or routing. A dedicated host still meets the requirement: one running host, many tenants, modules gated per tenant, no per-module deployment.
- **Alternatives**: Load into Platform.Api (rejected: blast radius, secrets exposure). Load into the Gateway (rejected: Ocelot process becomes stateful app host; every plugin fault is a full outage). One host per tenant (rejected: defeats "1 host, many tenants").

## R2. Extensible module types ("add a type later with minimal change")

- **Decision**: Strategy pattern keyed by `module.type`, in two layers:
  1. `SharedKernel.ModuleTypes` — a registry of `IModuleTypeRules` (pure manifest/package validation per type). `ManifestValidator` delegates type-specific checks to it.
  2. `Platform.Application.IModuleHostingStrategy` — per-type runtime behaviour: gateway target (base URL + upstreams), per-tenant activate/deactivate (enabled flag), migrate. `ModuleHostingRegistry` resolves strategies from DI (`IEnumerable<IModuleHostingStrategy>`).
  The lifecycle ports (`EfLifecyclePorts`) call the strategy instead of hard-coding "push artifactApiUrl to the gateway". `ModuleLifecycleService`, catalog endpoints and the Gateway are unchanged.
- **Adding a type** = one `IModuleTypeRules` + one `IModuleHostingStrategy` + two registrations. Proven by a test that registers a fake third type.
- **Alternatives**: `switch (type)` in lifecycle (rejected: every new type edits core flow). Plugin-ize types via MEF (rejected: unnecessary for two server-side types).

## R3. Manifest shape for types and connectivity

- **Decision** (backward compatible additions; `manifestVersion` stays `"1.0"`, new keys optional):
  ```yaml
  module:
    type: plugin            # remote (default) | plugin
  service:
    network: same-cluster   # remote only: same-cluster (default) | cross-cluster | public
    baseUrl: http://inventory:8080   # remote only, optional default artifact URL
  plugin:                   # required when type = plugin
    assembly: Inventory.Plugin.dll
    entryType: Inventory.Plugin.InventoryPlugin   # optional
    contract: "1.0"         # plugin contract version (default "1.0")
  ```
  Plugin `frontend.remoteUrl` may be empty: the platform derives it from the Module Host when the package carries `web/assets/remoteEntry.js`.
- **Rationale**: Unknown keys are already ignored by the parser, so old platforms/tests don't break; defaults keep every existing manifest valid (SC-007).

## R4. Package format and safety

- **Decision**: Zip with `manifest.yml` at the root; plugins add `plugin/` (the publish output: entry assembly + private dependencies + `.deps.json`) and optional `web/` (Vite `dist` contents). Inspection lives in `SharedKernel.ModulePackage` so the Platform (upload) and Module Host (extract) share one implementation:
  - reject absolute paths, drive letters, `..` segments, backslash tricks, entries outside allowed roots (`manifest.yml`, `README.md`, `plugin/**`, `web/**`);
  - limits: compressed ≤ 50 MB, uncompressed ≤ 200 MB, ≤ 2,000 entries (options-configurable);
  - SHA-256 of the exact uploaded bytes is the package fingerprint;
  - plugin: `plugin/{plugin.assembly}` must exist; frontend routes with empty `remoteUrl` require `web/assets/remoteEntry.js`.
- **Storage**: Platform writes `{ModulePackages:Root}/{moduleId}/{version}/package.zip` (default `./module-packages`) after validation, inside the same request that inserts the catalog row; file is deleted if the DB insert fails. Unique `(ModuleId, Version)` index guarantees one winner under concurrent uploads.
- **Alternatives**: NuGet `.nupkg` (rejected: tooling overhead, no UI assets convention). Upload directly to Module Host (rejected: host would become a registry; integrity anchor must be the catalog).

## R5. Loading plugins in-process with isolation

- **Decision**: One collectible `AssemblyLoadContext` per (module, version) (`PluginLoadContext`, `isCollectible: true`) with `AssemblyDependencyResolver` on the entry assembly. Resolution rule: assemblies the host provides (anything already in the default context or on the trusted platform assembly list: framework, `Microsoft.AspNetCore.*`, `Microsoft.Extensions.*`, `SharedKernel`, `ModulePlatform.Plugin.Abstractions`) return `null` → default context (type identity preserved for `IModulePlugin`, `ModuleGuards`, DI, routing). Everything else resolves from the plugin folder → private, per-version.
- **Rationale**: Gives FR-012/FR-013: shared contracts, isolated private dependencies, side-by-side versions, unloadable.
- **Alternatives**: Load into default ALC (rejected: no unload, dependency conflicts). Separate process per plugin (rejected: that is the remote type).

## R6. Plugin contract

- **Decision**: New class library `src/Shared/ModulePlatform.Plugin.Abstractions` (net8.0, `FrameworkReference Microsoft.AspNetCore.App`):
  ```csharp
  public interface IModulePlugin {
      void ConfigureServices(IServiceCollection services, PluginContext context);
      void MapEndpoints(IEndpointRouteBuilder endpoints, PluginContext context);
      Task MigrateAsync(PluginMigration migration, IServiceProvider services, CancellationToken ct) => Task.CompletedTask;
  }
  public sealed record PluginContext(string ModuleId, string Version, IConfiguration Configuration, string ContentRoot);
  public sealed record PluginMigration(string FromVersion, string ToVersion);
  public static class PluginContract { public const string Version = "1.0"; }
  ```
  Plugin projects reference Abstractions and SharedKernel with `Private="false" ExcludeAssets="runtime"` so the host's copies are used and the package never carries them.

## R7. Dynamic endpoints without downtime

- **Decision**: The Module Host registers one custom `PluginEndpointDataSource : EndpointDataSource` in its routing. Each runtime builds its endpoints by calling `plugin.MapEndpoints` on a private `IEndpointRouteBuilder` whose `ServiceProvider` is the runtime's own container, inside a `MapGroup("/plugins/{moduleId}")` carrying `PluginEndpointMetadata(runtime)`. Swapping the endpoint list fires the data source's change token; ASP.NET Core rebuilds the matcher atomically — no restart, in-flight requests unaffected.
- **Per-tenant version selection**: a `PluginTenantMatcherPolicy : MatcherPolicy, IEndpointSelectorPolicy` invalidates candidates whose runtime is not the one the tenant shell resolved (stored in `HttpContext.Items` by the shell middleware that runs before routing). Two versions of a module therefore coexist without ambiguous matches.
- **Plugin DI**: a middleware after `UseAuthorization` swaps `HttpContext.RequestServices` to a scope of the matched runtime's container, so minimal-API parameter binding resolves plugin services. The runtime container is built from: shared host singletons (`IConfiguration`, `ILoggerFactory`/`ILogger<>`, `IHttpClientFactory` with the host's named `platform`/`auth` clients, `IHostEnvironment`), runtime-private `IMemoryCache`, routing/options/JSON basics, then `plugin.ConfigureServices`.
- **Alternatives**: Build a full nested `WebApplication` per plugin (rejected: heavy, duplicate auth stack). Restart host on change (rejected: downtime). YARP per plugin (rejected: needs separate processes).

## R8. Tenant shells (".NET shell per tenant")

- **Decision**: `TenantShellRegistry` holds an immutable snapshot: `tenants[tenantId][moduleId] = { version, enabled }` + `runtimes[(moduleId, version)]`. A tenant shell = the tenant's installed modules resolved to shared runtimes, layered over host-wide shared services (auth, http clients, logging, config). Writes are serialized (single writer, `SemaphoreSlim`), build/load happens **before** the snapshot swap, reads are lock-free (`volatile` reference). Unreferenced runtimes are drained (in-flight counter reaches 0, max `PluginHost:DrainTimeoutSeconds`, default 30) then disposed and their ALC unloaded.
- **Isolation guarantees**: tenant A's install only adds/changes A's entry; other tenants' entries and runtimes are untouched. A load failure aborts before the swap. Different tenants can map the same module to different runtimes (side-by-side upgrade, US3).
- **Why shared runtimes, not per-tenant containers**: Orchard Core-style per-tenant containers give stronger state isolation but cost memory O(tenants × plugins) and N× warm-up. Shared runtime + tenant-scoped request context keeps memory O(plugin versions). Documented trade-off; per-tenant containers are listed as a future strategy option.

## R9. Platform ↔ Module Host protocol

- **Decision**: Mirrors the existing Platform ↔ Gateway protocol (push + reconcile, `X-Internal-Key`):
  - Platform → Host `PUT /internal/plugins/{moduleId}/tenants/{tenantId}` `{version, enabled, packageSha256}` → 200 only after the runtime is loaded and the shell swapped; `DELETE` same path (idempotent); `POST /internal/plugins/{moduleId}/versions/{version}/migrate`.
  - Host → Platform `GET /internal/modules/{id}/versions/{version}/package` (stream) and `GET /internal/plugin-host/states` (reconcile snapshot) on start-up and every 5 min.
- **Ordering (FR-018)**: install/enable/upgrade = activate in host → push gateway route; disable = gateway (enabled=false) → host disabled; uninstall = gateway remove → host deactivate. Gateway routes for plugin modules target the host base URL with downstream prefixed `/plugins/{moduleId}`; the gateway code is unchanged.

## R10. Remote module communication (same cluster / cross cluster / public)

| Zone | Gateway → module | Module → platform (tenant status, permission resolve) | Rule enforced |
|------|------------------|-----------------------------------------------------|---------------|
| same-cluster (default) | cluster DNS, http allowed | cluster-internal endpoints (`/internal/...`, Auth resolve) | none extra |
| cross-cluster | https only (outside Development); private link / mTLS recommended | via private link to internal endpoints, or gateway-routed APIs | https `baseUrl`/`artifactApiUrl` |
| public | https only (outside Development) | must not use cluster-internal endpoints; validate the user JWT forwarded by the gateway | https `baseUrl`/`artifactApiUrl` |

- **Decision**: Enforce transport at registration (FR-025), carry the zone in the manifest, document trust recommendations. User JWTs (HS256 shared secret today) remain the end-user credential forwarded by the gateway; moving to asymmetric keys/JWKS for public modules is a recommended follow-up.

## R11. Frontend for plugins

- **Decision**: Module Host serves `web/` at `/plugin-assets/{moduleId}/{version}/{**path}` (anonymous, CORS enabled, immutable caching). The bootstrap endpoint returns `{PluginHost:PublicBaseUrl}/plugin-assets/{id}/{version}/assets/remoteEntry.js` when a plugin manifest has an empty `remoteUrl`. Tenant Admin's runtime remote loader needs no change.

## R12. EF migration

- **Decision**: Add nullable/defaulted columns to `ModuleVersions`: `ModuleType` (text, default `'remote'`), `PackageSha256` (text, null), `PackagePath` (text, null). Additive migration, safe on existing rows (default fills `remote`), no index changes. Generated with `dotnet ef migrations add ModuleHostingTypes`.

## R13. CLI

- **Decision**: `create-module new <name> --type remote|plugin` (default remote). Plugin template: `src/Modules/<Pascal>/<Pascal>.Plugin/` class library + `modules/<name>/manifest.yml` (`type: plugin`, empty `remoteUrl`) + `web/` (served by the host) + parity test; wiring: solution + test project only (no AppHost process, no compose service). New `package <name>` (dotnet publish plugin → `plugin/`, npm build → `web/`, zip via system `zip`/`tar`), `publish <name> --package [file]` (multipart upload; auto for plugins), `list` shows `TYPE`.
