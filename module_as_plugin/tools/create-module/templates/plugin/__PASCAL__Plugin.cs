// __DISPLAY_NAME__ plugin — scaffolded by tools/create-module. Loaded in-process by the
// Module Host (:5300); endpoints are served under /plugins/__MODULE_NAME__.
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModulePlatform.Plugin.Abstractions;
using __PASCAL__.Plugin.Endpoints;

namespace __PASCAL__.Plugin;

public sealed class __PASCAL__Plugin : IModulePlugin
{
    public void ConfigureServices(IServiceCollection services, PluginContext context)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, PluginContext context) => ModuleEndpoints.Map(endpoints);
}
