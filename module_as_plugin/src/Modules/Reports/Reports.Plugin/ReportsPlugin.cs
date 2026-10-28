// Reports plugin — scaffolded by tools/create-module. Loaded in-process by the
// Module Host (:5300); endpoints are served under /plugins/reports.
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModulePlatform.Plugin.Abstractions;
using Reports.Plugin.Endpoints;

namespace Reports.Plugin;

public sealed class ReportsPlugin : IModulePlugin
{
    public void ConfigureServices(IServiceCollection services, PluginContext context)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, PluginContext context) => ModuleEndpoints.Map(endpoints);
}
