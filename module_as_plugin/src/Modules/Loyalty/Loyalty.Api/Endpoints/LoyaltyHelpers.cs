using Microsoft.Extensions.Caching.Memory;

namespace Loyalty.Api.Endpoints;

public static class LoyaltyHelpers
{
    // Tenant usability via Platform's cluster-internal status endpoint (cached
    // 30s). Fail closed: unknown → 404, disabled → 403, platform unreachable →
    // 503 (retryable). No fail-open.
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
            return Results.Json(new { error = new { code = "AUTHZ_UNAVAILABLE", message = $"Tenant status unavailable: {ex.Message}", retryable = true } }, statusCode: 503);
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
            ? Results.Json(new { error = new { code = "UNKNOWN_TENANT", message = "Unknown tenant." } }, statusCode: 404)
            : Results.Json(new { error = new { code = "TENANT_DISABLED", message = "Tenant is not active." } }, statusCode: 403);
    }
}
