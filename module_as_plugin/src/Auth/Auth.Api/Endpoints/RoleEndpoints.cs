using Auth.Api.Domain;
using Auth.Api.Infrastructure;
using Auth.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SharedKernel;
namespace Auth.Api.Endpoints;

public static class RoleEndpoints
{
    public static void Map(WebApplication app)
    {
    // Roles (A-BR2)
    app.MapGet("/api/v1/admin/roles", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx) =>
    {
        await using var db = await provider.ForRequestAsync(ctx, legacy);
        return Results.Json(new { data = await db.Roles.AsNoTracking().ToListAsync() });
    }).RequireAuthorization();
    app.MapPost("/api/v1/admin/roles", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx, CreateRole req) =>
    {
        await using var db = await provider.ForRequestAsync(ctx, legacy);
        if (await db.Roles.AnyAsync(r => r.Name.ToLower() == req.Name.ToLower()))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Role name must be unique (case-insensitive).", 400);
        var role = new Role
        {
            Id = Cuid.New("r"), Name = req.Name, Description = req.Description,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return Results.Json(new { data = role }, statusCode: 201);
    }).RequireAuthorization();

    // Roles: update description / activate/deactivate (name immutable).
    app.MapPatch("/api/v1/admin/roles/{id}", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx, string id, PatchRoleRequest req) =>
    {
        await using var db = await provider.ForRequestAsync(ctx, legacy);
        var role = await db.Roles.FindAsync(id);
        if (role is null) return ApiErrors.Problem(ErrorCodes.UnknownRole, $"Unknown role '{id}'.", 404);
        if (req.Description is not null) role.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        if (req.IsActive.HasValue) role.IsActive = req.IsActive.Value;
        role.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.Json(new { data = role });
    }).RequireAuthorization();

    // Role-permission assignment.
    app.MapGet("/api/v1/admin/roles/{id}/permissions", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx, string id) =>
    {
        await using var db = await provider.ForRequestAsync(ctx, legacy);
        if (!await db.Roles.AnyAsync(r => r.Id == id))
            return ApiErrors.Problem(ErrorCodes.UnknownRole, $"Unknown role '{id}'.", 404);
        var perms = await db.RolePermissions.Where(rp => rp.RoleId == id)
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p).AsNoTracking().ToListAsync();
        return Results.Json(new { data = perms });
    }).RequireAuthorization();
    app.MapPost("/api/v1/admin/roles/{id}/permissions", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx, string id, AssignPermissionRequest req) =>
    {
        await using var db = await provider.ForRequestAsync(ctx, legacy);
        if (!await db.Roles.AnyAsync(r => r.Id == id))
            return ApiErrors.Problem(ErrorCodes.UnknownRole, $"Unknown role '{id}'.", 404);
        if (string.IsNullOrWhiteSpace(req.PermissionId) || !await db.Permissions.AnyAsync(p => p.Id == req.PermissionId))
            return ApiErrors.Problem(ErrorCodes.UnknownPermission, $"Unknown permission '{req.PermissionId}'.", 404);
        if (await db.RolePermissions.AnyAsync(x => x.RoleId == id && x.PermissionId == req.PermissionId))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Permission already granted to role.", 409);
        db.RolePermissions.Add(new RolePermission { RoleId = id, PermissionId = req.PermissionId });
        await db.SaveChangesAsync();
        return Results.Json(new { data = new { roleId = id, permissionId = req.PermissionId } }, statusCode: 201);
    }).RequireAuthorization();
    app.MapDelete("/api/v1/admin/roles/{id}/permissions/{permissionId}", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx, string id, string permissionId) =>
    {
        await using var db = await provider.ForRequestAsync(ctx, legacy);
        var link = await db.RolePermissions.FindAsync(id, permissionId);
        if (link is null) return ApiErrors.Problem(ErrorCodes.ValidationError, "Permission is not granted to role.", 404);
        db.RolePermissions.Remove(link);
        await db.SaveChangesAsync();
        return Results.Json(new { data = new { roleId = id, permissionId, revoked = true } });
    }).RequireAuthorization();

    }
}
