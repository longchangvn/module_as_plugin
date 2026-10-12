using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Models;
using Platform.Application;
using Platform.Domain;
using Platform.Infrastructure;
using SharedKernel;

namespace Platform.Api.Endpoints;

public static class LoginEndpoints
{
    public static void Map(WebApplication app)
    {
    // ---- platform login: mints platform JWT (role=Admin|Maintainer, no tenant) ----
    app.MapPost("/api/v1/platform/auth/login", async (CatalogDbContext db, IConfiguration cfg, PlatformLoginRequest req) =>
    {
        var user = await db.PlatformUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == req.Email.ToLower());
        if (user is null || !user.IsActive)
            return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid credentials.", 401);
        // NOTE: password verification stubbed in v1 (see P5 note); production wires Argon2 here.
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var token = JwtMint.Mint(JwtMint.Secret(cfg), new Dictionary<string, string>
        {
            ["sub"] = user.Id, ["email"] = user.Email, ["platform_role"] = user.Role.ToString(),
        });
        return Results.Json(new { data = new { accessToken = token, role = user.Role.ToString() } });
    });

    // Shared lifecycle guards: platform Admin always allowed; tenant writers need
    }
}
