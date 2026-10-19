using Microsoft.EntityFrameworkCore;
using Platform.Api.Models;
using Platform.Infrastructure;
using SharedKernel;

namespace Platform.Api.Endpoints;

public static class LoginEndpoints
{
    public static void Map(WebApplication app)
    {
    // ---- platform login: mints platform JWT (role=Admin|Maintainer, no tenant) ----
    // Password policy (closes the P5 stub-verify gap, §17):
    //   argon2id$…  → verified (production path).
    //   sha256:…    → verified for legacy rows, transparently upgraded to Argon2.
    //   dev-stub / unverifiable placeholder → any password accepted ONLY in
    //     Development; the hash is upgraded to Argon2 of the submitted password
    //     (upgrade-on-login). Production fails closed with 401.
    app.MapPost("/api/v1/platform/auth/login", async (CatalogDbContext db, IConfiguration cfg, IHostEnvironment env, PlatformLoginRequest req) =>
    {
        var user = await db.PlatformUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == req.Email.ToLower());
        if (user is null || !user.IsActive)
            return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid credentials.", 401);
        var (ok, needsRehash) = PasswordHasher.Verify(req.Password ?? "", user.PasswordHash);
        if (!ok && PasswordHasher.IsUnverifiablePlaceholder(user.PasswordHash))
        {
            // dev upgrade-on-login (dev seed hash is a placeholder)
            if (env.IsDevelopment() && !string.IsNullOrEmpty(req.Password))
            {
                ok = true;
                needsRehash = true;
            }
        }
        if (!ok)
            return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid credentials.", 401);
        if (needsRehash) user.PasswordHash = PasswordHasher.Hash(req.Password ?? "");
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
