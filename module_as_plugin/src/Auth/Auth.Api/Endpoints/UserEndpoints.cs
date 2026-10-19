using Auth.Api.Application;
using Auth.Api.Domain;
using Auth.Api.Infrastructure;
using Auth.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SharedKernel;
namespace Auth.Api.Endpoints;

public static class UserEndpoints
{
    public static void Map(WebApplication app)
    {
    // Users (A-BR1). Tenant-scoped via JWT claim or X-Tenant-Id header
    // (bootstrap path); platform tokens without a header hit the legacy store.
    app.MapGet("/api/v1/admin/users", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx) =>
    {
        await using var db = await provider.ForRequestAsync(ctx, legacy);
        return Results.Json(new { data = await db.Users.AsNoTracking().ToListAsync() });
    }).RequireAuthorization();
    app.MapPost("/api/v1/admin/users", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx, CreateUser req) =>
    {
        await using var db = await provider.ForRequestAsync(ctx, legacy);
        if (await db.Users.AnyAsync(u => u.Email.ToLower() == req.Email.ToLower()))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Email already exists.", 400);
        if (req.Password is not null && (req.Password.Length < 8 || req.Password.Length > 200))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Password must be 8–200 characters.", 400, new { field = "password" });
        var user = new TenantUser
        {
            Id = Cuid.New("u"), Email = req.Email, Name = req.Name,
            // Password optional: with one → Argon2id; without → null (user claims
            // a password on first login, see LoginEndpoints policy).
            PasswordHash = req.Password is null ? null : SharedKernel.PasswordHasher.Hash(req.Password),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Results.Json(new { data = user }, statusCode: 201);
    }).RequireAuthorization();

