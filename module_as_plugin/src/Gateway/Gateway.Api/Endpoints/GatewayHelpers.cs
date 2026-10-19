namespace Gateway.Api.Endpoints;

public static class GatewayHelpers
{
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
