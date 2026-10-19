using Microsoft.EntityFrameworkCore;
using Platform.Api.Models;
using Platform.Domain;
using Platform.Infrastructure;
using SharedKernel;

namespace Platform.Api.Endpoints;

public static class PlatformUserEndpoints
{
    public static void Map(WebApplication app)
    {
    // ---- platform users (P-BR1; Admin only) ----
    app.MapGet("/api/v1/platform/users", async (CatalogDbContext db, HttpContext ctx) =>
    {
        if (ApiHelpers.RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Admin only.", 403);
        var users = await db.PlatformUsers.AsNoTracking().ToListAsync();
        return Results.Json(new { data = users.Select(ToProfile) });
    }).RequireAuthorization("PlatformAny");

    // Create. Password optional: with one → Argon2id hash; without → unverifiable
    // placeholder (cannot log in until an admin resets the password).
    app.MapPost("/api/v1/platform/users", async (CatalogDbContext db, HttpContext ctx, CreateUserRequest req) =>
    {
        if (ApiHelpers.RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Admin only.", 403);
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Name))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Email and name are required.", 400);
        if (req.Password is not null && (req.Password.Length < 8 || req.Password.Length > 200))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Password must be 8–200 characters.", 400, new { field = "password" });
        if (!Enum.TryParse<PlatformRole>(req.Role, true, out var role))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Role must be Admin|Maintainer.", 400, new { field = "role" });
        if (await db.PlatformUsers.AnyAsync(u => u.Email.ToLower() == req.Email.ToLower()))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Email already exists.", 400);
        var user = new PlatformUser
        {
            Id = Cuid.New("pu"), Email = req.Email, Name = req.Name,
            PasswordHash = req.Password is null ? "unset" : PasswordHasher.Hash(req.Password),
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.PlatformUsers.Add(user);
        await db.SaveChangesAsync();
        await ApiHelpers.Audit(db, null, ApiHelpers.ActorId(ctx), ApiHelpers.RequireRole(ctx), "user.create", "platform_user", user.Id, null, ctx);
        return Results.Json(new { data = ToProfile(user) }, statusCode: 201);
    }).RequireAuthorization("PlatformAny");

    // P-BR1: deactivate / reactivate (Admin only; cannot deactivate yourself).
    app.MapPost("/api/v1/platform/users/{id}/deactivate", async (CatalogDbContext db, HttpContext ctx, string id) =>
    {
        if (ApiHelpers.RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Admin only.", 403);
        var user = await db.PlatformUsers.FindAsync(id);
        if (user is null) return ApiErrors.Problem(ErrorCodes.UnknownUser, $"Unknown user '{id}'.", 404);
        if (user.Id == ApiHelpers.ActorId(ctx))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "You cannot deactivate your own account.", 422);
        if (!user.IsActive) return ApiErrors.Problem(ErrorCodes.ValidationError, "User is already inactive.", 409);
        user.IsActive = false; user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await ApiHelpers.Audit(db, null, ApiHelpers.ActorId(ctx), ApiHelpers.RequireRole(ctx), "user.deactivate", "platform_user", id, null, ctx);
        return Results.Json(new { data = ToProfile(user) });
    }).RequireAuthorization("PlatformAny");

    app.MapPost("/api/v1/platform/users/{id}/activate", async (CatalogDbContext db, HttpContext ctx, string id) =>
    {
        if (ApiHelpers.RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Admin only.", 403);
        var user = await db.PlatformUsers.FindAsync(id);
        if (user is null) return ApiErrors.Problem(ErrorCodes.UnknownUser, $"Unknown user '{id}'.", 404);
        if (user.IsActive) return ApiErrors.Problem(ErrorCodes.ValidationError, "User is already active.", 409);
        user.IsActive = true; user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await ApiHelpers.Audit(db, null, ApiHelpers.ActorId(ctx), ApiHelpers.RequireRole(ctx), "user.activate", "platform_user", id, null, ctx);
        return Results.Json(new { data = ToProfile(user) });
    }).RequireAuthorization("PlatformAny");

    // P-BR1: admin-set password reset (no current password required; audited).
    app.MapPost("/api/v1/platform/users/{id}/reset-password", async (CatalogDbContext db, HttpContext ctx, string id, ResetPlatformPasswordRequest req) =>
    {
        if (ApiHelpers.RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Admin only.", 403);
        var user = await db.PlatformUsers.FindAsync(id);
        if (user is null) return ApiErrors.Problem(ErrorCodes.UnknownUser, $"Unknown user '{id}'.", 404);
        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8 || req.NewPassword.Length > 200)
            return ApiErrors.Problem(ErrorCodes.ValidationError, "New password must be 8–200 characters.", 400, new { field = "newPassword" });
        user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await ApiHelpers.Audit(db, null, ApiHelpers.ActorId(ctx), ApiHelpers.RequireRole(ctx), "user.password-reset", "platform_user", id, null, ctx);
        return Results.Json(new { data = new { reset = true } });
    }).RequireAuthorization("PlatformAny");

    // ---- self profile (any platform session) ----
    // GET returns the caller's own record without the password hash.
    app.MapGet("/api/v1/platform/auth/me", async (CatalogDbContext db, HttpContext ctx) =>
    {
        var user = await db.PlatformUsers.FindAsync(ApiHelpers.ActorId(ctx));
        if (user is null || !user.IsActive)
            return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid session.", 401);
        return Results.Json(new { data = ToProfile(user) });
    }).RequireAuthorization("PlatformAny");

    // PATCH updates the caller's own display name (email is immutable).
    app.MapPatch("/api/v1/platform/auth/me", async (CatalogDbContext db, HttpContext ctx, UpdatePlatformProfileRequest req) =>
    {
        var user = await db.PlatformUsers.FindAsync(ApiHelpers.ActorId(ctx));
        if (user is null || !user.IsActive)
            return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid session.", 401);
        var name = req.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 200)
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Name must be 1–200 characters.", 400, new { field = "name" });
        user.Name = name;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.Json(new { data = ToProfile(user) });
    }).RequireAuthorization("PlatformAny");

    // POST changes the caller's own password. The current password is verified
    // (Argon2, or legacy sha256/dev-stub via the same policy as login); the new
    // password is stored as an Argon2id hash.
    app.MapPost("/api/v1/platform/auth/change-password", async (CatalogDbContext db, HttpContext ctx, IHostEnvironment env, ChangePlatformPasswordRequest req) =>
    {
        var user = await db.PlatformUsers.FindAsync(ApiHelpers.ActorId(ctx));
        if (user is null || !user.IsActive)
            return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid session.", 401);
        if (string.IsNullOrWhiteSpace(req.CurrentPassword))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Current password is required.", 400, new { field = "currentPassword" });
        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8 || req.NewPassword.Length > 200)
            return ApiErrors.Problem(ErrorCodes.ValidationError, "New password must be 8–200 characters.", 400, new { field = "newPassword" });
        var (ok, _) = PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash);
        // Same policy as login: unverifiable placeholder (dev seed) accepts the
        // current password in Development only; Production fails closed.
        if (!ok && !(PasswordHasher.IsUnverifiablePlaceholder(user.PasswordHash) && env.IsDevelopment()))
            return ApiErrors.Problem(ErrorCodes.Unauthorized, "Current password is invalid.", 401);
        user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await ApiHelpers.Audit(db, null, user.Id, user.Role.ToString(), "user.password-change", "platform_user", user.Id, null, ctx);
        return Results.Json(new { data = new { changed = true } });
    }).RequireAuthorization("PlatformAny");
    }

    // Profile view — never includes the password hash.
    private static object ToProfile(PlatformUser user) => new
    {
        user.Id, user.Email, user.Name,
        role = user.Role.ToString(),
        user.IsActive, user.LastLoginAt, user.CreatedAt, user.UpdatedAt,
    };
}
