using Auth.Api.Infrastructure;
using Auth.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SharedKernel;

namespace Auth.Api.Endpoints;

public static class LoginEndpoints
{
    public static void Map(WebApplication app)
    {
    // Tenant login — mints tenant JWT (tenantId + userId; tenant_admin when the user holds an *Admin* role).
    // The `tenantId` field accepts the opaque id (tnt_…), the slug, or the
    // tenant name (case-insensitive) — non-id input is resolved via Platform's
    // cluster-internal resolve endpoint before touching the tenant store.
    app.MapPost("/api/v1/auth/login", async (TenantDbContext legacy, TenantDbProvider provider, IConfiguration cfg, IHttpClientFactory http, IMemoryCache cache, LoginRequest req, CancellationToken ct) =>
    {
        var resolved = await ResolveTenantIdAsync(http, cfg, req.TenantId);
        if (resolved.Unavailable)
            return ApiErrors.Problem(ErrorCodes.AuthzUnavailable, "Tenant status unavailable.", 503, retryable: true);
        if (resolved.Id is null)
            return ApiErrors.Problem(ErrorCodes.UnknownTenant, "Unknown tenant.", 404);
        var tenantId = resolved.Id;
        // Per-tenant store first: first login migrates + reports Ready (lazy
        // provisioning), so the usability check below sees the flipped state.
        await using var db = await provider.ForTenantAsync(tenantId, legacy);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == req.Email.ToLower(), ct);
        if (user is null || !user.IsActive)
            return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid credentials.", 401);
        // BR-R4: disabled/unknown tenants cannot log in (fail closed).
        var usability = await ApiHelpers.TenantUsabilityAsync(http, cache, cfg, tenantId);
        if (usability is not null) return usability;
        // NOTE: password verification is stubbed in v1 (seed users have no hash); production wires Argon2 here.
        var roleIds = await db.UserRoles.Where(ur => ur.UserId == user.Id).Select(ur => ur.RoleId).ToListAsync(ct);
        var roleNames = await db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Name).ToListAsync(ct);
        var isAdmin = roleNames.Any(n => n.Contains("Admin", StringComparison.OrdinalIgnoreCase));
        var token = JwtMint.Mint(JwtMint.Secret(cfg), new Dictionary<string, string>
        {
            ["sub"] = user.Id, ["email"] = user.Email,
            ["tenantId"] = tenantId, ["userId"] = user.Id,
            ["tenant_admin"] = isAdmin ? "true" : "false",
        });
        return Results.Json(new { data = new { accessToken = token, userId = user.Id, tenantAdmin = isAdmin, tenantId } });
    });
    }

    // Resolves opaque id, slug, or name (case-insensitive) to the tenant id.
    // Id-shaped input (tnt_…) skips the lookup; anything else goes through
    // Platform's cluster-internal resolve endpoint. Fail closed: unknown →
    // null id, Platform unreachable → Unavailable (caller returns 503).
    private static async Task<(string? Id, bool Unavailable)> ResolveTenantIdAsync(
        IHttpClientFactory http, IConfiguration cfg, string? input)
    {
        var value = input?.Trim() ?? "";
        if (value.Length == 0) return (null, false);
        if (value.StartsWith("tnt_", StringComparison.OrdinalIgnoreCase)) return (value, false);
        try
        {
            var baseUrl = (cfg["Platform:BaseUrl"] ?? "http://localhost:5121").TrimEnd('/');
            using var client = http.CreateClient("platform");
            using var res = await client.GetAsync(
                $"{baseUrl}/internal/tenants:resolve?slug={Uri.EscapeDataString(value)}&name={Uri.EscapeDataString(value)}");
            if (!res.IsSuccessStatusCode) return (null, false);
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
            return (doc.RootElement.GetProperty("data").GetProperty("id").GetString(), false);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
        {
            return (null, true);
        }
    }
}
