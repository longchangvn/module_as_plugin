using Microsoft.EntityFrameworkCore;
using Platform.Infrastructure;
using SharedKernel;

namespace Platform.Api.Endpoints;

public static class HealthEndpoints
{
    public static void Map(WebApplication app)
    {
    // ---- health ----
    app.MapGet("/healthz", () => Results.Json(new { status = "ok", service = "platform" }));
    app.MapGet("/readyz", async (CatalogDbContext db) =>
        await db.Database.CanConnectAsync()
            ? Results.Json(new { ready = true })
            : Results.Json(new { ready = false }, statusCode: 503));
    }
}
