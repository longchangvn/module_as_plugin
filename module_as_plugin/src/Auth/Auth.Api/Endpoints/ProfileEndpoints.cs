using Auth.Api.Infrastructure;
using Auth.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SharedKernel;

namespace Auth.Api.Endpoints;

// Self profile for the logged-in tenant user: name, readonly email, related
// info (roles, effective permissions, tenant), plus self-service name update
// and password change.
public static class ProfileEndpoints
{
    // The JWT handler maps "sub" to NameIdentifier by default, so accept both.
    private static string CallerUserId(HttpContext ctx) =>
        ctx.User.FindFirst("userId")?.Value
        ?? ctx.User.FindFirst("sub")?.Value
        ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? "";

    public static void Map(WebApplication app)
    {
        // GET the caller's own record (never includes the password hash).
        app.MapGet("/api/v1/auth/me", async (
            TenantDbContext legacy, TenantDbProvider provider,
            IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg, HttpContext ctx) =>
        {
            var userId = CallerUserId(ctx);
            var tenantId = ctx.User.FindFirst("tenantId")?.Value
                ?? ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? "";
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tenantId))
                return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid session.", 401);
            var usability = await ApiHelpers.TenantUsabilityAsync(http, cache, cfg, tenantId);
            if (usability is not null) return usability;
            await using var db = await provider.ForTenantAsync(tenantId, legacy);
            var user = await db.Users.FindAsync(userId);
            if (user is null || !user.IsActive)
                return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid session.", 401);
            return Results.Json(new { data = await ToProfileAsync(db, tenantId, ctx, user) });
        }).RequireAuthorization();

        // PATCH updates the caller's own display name (email is immutable).
        app.MapPatch("/api/v1/auth/me", async (
            TenantDbContext legacy, TenantDbProvider provider,
            IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg, HttpContext ctx, UpdateMyProfileRequest req) =>
        {
            var userId = CallerUserId(ctx);
            var tenantId = ctx.User.FindFirst("tenantId")?.Value
                ?? ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? "";
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tenantId))
                return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid session.", 401);
            var usability = await ApiHelpers.TenantUsabilityAsync(http, cache, cfg, tenantId);
            if (usability is not null) return usability;
            var name = req.Name?.Trim() ?? "";
            if (name.Length == 0 || name.Length > 200)
                return ApiErrors.Problem(ErrorCodes.ValidationError, "Name must be 1–200 characters.", 400, new { field = "name" });
            await using var db = await provider.ForTenantAsync(tenantId, legacy);
            var user = await db.Users.FindAsync(userId);
            if (user is null || !user.IsActive)
                return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid session.", 401);
            user.Name = name;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Json(new { data = await ToProfileAsync(db, tenantId, ctx, user) });
        }).RequireAuthorization();

        // POST changes the caller's own password. The current password is
        // verified against the stored hash (same policy as login: null hash =
        // claim-on-first-login in Development flows); the new password is
        // stored as an Argon2id hash.
        app.MapPost("/api/v1/auth/change-password", async (
            TenantDbContext legacy, TenantDbProvider provider,
            IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg, HttpContext ctx, ChangeMyPasswordRequest req) =>
        {
            var userId = CallerUserId(ctx);
            var tenantId = ctx.User.FindFirst("tenantId")?.Value
                ?? ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? "";
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tenantId))
                return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid session.", 401);
            var usability = await ApiHelpers.TenantUsabilityAsync(http, cache, cfg, tenantId);
            if (usability is not null) return usability;
            if (string.IsNullOrWhiteSpace(req.CurrentPassword))
                return ApiErrors.Problem(ErrorCodes.ValidationError, "Current password is required.", 400, new { field = "currentPassword" });
            if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8 || req.NewPassword.Length > 200)
                return ApiErrors.Problem(ErrorCodes.ValidationError, "New password must be 8–200 characters.", 400, new { field = "newPassword" });
            await using var db = await provider.ForTenantAsync(tenantId, legacy);
            var user = await db.Users.FindAsync(userId);
            if (user is null || !user.IsActive)
                return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid session.", 401);
            var (ok, _) = PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash);
            // null hash (bootstrap/seed user): accept the current password once —
            // same claim semantics as first login.
            if (!ok && user.PasswordHash is not null)
                return ApiErrors.Problem(ErrorCodes.Unauthorized, "Current password is invalid.", 401);
            user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Json(new { data = new { changed = true } });
        }).RequireAuthorization();
    }

    private static async Task<object> ToProfileAsync(
        TenantDbContext db, string tenantId, HttpContext ctx, Auth.Api.Domain.TenantUser user)
    {
        var roleIds = await db.UserRoles.Where(ur => ur.UserId == user.Id).Select(ur => ur.RoleId).ToListAsync();
        var roles = await db.Roles.Where(r => roleIds.Contains(r.Id)).AsNoTracking().ToListAsync();
        var activeRoleIds = roles.Where(r => r.IsActive).Select(r => r.Id).ToList();
        var rolePermIds = await db.RolePermissions.Where(rp => activeRoleIds.Contains(rp.RoleId)).Select(rp => rp.PermissionId).ToListAsync();
        var extraPermIds = await db.UserAdditionalPermissions.Where(u => u.UserId == user.Id).Select(u => u.PermissionId).ToListAsync();
        var codes = await db.Permissions
            .Where(p => rolePermIds.Concat(extraPermIds).Contains(p.Id))
            .Select(p => p.Code).ToListAsync();
        var permissions = codes.Distinct(StringComparer.Ordinal).OrderBy(c => c).ToList();
        var tenantAdmin = string.Equals(ctx.User.FindFirst("tenant_admin")?.Value, "true", StringComparison.OrdinalIgnoreCase);
        return new
        {
            user.Id,
            user.Email,
            user.Name,
            user.IsActive,
            tenantId,
            tenantAdmin,
            roles = roles.Select(r => new { r.Id, r.Name, r.IsActive }).ToList(),
            permissions,
            user.CreatedAt,
            user.UpdatedAt,
        };
    }
}
