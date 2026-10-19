// Request guards for module APIs (template helper). All checks fail closed.
//   RequireTenantAsync     — tenant claim present, X-Tenant-Id matches (R9),
//                            tenant usable via Platform internal status (30s cache).
//   RequirePermissionAsync — effective permission via Auth resolve (A-BR5);
//                            no module-side cache, Auth's own cache is
//                            version-invalidated on every grant/revoke.
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace SharedKernel;

public static class ModuleGuards
{
    public static string? TenantId(HttpContext ctx) => ctx.User.FindFirst("tenantId")?.Value;

    public static string? UserId(HttpContext ctx) =>
        ctx.User.FindFirst("userId")?.Value
        ?? ctx.User.FindFirst("sub")?.Value
        ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public static bool HasPermission(IEnumerable<string>? permissions, string code) =>
        permissions?.Contains(code, StringComparer.Ordinal) == true;

    public static async Task<IResult?> RequireTenantAsync(HttpContext ctx, IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg)
    {
        var tenantId = TenantId(ctx);
        if (string.IsNullOrEmpty(tenantId))
            return ApiErrors.Problem(ErrorCodes.ForbiddenTenant, "Tenant context required.", 403);
        var header = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header) && !string.Equals(header.Trim(), tenantId, StringComparison.OrdinalIgnoreCase))
            return ApiErrors.Problem(ErrorCodes.ForbiddenTenant, "Tenant header does not match request tenant.", 403);

        var key = $"tenant-usable:{tenantId}";
        if (!cache.TryGetValue<string>(key, out var verdict) || verdict is null)
        {
            try
            {
                var baseUrl = (cfg["Platform:BaseUrl"] ?? "http://localhost:5121").TrimEnd('/');
                using var client = http.CreateClient("platform");
                using var res = await client.GetAsync($"{baseUrl}/internal/tenants/{Uri.EscapeDataString(tenantId)}/status");
                if (res.StatusCode == System.Net.HttpStatusCode.NotFound) verdict = "unknown";
                else
                {
                    res.EnsureSuccessStatusCode();
                    using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
                    var status = doc.RootElement.GetProperty("data").GetProperty("status").GetString();
                    verdict = string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase) ? "usable" : "disabled";
                }
                cache.Set(key, verdict, TimeSpan.FromSeconds(30));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return ApiErrors.Problem(ErrorCodes.AuthzUnavailable, "Tenant status unavailable.", 503, retryable: true);
            }
        }
        return verdict switch
        {
            "usable" => null,
            "unknown" => ApiErrors.Problem(ErrorCodes.UnknownTenant, "Unknown tenant.", 404),
            _ => ApiErrors.Problem(ErrorCodes.TenantDisabled, "Tenant is not active.", 403),
        };
    }

    public static async Task<IResult?> RequirePermissionAsync(HttpContext ctx, IHttpClientFactory http, IConfiguration cfg, string tenantId, string permission)
    {
        var userId = UserId(ctx);
        if (string.IsNullOrEmpty(userId))
            return ApiErrors.Problem(ErrorCodes.Forbidden, "User context required.", 403);
        ResolveResponse? body;
        try
        {
            var baseUrl = (cfg["Auth:BaseUrl"] ?? "http://localhost:5122").TrimEnd('/');
            using var client = http.CreateClient("auth");
            using var res = await client.GetAsync(
                $"{baseUrl}/api/v1/auth/permissions/resolve?tenantId={Uri.EscapeDataString(tenantId)}&userId={Uri.EscapeDataString(userId)}");
            if (!res.IsSuccessStatusCode)
                return ApiErrors.Problem(ErrorCodes.AuthzUnavailable, "Permission check unavailable.", 503, retryable: true);
            body = await res.Content.ReadFromJsonAsync<ResolveResponse>(JsonOptions);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return ApiErrors.Problem(ErrorCodes.AuthzUnavailable, "Permission check unavailable.", 503, retryable: true);
        }
        return HasPermission(body?.Data?.Permissions, permission)
            ? null
            : ApiErrors.Problem(ErrorCodes.Forbidden, $"Requires {permission}.", 403, new { requiredPermission = permission });
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record ResolveResponse(ResolveData? Data);
    private sealed record ResolveData(List<string>? Permissions);
}
