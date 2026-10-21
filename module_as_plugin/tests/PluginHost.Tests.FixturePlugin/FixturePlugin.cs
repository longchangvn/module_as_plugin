using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModulePlatform.Plugin.Abstractions;
using SharedKernel;

namespace PluginHost.Tests.FixturePlugin;

// Minimal plugin used by PluginHost.Tests. Reports ModuleId/Version from the PluginContext so the
// same compiled dll can be loaded as several module versions (side-by-side tests).
public sealed class FixturePlugin : IModulePlugin
{
    public void ConfigureServices(IServiceCollection services, PluginContext context) =>
        services.AddSingleton(new FixtureGreeter($"{context.ModuleId}@{context.Version}"));

    public void MapEndpoints(IEndpointRouteBuilder endpoints, PluginContext context)
    {
        // [FromServices]: resolved from the plugin container via the host's PluginServicesMiddleware.
        endpoints.MapGet("/api/v1/data", (HttpContext http, [FromServices] FixtureGreeter greeter) => Results.Json(new
        {
            data = new
            {
                module = context.ModuleId,
                version = context.Version,
                tenant = ModuleGuards.TenantId(http),
                greeting = greeter.Greeting,
            },
        })).RequireAuthorization();
    }

    public Task MigrateAsync(PluginMigration migration, IServiceProvider services, CancellationToken ct)
    {
        if (migration.FromVersion == "fail") throw new InvalidOperationException("fixture migrate failure");
        services.GetRequiredService<FixtureGreeter>().LastMigration = $"{migration.FromVersion}->{migration.ToVersion}";
        return Task.CompletedTask;
    }
}

public sealed class FixtureGreeter(string identity)
{
    public string Greeting => $"hello from {identity}";
    public string? LastMigration { get; set; }
}
