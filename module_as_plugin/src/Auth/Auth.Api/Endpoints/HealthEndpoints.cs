using Auth.Api.Infrastructure;

namespace Auth.Api.Endpoints;

public static class HealthEndpoints
{
    public static void Map(WebApplication app)
    {
    app.MapGet("/healthz", () => Results.Json(new { status = "ok", service = "auth" }));
    app.MapGet("/readyz", async (TenantDbContext db) =>
        await db.Database.CanConnectAsync()
            ? Results.Json(new { ready = true })
            : Results.Json(new { ready = false }, statusCode: 503));
    }
}
