using PluginHost.Api.Routing;

namespace PluginHost.Api.Middleware;

// Runs AFTER UseAuthorization: when the matched endpoint belongs to a plugin, RequestServices
// becomes a scope of that runtime's container (minimal-API binding resolves plugin services),
// falling back to the host request scope for host-only services (e.g. IAuthenticationService).
public sealed class PluginServicesMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var meta = ctx.GetEndpoint()?.Metadata.GetMetadata<PluginEndpointMetadata>();
        if (meta is null)
        {
            await next(ctx);
            return;
        }
        var hostServices = ctx.RequestServices;
        await using var scope = meta.Runtime.Services.CreateAsyncScope();
        ctx.RequestServices = new FallbackServiceProvider(scope.ServiceProvider, hostServices);
        try
        {
            await next(ctx);
        }
        finally
        {
            ctx.RequestServices = hostServices;
        }
    }

    private sealed class FallbackServiceProvider(IServiceProvider primary, IServiceProvider fallback)
        : IServiceProvider, IKeyedServiceProvider
    {
        public object? GetService(Type serviceType) =>
            primary.GetService(serviceType) ?? fallback.GetService(serviceType);

        public object? GetKeyedService(Type serviceType, object? serviceKey) =>
            (primary as IKeyedServiceProvider)?.GetKeyedService(serviceType, serviceKey)
            ?? (fallback as IKeyedServiceProvider)?.GetKeyedService(serviceType, serviceKey);

        public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
            GetKeyedService(serviceType, serviceKey)
            ?? throw new InvalidOperationException($"No keyed service for type '{serviceType}' and key '{serviceKey}' has been registered.");
    }
}
