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

        // Internal: platform lifecycle pushes per-tenant module state
        // (source=module-install). X-Internal-Key required on every call (R-GW2).
        var modulesApi = app.MapGroup("/api/v1/gateway/internal/modules")
            .AddEndpointFilter(async (fc, next) => GatewayHelpers.IsInternalCaller(fc.HttpContext, configuration)
                ? await next(fc)
                : Results.Json(new { error = new { code = "UNAUTHORIZED", message = "Invalid internal key." } }, statusCode: 401));
        modulesApi.MapPut("/{moduleId}/tenants/{tenantId}", async (GatewayStore store, string moduleId, string tenantId, ModuleTenantUpsert req) =>
        {
            if (GatewayStore.ValidateModuleSpec(moduleId, tenantId, req.Version, req.BaseUrl, req.Upstreams) is { } invalid)
                return ConfigInvalid(invalid);
            var (ok, err) = await store.UpsertModuleTenantAsync(moduleId, tenantId, req);
            return ok ? Results.Json(new { data = new { moduleId, tenantId, req.Enabled, req.Version } }) : ApplyFailed(err);
        });
        modulesApi.MapDelete("/{moduleId}/tenants/{tenantId}", async (GatewayStore store, string moduleId, string tenantId) =>
        {
            var (ok, err) = await store.RemoveModuleTenantAsync(moduleId, tenantId);
            return ok ? Results.Json(new { data = new { moduleId, tenantId, removed = true } }) : ApplyFailed(err);
        });
        // Full replace — e.g. disaster recovery; the reconciler uses the same path in-process.
        app.MapPut("/api/v1/gateway/internal/modules:snapshot", async (GatewayStore store, HttpContext ctx, ModuleStatesSnapshot req) =>
        {
            if (!GatewayHelpers.IsInternalCaller(ctx, configuration))
                return Results.Json(new { error = new { code = "UNAUTHORIZED", message = "Invalid internal key." } }, statusCode: 401);
            if (SnapshotError(req) is { } invalid) return ConfigInvalid(invalid);
            var (ok, _, drift, err) = await store.ReplaceModuleStatesAsync(ModuleTenantState.FromSnapshot(req.Modules));
            return ok ? Results.Json(new { data = new { modules = req.Modules.Count, drift } }) : ApplyFailed(err);
        });
        app.MapGet("/api/v1/gateway/internal/modules", (GatewayStore store, HttpContext ctx) =>
            GatewayHelpers.IsInternalCaller(ctx, configuration)
                ? Results.Json(new { data = store.ModuleStates.Modules })
                : Results.Json(new { error = new { code = "UNAUTHORIZED", message = "Invalid internal key." } }, statusCode: 401));
    }

    public static string? SnapshotError(ModuleStatesSnapshot snapshot)
    {
        foreach (var m in snapshot.Modules ?? [])
        {
            foreach (var v in m.Versions ?? [])
                if (GatewayStore.ValidateModuleSpec(m.ModuleId, null, v.Version, v.BaseUrl, v.Upstreams) is { } err) return err;
            foreach (var t in m.Tenants ?? [])
                if (GatewayStore.ValidateModuleSpec(m.ModuleId, t.TenantId, t.Version, null, null) is { } err) return err;
        }
        return null;
    }

    private static IResult ConfigInvalid(string message) =>
        Results.Json(new { error = new { code = "CONFIG_INVALID", message, retryable = false } }, statusCode: 400);

    private static IResult ApplyFailed(string? message) =>
        Results.Json(new { error = new { code = "GATEWAY_APPLY_FAILED", message } }, statusCode: 502);
}
