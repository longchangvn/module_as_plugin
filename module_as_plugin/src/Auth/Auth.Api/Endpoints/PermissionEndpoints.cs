using Auth.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Auth.Api.Endpoints;

public static class PermissionEndpoints
{
    public static void Map(WebApplication app)
    {
    // Permission catalog (read-only, A-BR3/A-BR5)
    app.MapGet("/api/v1/auth/permissions", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx) =>
    {
        await using var db = await provider.ForRequestAsync(ctx, legacy);
        return Results.Json(new { data = await db.Permissions.AsNoTracking().ToListAsync() });
    }).RequireAuthorization();
    }
}
