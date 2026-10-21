using PluginHost.Api.Runtime;
using PluginHost.Api.Shells;
using SharedKernel;

namespace PluginHost.Api.Middleware;

// Runs BEFORE routing for /plugins/{moduleId}/**: authenticates the tenant (JWT tenantId claim,
// optional X-Tenant-Id must match), resolves the tenant shell and pins the tenant's runtime for
// the matcher policy. The runtime's in-flight counter covers the whole request (drain on unload).
public sealed class TenantShellMiddleware(RequestDelegate next, TenantShellRegistry registry)
{
    private const string RuntimeItem = "PluginHost.Runtime";
    private const int MaxAdmissionAttempts = 5;

    public static PluginRuntime? PinnedRuntime(HttpContext ctx) =>
        ctx.Items.TryGetValue(RuntimeItem, out var value) ? value as PluginRuntime : null;

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!ctx.Request.Path.StartsWithSegments("/plugins", StringComparison.OrdinalIgnoreCase, out var rest))
        {
            await next(ctx);
            return;
        }
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            await Deny(ctx, 401, ErrorCodes.Unauthorized, "Authentication required.");
            return;
        }
        var tenantId = ModuleGuards.TenantId(ctx);
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            await Deny(ctx, 403, ErrorCodes.ForbiddenTenant, "Tenant context required.");
            return;
        }
        var header = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header) && !string.Equals(header.Trim(), tenantId, StringComparison.OrdinalIgnoreCase))
        {
            await Deny(ctx, 403, ErrorCodes.ForbiddenTenant, "Tenant header does not match request tenant.");
            return;
        }
        var moduleId = (rest.Value ?? "").TrimStart('/').Split('/', 2)[0];

        // Retry only when the resolved runtime started draining between Decide and TryEnter
        // (a swap just happened): the next Decide sees the new snapshot.
        for (var attempt = 0; attempt < MaxAdmissionAttempts; attempt++)
        {
            var decision = moduleId.Length == 0 ? new ShellDecision(ShellDecisionKind.NotInstalled) : registry.Decide(tenantId, moduleId);
            switch (decision.Kind)
            {
                case ShellDecisionKind.NotInstalled:
                    await Deny(ctx, 404, ErrorCodes.UnknownModuleRoute, $"Module '{moduleId}' is not installed for this tenant.");
                    return;
                case ShellDecisionKind.Disabled:
                    await Deny(ctx, 503, ErrorCodes.ModuleDisabled, $"Module '{moduleId}' is disabled for this tenant.");
                    return;
            }
            var runtime = decision.Runtime!;
            if (!runtime.TryEnter()) continue;
            try
            {
                ctx.Items[RuntimeItem] = runtime;
                await next(ctx);
            }
            finally
            {
                runtime.Exit();
            }
            return;
        }
        await Deny(ctx, 503, ErrorCodes.ModuleNotReady, $"Module '{moduleId}' is being reloaded; retry.");
    }

    // Same envelope as the gateway's module guard.
    private static Task Deny(HttpContext ctx, int status, string code, string message)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { error = new { code, message } });
    }
}
