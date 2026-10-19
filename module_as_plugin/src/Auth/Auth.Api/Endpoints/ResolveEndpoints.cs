using Auth.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SharedKernel;

namespace Auth.Api.Endpoints;

public static class ResolveEndpoints
{
    public static void Map(WebApplication app)
    {
    // Resolve endpoint for gateway/module APIs — A-BR5: effective set (role ∪
    // additional, ACTIVE only), cached server-side 60s per (tenant,user) and
    // invalidated by a per-tenant version bump on every seed apply/revoke.
    app.MapGet("/api/v1/auth/permissions/resolve", async (TenantDbContext legacy, TenantDbProvider provider, IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg, PermissionVersionCache versions, string tenantId, string userId) =>
    {
        // Disabled/unknown tenants resolve nothing (fail closed on explicit state).
        var usability = await ApiHelpers.TenantUsabilityAsync(http, cache, cfg, tenantId);
        if (usability is not null) return usability;
        var version = versions.For(tenantId);
        var cacheKey = $"perm-resolve:{tenantId}:{userId}:{version}";
        if (cache.TryGetValue<List<string>>(cacheKey, out var cachedPerms) && cachedPerms is not null)
            return Results.Json(new { data = new { tenantId, userId, permissions = cachedPerms, version } });
        await using var db = await provider.ForTenantAsync(tenantId, legacy);
        var user = await db.Users.FindAsync(userId);
        if (user is null || !user.IsActive) return Results.Json(new { data = new { permissions = Array.Empty<string>(), version } });
        var roleIds = await db.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync();
        var activeRoleIds = await db.Roles.Where(r => roleIds.Contains(r.Id) && r.IsActive).Select(r => r.Id).ToListAsync();
        var rolePermIds = await db.RolePermissions.Where(rp => activeRoleIds.Contains(rp.RoleId)).Select(rp => rp.PermissionId).ToListAsync();
        var extraPermIds = await db.UserAdditionalPermissions.Where(u => u.UserId == userId).Select(u => u.PermissionId).ToListAsync();
        var codes = await db.Permissions.Where(p => rolePermIds.Concat(extraPermIds).Contains(p.Id)).Select(p => p.Code).ToListAsync();
        var union = codes.Distinct(StringComparer.Ordinal).OrderBy(c => c).ToList();
        cache.Set(cacheKey, union, TimeSpan.FromSeconds(60));
        return Results.Json(new { data = new { tenantId, userId, permissions = union, version } });
    });
    }
}