    // Users: activate/deactivate + rename (email immutable).
    app.MapPatch("/api/v1/admin/users/{id}", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx, PermissionVersionCache versions, string id, PatchUserRequest req) =>
    {
        await using var db = await provider.ForRequestAsync(ctx, legacy);
        var user = await db.Users.FindAsync(id);
        if (user is null) return ApiErrors.Problem(ErrorCodes.UnknownUser, $"Unknown user '{id}'.", 404);
        if (req.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > 200)
                return ApiErrors.Problem(ErrorCodes.ValidationError, "Name must be 1–200 characters.", 400, new { field = "name" });
            user.Name = req.Name.Trim();
        }
        if (req.IsActive == false && user.IsActive &&
            await LastAdminGuard.WouldLockOutAsync(db, g => g.UserId == id))
            return LastAdminProblem();
        if (req.IsActive.HasValue) user.IsActive = req.IsActive.Value;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        if (req.IsActive.HasValue) ApiHelpers.BumpPermissions(ctx, versions); // effective sets may change
        return Results.Json(new { data = user });
    }).RequireAuthorization();


    // User-role assignment.
    app.MapGet("/api/v1/admin/users/{id}/roles", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx, string id) =>
    {
        await using var db = await provider.ForRequestAsync(ctx, legacy);
        if (!await db.Users.AnyAsync(u => u.Id == id))
            return ApiErrors.Problem(ErrorCodes.UnknownUser, $"Unknown user '{id}'.", 404);
        var roles = await db.UserRoles.Where(ur => ur.UserId == id)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r).AsNoTracking().ToListAsync();
        return Results.Json(new { data = roles });
    }).RequireAuthorization();
    app.MapPost("/api/v1/admin/users/{id}/roles", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx, PermissionVersionCache versions, string id, AssignRoleRequest req) =>
    {
        await using var db = await provider.ForRequestAsync(ctx, legacy);
        if (!await db.Users.AnyAsync(u => u.Id == id))
            return ApiErrors.Problem(ErrorCodes.UnknownUser, $"Unknown user '{id}'.", 404);
        if (string.IsNullOrWhiteSpace(req.RoleId) || !await db.Roles.AnyAsync(r => r.Id == req.RoleId))
            return ApiErrors.Problem(ErrorCodes.UnknownRole, $"Unknown role '{req.RoleId}'.", 404);
        if (await db.UserRoles.AnyAsync(x => x.UserId == id && x.RoleId == req.RoleId))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Role already assigned to user.", 409);
        db.UserRoles.Add(new UserRole { UserId = id, RoleId = req.RoleId });
        await db.SaveChangesAsync();
        ApiHelpers.BumpPermissions(ctx, versions);
        return Results.Json(new { data = new { userId = id, roleId = req.RoleId } }, statusCode: 201);
    }).RequireAuthorization();
    app.MapDelete("/api/v1/admin/users/{id}/roles/{roleId}", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx, PermissionVersionCache versions, string id, string roleId) =>
    {
        await using var db = await provider.ForRequestAsync(ctx, legacy);
        var link = await db.UserRoles.FindAsync(id, roleId);
        if (link is null) return ApiErrors.Problem(ErrorCodes.ValidationError, "Role is not assigned to user.", 404);
        if (await LastAdminGuard.WouldLockOutAsync(db, g => g.UserId == id && g.RoleId == roleId))
            return LastAdminProblem();
        db.UserRoles.Remove(link);
        await db.SaveChangesAsync();
        ApiHelpers.BumpPermissions(ctx, versions);
        return Results.Json(new { data = new { userId = id, roleId, revoked = true } });
    }).RequireAuthorization();


    // User additional (direct) permissions.
    app.MapGet("/api/v1/admin/users/{id}/permissions", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx, string id) =>
    {
        await using var db = await provider.ForRequestAsync(ctx, legacy);
        if (!await db.Users.AnyAsync(u => u.Id == id))
            return ApiErrors.Problem(ErrorCodes.UnknownUser, $"Unknown user '{id}'.", 404);
        var perms = await db.UserAdditionalPermissions.Where(u => u.UserId == id)
            .Join(db.Permissions, u => u.PermissionId, p => p.Id, (u, p) => p).AsNoTracking().ToListAsync();
        return Results.Json(new { data = perms });
    }).RequireAuthorization();
    app.MapPost("/api/v1/admin/users/{id}/permissions", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx, PermissionVersionCache versions, string id, AssignPermissionRequest req) =>
    {
        await using var db = await provider.ForRequestAsync(ctx, legacy);
        if (!await db.Users.AnyAsync(u => u.Id == id))
            return ApiErrors.Problem(ErrorCodes.UnknownUser, $"Unknown user '{id}'.", 404);
        if (string.IsNullOrWhiteSpace(req.PermissionId) || !await db.Permissions.AnyAsync(p => p.Id == req.PermissionId))
            return ApiErrors.Problem(ErrorCodes.UnknownPermission, $"Unknown permission '{req.PermissionId}'.", 404);
        if (await db.UserAdditionalPermissions.AnyAsync(x => x.UserId == id && x.PermissionId == req.PermissionId))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Permission already granted to user.", 409);
        db.UserAdditionalPermissions.Add(new UserAdditionalPermission { UserId = id, PermissionId = req.PermissionId });
        await db.SaveChangesAsync();
        ApiHelpers.BumpPermissions(ctx, versions);
        return Results.Json(new { data = new { userId = id, permissionId = req.PermissionId } }, statusCode: 201);
    }).RequireAuthorization();
    app.MapDelete("/api/v1/admin/users/{id}/permissions/{permissionId}", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx, PermissionVersionCache versions, string id, string permissionId) =>
    {
        await using var db = await provider.ForRequestAsync(ctx, legacy);
        var link = await db.UserAdditionalPermissions.FindAsync(id, permissionId);
        if (link is null) return ApiErrors.Problem(ErrorCodes.ValidationError, "Permission is not granted to user.", 404);
        db.UserAdditionalPermissions.Remove(link);
        await db.SaveChangesAsync();
        ApiHelpers.BumpPermissions(ctx, versions);
        return Results.Json(new { data = new { userId = id, permissionId, revoked = true } });
    }).RequireAuthorization();
    }

    internal static IResult LastAdminProblem() => ApiErrors.Problem(ErrorCodes.LastAdmin,
        "This change would remove the tenant's last active admin; assign another admin first.", 409);
}
