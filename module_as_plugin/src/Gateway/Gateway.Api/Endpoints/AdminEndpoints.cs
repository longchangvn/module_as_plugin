using Gateway.Api.Models;
using Gateway.Api.Services;

namespace Gateway.Api.Endpoints;

public static class AdminEndpoints
{
    public static void Map(WebApplication app)
    {
        var configuration = app.Configuration;
        var isDevelopment = app.Environment.IsDevelopment();

        // Admin branch — reserved prefix, never proxied (SR-1).
        app.MapGet("/api/v1/gateway/summary", (GatewayStore store, TrafficRecorder traffic) =>
        {
            var summary = store.GetSummary();
            return Results.Json(new { data = summary with { TotalRequests = traffic.Total } });
        });
        app.MapGet("/api/v1/gateway/services", async (GatewayStore store, bool health = false) =>
            Results.Json(new { data = await store.ServicesWithHealthAsync(health), count = store.Services().Count }));
        app.MapPost("/api/v1/gateway/services", async (GatewayStore store, HttpContext ctx, ServiceUpsert req) =>
        {
            if (!GatewayHelpers.IsWriter(ctx, configuration, GatewayHelpers.UseDevKey(ctx, configuration, isDevelopment)))
                return Results.Json(new { error = new { code = "FORBIDDEN", message = "Admin only." } }, statusCode: 403);
            var result = await store.AddServiceAsync(req);
            return result.Ok ? Results.Json(new { data = result.Value }, statusCode: 201)
                : Results.Json(new { error = new { code = result.ErrorCode, message = result.Error } }, statusCode: 409);
        });
        app.MapPut("/api/v1/gateway/services/{id}", async (GatewayStore store, HttpContext ctx, string id, ServiceUpsert req) =>
        {
            if (!GatewayHelpers.IsWriter(ctx, configuration, GatewayHelpers.UseDevKey(ctx, configuration, isDevelopment)))
                return Results.Json(new { error = new { code = "FORBIDDEN", message = "Admin only." } }, statusCode: 403);
            var result = await store.UpdateServiceAsync(id, req);
            return result.Ok ? Results.Json(new { data = result.Value })
                : Results.Json(new { error = new { code = result.ErrorCode, message = result.Error } }, statusCode: 404);
        });
        app.MapDelete("/api/v1/gateway/services/{id}", async (GatewayStore store, HttpContext ctx, string id) =>
        {
            if (!GatewayHelpers.IsWriter(ctx, configuration, GatewayHelpers.UseDevKey(ctx, configuration, isDevelopment)))
                return Results.Json(new { error = new { code = "FORBIDDEN", message = "Admin only." } }, statusCode: 403);
            var result = await store.DeleteServiceAsync(id);
            return result.Ok ? Results.Json(new { data = new { id } })
                : Results.Json(new { error = new { code = result.ErrorCode, message = result.Error, routes = result.Value } },
                    statusCode: result.ErrorCode == "NOT_FOUND" ? 404 : 409);
        });
        app.MapGet("/api/v1/gateway/routes", (GatewayStore store) =>
            Results.Json(new { data = store.Routes() }));
        app.MapPost("/api/v1/gateway/routes", async (GatewayStore store, HttpContext ctx, RouteUpsert req) =>
        {
            if (!GatewayHelpers.IsWriter(ctx, configuration, GatewayHelpers.UseDevKey(ctx, configuration, isDevelopment)))
                return Results.Json(new { error = new { code = "FORBIDDEN", message = "Admin only." } }, statusCode: 403);
            var result = await store.AddRouteAsync(req);
            if (!result.Ok)
            {
                var status = result.ErrorCode is "ROUTE_EXISTS" ? 409 : 400;
                return Results.Json(new { error = new { code = result.ErrorCode, message = result.Error, retryable = false } }, statusCode: status);
            }
            return Results.Json(new { data = result.Value }, statusCode: 201);
        });
        app.MapPut("/api/v1/gateway/routes/{id}", async (GatewayStore store, HttpContext ctx, string id, RouteUpsert req) =>
        {
            if (!GatewayHelpers.IsWriter(ctx, configuration, GatewayHelpers.UseDevKey(ctx, configuration, isDevelopment)))
                return Results.Json(new { error = new { code = "FORBIDDEN", message = "Admin only." } }, statusCode: 403);
            var result = await store.UpdateRouteAsync(id, req);
            if (!result.Ok)
            {
                var status = result.ErrorCode == "NOT_FOUND" ? 404 : result.ErrorCode == "ROUTE_EXISTS" ? 409 : 400;
                return Results.Json(new { error = new { code = result.ErrorCode, message = result.Error, retryable = false } }, statusCode: status);
            }
            return Results.Json(new { data = result.Value });
        });
        app.MapDelete("/api/v1/gateway/routes/{id}", async (GatewayStore store, HttpContext ctx, string id, bool force = false) =>
        {
            if (!GatewayHelpers.IsWriter(ctx, configuration, GatewayHelpers.UseDevKey(ctx, configuration, isDevelopment)))
                return Results.Json(new { error = new { code = "FORBIDDEN", message = "Admin only." } }, statusCode: 403);
            var result = await store.DeleteRouteAsync(id, force);
            return result.Ok ? Results.Json(new { data = new { id } })
                : Results.Json(new { error = new { code = result.ErrorCode, message = result.Error } }, statusCode: result.ErrorCode == "NOT_FOUND" ? 404 : 409);
        });
        app.MapPost("/api/v1/gateway/config/reload", async (GatewayStore store, HttpContext ctx) =>
        {
            if (!GatewayHelpers.IsWriter(ctx, configuration, GatewayHelpers.UseDevKey(ctx, configuration, isDevelopment)))
                return Results.Json(new { error = new { code = "FORBIDDEN", message = "Admin only." } }, statusCode: 403);
            var (ok, err) = await store.ReloadAsync();
            return ok ? Results.Json(new { data = new { reloaded = true } })
                : Results.Json(new { error = new { code = "CONFIG_INVALID", message = err, retryable = false } }, statusCode: 400);
        });
        app.MapGet("/api/v1/gateway/traffic", (TrafficRecorder traffic, int windowMinutes = 60) =>
            Results.Json(new { data = traffic.Snapshot(windowMinutes) }));
        app.MapPost("/api/v1/gateway/traffic/reset", (HttpContext ctx, TrafficRecorder traffic) =>
        {
            if (!GatewayHelpers.IsWriter(ctx, configuration, GatewayHelpers.UseDevKey(ctx, configuration, isDevelopment)))
                return Results.Json(new { error = new { code = "FORBIDDEN", message = "Admin only." } }, statusCode: 403);
            traffic.Clear();
            return Results.Json(new { data = new { reset = true } });
        });

        // Internal: platform lifecycle writes module routes (source=module-install, audited).
        app.MapPost("/api/v1/gateway/internal/module-routes", async (GatewayStore store, ModuleRoutesRequest req) =>
        {
            var result = await store.ApplyModuleRoutesAsync(req.ModuleId, req.Upstreams, req.Enabled, req.Remove, req.BaseUrl);
            return result.Ok ? Results.Json(new { data = new { applied = result.Value } })
                : Results.Json(new { error = new { code = "GATEWAY_APPLY_FAILED", message = result.Error } }, statusCode: 502);
        });
    }
}
