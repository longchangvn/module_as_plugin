using System.Security.Cryptography;
using System.Text;

namespace Gateway.Api.Endpoints;

public static class GatewayHelpers
{
    // Service-to-service auth for /api/v1/gateway/internal/** (R-GW2): shared
    // X-Internal-Key (InternalApi:Key, fail-closed via RequireSecret), fixed-time compare.
    public static bool IsInternalCaller(HttpContext ctx, IConfiguration cfg)
    {
        var expected = cfg["InternalApi:Key"];
        var presented = ctx.Request.Headers["X-Internal-Key"].FirstOrDefault() ?? "";
        return !string.IsNullOrWhiteSpace(expected) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(presented));
    }

    public static bool IsLocalPath(PathString path) =>
        path.StartsWithSegments("/api/v1/gateway") ||
        path.StartsWithSegments("/swagger") ||
        string.Equals(path.Value, "/healthz", StringComparison.OrdinalIgnoreCase);

    // Password-accepting endpoints throttled per client IP at the edge (FR-A5).
    private static readonly string[] CredentialPaths =
    [
        "/api/v1/platform/auth/login", "/api/v1/platform/auth/change-password",
        "/api/v1/auth/login", "/api/v1/auth/change-password",
    ];

    public static bool IsCredentialRequest(HttpRequest req) =>
        HttpMethods.IsPost(req.Method) &&
        CredentialPaths.Any(p => string.Equals(req.Path.Value?.TrimEnd('/'), p, StringComparison.OrdinalIgnoreCase));

    public static bool UseDevKey(HttpContext ctx, IConfiguration cfg, bool isDevelopment) =>
        isDevelopment &&
        ctx.Request.Headers.TryGetValue("X-API-Key", out var key) &&
        key == (cfg["Gateway:DevApiKey"] ?? "dev-key");

    public static bool IsWriter(HttpContext ctx, IConfiguration cfg, bool devKey)
    {
        if (devKey) return true;
        return ctx.User.Identity?.IsAuthenticated == true &&
            ctx.User.FindFirst("platform_role")?.Value == "Admin";
    }

    // Per-tenant module gate before proxying (SR-4, G-BR5, R-GW1). Tenant comes
    // from the validated JWT `tenantId` claim; X-Tenant-Id may only repeat it (R9).
    // Returns null to proxy, else (status, code, message). CORS preflight carries
    // no token, so OPTIONS only needs the module to exist for some tenant.
    public static (int Status, string Code, string Message)? ModuleGuard(HttpContext ctx, Services.ModuleTenantState state, string moduleId)
    {
        if (HttpMethods.IsOptions(ctx.Request.Method))
            return state.HasModule(moduleId) ? null : (404, "UNKNOWN_MODULE_ROUTE", $"No route for module '{moduleId}'.");
        if (ctx.User.Identity?.IsAuthenticated != true)
            return (401, "UNAUTHORIZED", "Authentication required.");
        var tenantId = ctx.User.FindFirst("tenantId")?.Value;
        if (string.IsNullOrWhiteSpace(tenantId))
            return (403, "FORBIDDEN_TENANT", "Tenant context required.");
        var header = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header) && !string.Equals(header.Trim(), tenantId, StringComparison.OrdinalIgnoreCase))
            return (403, "FORBIDDEN_TENANT", "Tenant header does not match request tenant.");
        return state.Decide(moduleId, tenantId) switch
        {
            Models.ModuleState.NotInstalled => (404, "UNKNOWN_MODULE_ROUTE", $"No route for module '{moduleId}'."),
            Models.ModuleState.Disabled => (503, "MODULE_DISABLED", $"Module '{moduleId}' is disabled."),
            _ => null,
        };
    }

    public static string? ModuleFromPath(string path)
    {
        // /api/v1/{moduleId}/{rest} — platform + auth (/auth/* and /admin/*) are always installed+enabled (A5).
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 3 && segments[0] == "api" && segments[1] == "v1")
        {
            var candidate = segments[2];
            if (candidate is "platform" or "auth" or "admin" or "tenant" or "gateway") return null;
            return candidate;
        }
        return null;
    }
}
