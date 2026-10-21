# Contract: create-module CLI additions

```
node tools/create-module/create-module.mjs <command> [options]

new <name> [--type remote|plugin]     default remote
    plugin: generates src/Modules/<Pascal>/<Pascal>.Plugin (class library implementing IModulePlugin),
            modules/<name>/manifest.yml (module.type: plugin, plugin.assembly, empty remoteUrl),
            modules/<name>/web (federation remote, built into the package), parity test.
            Wiring: ModulePlatform.sln + SharedKernel.Tests.csproj only (no AppHost process, no compose service).
            --api-port ignored for plugins.
package <name> [--out <file>] [--skip-build]
    remote: zip { manifest.yml, README.md? }
    plugin: dotnet publish <Pascal>.Plugin -c Release -> plugin/ (shared assemblies excluded),
            npm run build in web -> web/, zip { manifest.yml, plugin/**, web/** }
    default out: dist/modules/<name>-<version>.zip
publish <name> [--package [file]] [--api-url <url>] [auth options]
    plugin: always uploads a package (builds it first when no file is given)
    remote: --package uploads a package; otherwise existing JSON manifest publish
list        columns: NAME TYPE VERSION SCOPE API REMOTE   (plugin API column shows "host")
remove      handles both layouts (.Api or .Plugin project)
```
