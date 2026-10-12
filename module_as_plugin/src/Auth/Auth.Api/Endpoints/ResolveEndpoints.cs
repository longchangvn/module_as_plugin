using Auth.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SharedKernel;

namespace Auth.Api.Endpoints;

public static class ResolveEndpoints
{
    public static void Map(WebApplication app)
    {
    // Resolve endpoint for gateway/module APIs — cached 60s by callers, fail closed (A-BR5).
    // v1 scaffold: no in-process cache; callers add the short-TTL cache.
    app.MapGet("/api/v1/auth/permissions/resolve", async (TenantDbContext legacy, TenantDbProvider provider, IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg, string tenantId, string userId) =>
    {
        await using var db = await provider.ForTenantAsync(tenantId, legacy);
        // Disabled/unknown tenants resolve nothing (fail closed on explicit state).
        var usability = await ApiHelpers.TenantUsabilityAsync(http, cache, cfg, tenantId);
        if (usability is not null) return usability;
        var user = await db.Users.FindAsync(userId);
        if (user is null || !user.IsActive) return Results.Json(new { data = new { permissions = Array.Empty<string>(), version = 1 } });
        var roleIds = await db.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync();
        var activeRoleIds = await db.Roles.Where(r => roleIds.Contains(r.Id) && r.IsActive).Select(r => r.Id).ToListAsync();
        var rolePermIds = await db.RolePermissions.Where(rp => activeRoleIds.Contains(rp.RoleId)).Select(rp => rp.PermissionId).ToListAsync();
        var extraPermIds = await db.UserAdditionalPermissions.Where(u => u.UserId == userId).Select(u => u.PermissionId).ToListAsync();
        var codes = await db.Permissions.Where(p => rolePermIds.Concat(extraPermIds).Contains(p.Id)).Select(p => p.Code).ToListAsync();
        var union = codes.Distinct(StringComparer.Ordinal).OrderBy(c => c).ToList();
        return Results.Json(new { data = new { tenantId, userId, permissions = union, version = 1 } });
    });
    }
}
