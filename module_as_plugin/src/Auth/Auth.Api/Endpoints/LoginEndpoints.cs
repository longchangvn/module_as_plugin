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
    app.MapPost("/api/v1/auth/login", async (TenantDbContext legacy, TenantDbProvider provider, IConfiguration cfg, IHttpClientFactory http, IMemoryCache cache, LoginRequest req, CancellationToken ct) =>
    {
        // Per-tenant store first: first login migrates + reports Ready (lazy
        // provisioning), so the usability check below sees the flipped state.
        await using var db = await provider.ForTenantAsync(req.TenantId, legacy);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == req.Email.ToLower(), ct);
        if (user is null || !user.IsActive)
            return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid credentials.", 401);
        // BR-R4: disabled/unknown tenants cannot log in (fail closed).
        var usability = await ApiHelpers.TenantUsabilityAsync(http, cache, cfg, req.TenantId);
        if (usability is not null) return usability;
        // NOTE: password verification is stubbed in v1 (seed users have no hash); production wires Argon2 here.
        var roleIds = await db.UserRoles.Where(ur => ur.UserId == user.Id).Select(ur => ur.RoleId).ToListAsync(ct);
        var roleNames = await db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Name).ToListAsync(ct);
        var isAdmin = roleNames.Any(n => n.Contains("Admin", StringComparison.OrdinalIgnoreCase));
        var token = JwtMint.Mint(JwtMint.Secret(cfg), new Dictionary<string, string>
        {
            ["sub"] = user.Id, ["email"] = user.Email,
            ["tenantId"] = req.TenantId, ["userId"] = user.Id,
            ["tenant_admin"] = isAdmin ? "true" : "false",
        });
        return Results.Json(new { data = new { accessToken = token, userId = user.Id, tenantAdmin = isAdmin } });
    });
    }
}
