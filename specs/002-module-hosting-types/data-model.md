# Data Model: Module Hosting Types

## Persistent (catalog, PostgreSQL)

### ModuleVersion (existing table `ModuleVersions`, extended)

| Field | Type | Rules |
|-------|------|-------|
| Id, ModuleId, Version, ManifestYaml, ManifestHash, ArtifactApiUrl, ArtifactRemoteUrl, Status, PublishedBy, PublishedAt | existing | unchanged; unique (ModuleId, Version) guards concurrent uploads |
| **ModuleType** | text, NOT NULL, default `'remote'` | equals manifest `module.type` (lower-case); one of the registered types |
| **PackageSha256** | text, NULL | lower-case hex SHA-256 of the uploaded zip bytes; required when ModuleType = `plugin`; null for JSON-published remote versions |
| **PackagePath** | text, NULL | path relative to `ModulePackages:Root`: `{moduleId}/{version}/{sha256}-{nonce}.zip` (unique per upload so a losing concurrent upload only deletes its own file); never returned by public APIs |

Immutability: a version row and its package file are never modified after insert (re-publish of the same version → `400`).

### TenantModuleInstall (unchanged)

Install state machine unchanged (`Installing → Installed/Disabled/Failed`, delete on uninstall). The Module Host shell entry for (tenant, module) exists iff an install row exists with a plugin version; `enabled = Enabled && State ∈ {Installed, Installing}` (same rule as the gateway snapshot).

## Manifest (YAML contract, additions)

| Key | Type | Default | Applies to | Validation |
|-----|------|---------|------------|------------|
| `module.type` | string | `remote` | all | must be a registered type (`remote`, `plugin`) |
| `service.network` | string | `same-cluster` | remote | `same-cluster` \| `cross-cluster` \| `public` |
| `service.baseUrl` | string | — | remote | absolute http(s); https required for `cross-cluster`/`public` outside Development; plugin: must be empty |
| `plugin.assembly` | string | — | plugin (required) | `^[A-Za-z0-9][A-Za-z0-9._-]*\.dll$` |
| `plugin.entryType` | string | — | plugin | optional full type name; if absent the single `IModulePlugin` implementation is used |
| `plugin.contract` | string | `1.0` | plugin | must equal a host-supported contract (`1.0`) |
| `frontend.remoteUrl` | string | — | remote: required absolute (https outside dev); plugin: optional (empty → derived from host) | existing rule for remote |

## Package (zip)

```text
package.zip
├── manifest.yml            # required, root
├── README.md               # optional
├── plugin/                 # plugin only: publish output (entry dll named by plugin.assembly, private deps, *.deps.json)
└── web/                    # optional: Vite dist (assets/remoteEntry.js ...)
```

Rules: entries only under the roots above; no absolute paths, drive letters or `..`; compressed ≤ `MaxPackageBytes` (50 MB), sum of uncompressed sizes ≤ `MaxUncompressedBytes` (200 MB), entries ≤ `MaxEntries` (2,000). Plugin: `plugin/{assembly}` must exist. Plugin with non-empty `frontend.routes` and empty `remoteUrl`: `web/assets/remoteEntry.js` must exist.

`ModulePackageInfo` (in-memory result): `ManifestYaml`, `Manifest`, `Sha256`, `HasPluginAssembly`, `HasWeb`, `HasRemoteEntry`, `EntryCount`, `UncompressedBytes`.

## Runtime (Module Host, in-memory)

### PluginRuntime
| Field | Notes |
|-------|-------|
| ModuleId, Version, PackageSha256 | identity; key = `moduleId@version` (case-insensitive) |
| LoadContext | collectible `PluginLoadContext`, unloaded on dispose |
| Plugin | `IModulePlugin` instance |
| Services | runtime `ServiceProvider` (shared host singletons + plugin services) |
| Endpoints | endpoints under `/plugins/{moduleId}` tagged with `PluginEndpointMetadata(runtime)` |
| ContentRoot | extracted folder `{CacheRoot}/{moduleId}/{version}` (`plugin/`, `web/`) |
| InFlight | request counter for drain |
| State | `Active` → `Draining` → `Disposed` |

### TenantShellSnapshot (immutable, swapped atomically)
- `Tenants: tenantId → (moduleId → ShellEntry{ Version, Enabled })`
- `Runtimes: moduleId@version → PluginRuntime` (only versions referenced by ≥ 1 tenant, plus runtimes pinned for an in-progress migrate)
- Decide(tenantId, moduleId) → `NotInstalled` | `Disabled` | `Enabled(runtime)`

State transitions per (tenant, module): absent → `Apply(version, enabled)` (load runtime if missing → swap) → present; present → `Remove` (swap → release unreferenced runtimes after drain).

## Strategy registry (Platform.Application)

- `IModuleHostingStrategy { Type; GatewayTarget Target(ManifestFile, ModuleVersionRef); ActivateAsync; DeactivateAsync; MigrateAsync }`
- `GatewayTarget(string? BaseUrl, IReadOnlyList<GatewayUpstream> Upstreams)`
- `ModuleVersionRef(ModuleId, Version, ModuleType, ArtifactApiUrl, PackageSha256)`
