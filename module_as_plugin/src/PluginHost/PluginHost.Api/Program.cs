// Module Host :5300 — in-process plugin host for `module.type: plugin` (spec 002, research R5–R9, R11).
// Each (module, version) runs in its own collectible AssemblyLoadContext with its own DI container;
// per-tenant shells map a tenant's installed modules to those shared runtimes and are swapped
// atomically (push from the Platform over /internal/plugins/**, reconciled every few minutes).
// Public traffic arrives from the Gateway at /plugins/{moduleId}/**; plugin web assets are served
// at /plugin-assets/{moduleId}/{version}/**. Composition lives in PluginHostApp.
using PluginHost.Api;

PluginHostApp.Build(args).Run();

public partial class Program;
