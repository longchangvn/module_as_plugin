using Auth.Api.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using SharedKernel;

namespace Auth.Api.Endpoints;

public static class ApiHelpers
{
    // Bump the tenant's permission-version so cached resolve results for this
    // tenant become unreachable (A-BR5 invalidation on admin grant changes).
    // Tenant comes from the JWT claim (tenant login) or X-Tenant-Id header
    // (bootstrap/platform path) — same resolution the store provider uses.
    public static void BumpPermissions(HttpContext ctx, PermissionVersionCache versions)
    {
        var tenantId = ctx.User.FindFirst("tenantId")?.Value
            ?? ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(tenantId))
            versions.Bump(tenantId);
    }

    // Tenant usability via Platform's cluster-internal status endpoint.
    // Cached 30s. Fail closed: unknown → 404, disabled → 403, platform
    // unreachable → 503 (retryable). No fail-open.
    public static async Task<IResult?> TenantUsabilityAsync(
        IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg, string tenantId)
    {
        var key = $"tenant-usable:{tenantId}";
        if (cache.TryGetValue<string>(key, out var cached) && cached is not null)
            return cached == "usable" ? null : FailFor(cached);
        string state;
        try
        {
            var baseUrl = (cfg["Platform:BaseUrl"] ?? "http://localhost:5121").TrimEnd('/');
            using var client = http.CreateClient("platform");
            using var res = await client.GetAsync($"{baseUrl}/internal/tenants/{Uri.EscapeDataString(tenantId)}/status");
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                return FailFor(Store("unknown"));
            res.EnsureSuccessStatusCode();
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
            state = doc.RootElement.GetProperty("data").GetProperty("status").GetString() ?? "";
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
        {
            return ApiErrors.Problem(ErrorCodes.AuthzUnavailable, $"Tenant status unavailable: {ex.Message}", 503, retryable: true);
        }
        var verdict = string.Equals(state, "Active", StringComparison.OrdinalIgnoreCase) ? "usable" : "disabled";
        cache.Set(key, verdict, TimeSpan.FromSeconds(30));
        return verdict == "usable" ? null : FailFor(verdict);

        string Store(string v)
        {
            cache.Set(key, v, TimeSpan.FromSeconds(30));
            return v;
        }
        static IResult FailFor(string v) => v == "unknown"
            ? ApiErrors.Problem(ErrorCodes.UnknownTenant, "Unknown tenant.", 404)
            : ApiErrors.Problem(ErrorCodes.TenantDisabled, "Tenant is not active.", 403);
    }
}
