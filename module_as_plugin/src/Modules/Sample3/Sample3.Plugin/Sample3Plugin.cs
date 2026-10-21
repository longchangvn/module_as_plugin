// Sample Module 3 plugin — scaffolded by tools/create-module. Loaded in-process by the
// Module Host (:5300); endpoints are served under /plugins/sample3.
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModulePlatform.Plugin.Abstractions;
using Sample3.Plugin.Endpoints;

namespace Sample3.Plugin;

public sealed class Sample3Plugin : IModulePlugin
{
    public void ConfigureServices(IServiceCollection services, PluginContext context)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, PluginContext context) => ModuleEndpoints.Map(endpoints);
}
