// Plugin programming model (contract 1.0). The Module Host (PluginHost.Api) provides this
// assembly, SharedKernel and the ASP.NET Core framework; plugins reference them with
// Private="false" ExcludeAssets="runtime" so type identity is shared across load contexts.
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ModulePlatform.Plugin.Abstractions;

public interface IModulePlugin
{
    // Register plugin services. The container already holds the shared host services:
    // IConfiguration, ILoggerFactory/ILogger<T>, IHttpClientFactory (named "platform", "auth"),
    // IHostEnvironment, a private IMemoryCache, options and routing basics.
    void ConfigureServices(IServiceCollection services, PluginContext context);

    // Map endpoints relative to the plugin root (/plugins/{moduleId}); paths match the
    // manifest api.routes downstream templates (e.g. /api/v1/data).
    void MapEndpoints(IEndpointRouteBuilder endpoints, PluginContext context);

    // Optional upgrade hook, invoked with the NEW version loaded.
    Task MigrateAsync(PluginMigration migration, IServiceProvider services, CancellationToken ct) => Task.CompletedTask;
}

public sealed record PluginContext(string ModuleId, string Version, IConfiguration Configuration, string ContentRoot);

public sealed record PluginMigration(string FromVersion, string ToVersion);

public static class PluginContract
{
    public const string Version = "1.0";
}
