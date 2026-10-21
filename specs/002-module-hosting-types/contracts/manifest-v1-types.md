# Contract: manifest.yml additions (module types)

`manifestVersion` stays `"1.0"`; all additions are optional with backward-compatible defaults.

## Remote module (standalone host)

```yaml
module:
  name: inventory
  type: remote                 # optional, default remote
  # ...
service:
  healthCheck: /healthz
  readinessCheck: /readyz
  network: cross-cluster       # same-cluster (default) | cross-cluster | public
  baseUrl: https://inventory.eu.example.com   # optional default artifactApiUrl
frontend:
  remoteUrl: https://inventory.eu.example.com/assets/remoteEntry.js   # required for remote
```

## Plugin module (in-process, Module Host)

```yaml
module:
  name: sample3
  type: plugin
  # ...
plugin:
  assembly: Sample3.Plugin.dll                    # required, file under plugin/ in the package
  entryType: Sample3.Plugin.Sample3Plugin         # optional
  contract: "1.0"                                 # optional, default "1.0"
frontend:
  remoteUrl: ""                                   # empty → served by the Module Host from web/
  scope: sample3_remote
  # routes/menus as usual
api:
  routes:
    - upstream: /api/v1/sample3/{rest}
      downstream: /api/v1/{rest}                  # relative to the plugin's own endpoint root
```

## Validation (ManifestValidator + type rules)

| Field | Rule | Error message |
|-------|------|---------------|
| `module.type` | registered type | `Unknown module type '{t}'. Supported: plugin, remote.` |
| `service.network` (remote) | one of `same-cluster`, `cross-cluster`, `public` | `Must be same-cluster, cross-cluster or public.` |
| `service.baseUrl` (remote) | absolute http(s); https if network ≠ same-cluster and not Development | `Must be an absolute https URL for {network} modules (http allowed in Development only).` |
| `frontend.remoteUrl` (remote) | existing rule (absolute https; http in Development) | unchanged |
| `frontend.remoteUrl` (plugin) | empty, or existing absolute rule | unchanged when set |
| `plugin.assembly` (plugin) | required, `^[A-Za-z0-9][A-Za-z0-9._-]*\.dll$` | `Required for plugin modules: file name of the entry assembly (*.dll).` |
| `plugin.contract` (plugin) | `1.0` | `Unsupported plugin contract '{c}'. Supported: 1.0.` |
| `service.baseUrl` (plugin) | must be empty | `Plugin modules are hosted by the platform; remove service.baseUrl.` |
| All existing rules | unchanged | unchanged |
