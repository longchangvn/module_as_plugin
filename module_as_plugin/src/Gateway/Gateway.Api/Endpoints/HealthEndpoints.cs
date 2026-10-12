namespace Gateway.Api.Endpoints;

public static class HealthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Json(new { status = "ok", service = "gateway" }));
    }
}
