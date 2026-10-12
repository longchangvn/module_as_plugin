using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Models;
using Platform.Application;
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
        return Results.Json(new { data = users });
    }).RequireAuthorization("PlatformAny");

    app.MapPost("/api/v1/platform/users", async (CatalogDbContext db, HttpContext ctx, CreateUserRequest req) =>
    {
        if (ApiHelpers.RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Admin only.", 403);
        if (await db.PlatformUsers.AnyAsync(u => u.Email.ToLower() == req.Email.ToLower()))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Email already exists.", 400);
        var user = new PlatformUser
        {
            Id = Cuid.New("pu"), Email = req.Email, Name = req.Name,
            PasswordHash = "argon2:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), // Argon2 wiring is ops config; never store plain
            Role = Enum.Parse<PlatformRole>(req.Role, true),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.PlatformUsers.Add(user);
        await db.SaveChangesAsync();
        await ApiHelpers.Audit(db, null, ApiHelpers.ActorId(ctx), ApiHelpers.RequireRole(ctx), "user.create", "platform_user", user.Id, null, ctx);
        return Results.Json(new { data = user }, statusCode: 201);
    }).RequireAuthorization("PlatformAny");
    }
}
