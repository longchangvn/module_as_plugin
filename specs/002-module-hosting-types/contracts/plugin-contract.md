# Contract: Plugin programming model (ModulePlatform.Plugin.Abstractions 1.0)

```csharp
namespace ModulePlatform.Plugin.Abstractions;

public interface IModulePlugin
{
    // Register plugin services. The container already contains shared host services:
    // IConfiguration, ILoggerFactory/ILogger<T>, IHttpClientFactory (named "platform", "auth"),
    // IHostEnvironment, IMemoryCache (private to this plugin version), options, routing basics.
    void ConfigureServices(IServiceCollection services, PluginContext context);

    // Map endpoints relative to the plugin root (/plugins/{moduleId}). Paths must match the
    // manifest api.routes downstream templates (e.g. /api/v1/data).
    void MapEndpoints(IEndpointRouteBuilder endpoints, PluginContext context);

    // Optional upgrade hook (called with the NEW version loaded).
    Task MigrateAsync(PluginMigration migration, IServiceProvider services, CancellationToken ct) => Task.CompletedTask;
}

public sealed record PluginContext(string ModuleId, string Version, IConfiguration Configuration, string ContentRoot);
public sealed record PluginMigration(string FromVersion, string ToVersion);
public static class PluginContract { public const string Version = "1.0"; }
```

Rules for plugin authors:

- Target `net8.0`; reference `ModulePlatform.Plugin.Abstractions` and `SharedKernel` with `Private="false" ExcludeAssets="runtime"` (host provides them).
- Exactly one public non-abstract `IModulePlugin` implementation with a public parameterless constructor (or set `plugin.entryType`).
- Treat the tenant as request data (`ModuleGuards.TenantId(ctx)`); never keep cross-tenant mutable state in singletons.
- Use `.RequireAuthorization()` + `ModuleGuards.RequireTenantAsync` / `RequirePermissionAsync` exactly like remote modules.
- No static mutable state that survives unload; dispose resources via DI.
