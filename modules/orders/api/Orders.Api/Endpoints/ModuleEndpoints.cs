using Microsoft.Extensions.Caching.Memory;
using Orders.Api.Models;
using SharedKernel;

namespace Orders.Api.Endpoints;

public static class ModuleEndpoints
{
    public const string ModuleId = "orders";
    public const string Version = "1.0.0";
    public const string ViewPermission = "ORDERS_DATA_VIEW";
    public const string ProtectedPermission = "ORDERS_DATA_PROTECTED";

    // Must equal manifest.yml api.seed / frontend.menus (OrdersModuleTests parity).
    public static readonly string[] SeedRoles = ["Orders Viewer"];
    public static readonly string[] SeedPermissions = [ViewPermission, ProtectedPermission];
    public static readonly string[] SeedMenus = ["orders"];

    public static void Map(WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Json(new { status = "ok", module = ModuleId, version = Version }));
        app.MapGet("/readyz", () => Results.Json(new { ready = true, checks = new { db = "n/a", dependencies = "ok" } }));

        app.MapPost("/internal/seed:preview", () => Results.Json(new
        {
            data = new { roles = SeedRoles, permissions = SeedPermissions, menus = SeedMenus },
        }));

        // Additive migrations only in v1 (no module data store).
        app.MapPost("/internal/migrate", (MigrateRequest req) =>
            req.FromVersion == req.ToVersion
                ? Results.Json(new { data = new { migrated = false } })
                : Results.Json(new { data = new { migrated = true, from = req.FromVersion, to = req.ToVersion } }));

        // Open endpoint: any signed-in user of an active tenant.
        app.MapGet("/api/v1/data", async (HttpContext ctx, IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg) =>
            await ModuleGuards.RequireTenantAsync(ctx, http, cache, cfg)
                ?? Results.Json(new { data = Content("sample", ModuleGuards.TenantId(ctx)!) }))
            .RequireAuthorization();

        // Permission-gated endpoint: re-checked on every call via Auth resolve (fail closed).
        app.MapGet("/api/v1/protected", async (HttpContext ctx, IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg) =>
        {
            if (await ModuleGuards.RequireTenantAsync(ctx, http, cache, cfg) is { } tenantError) return tenantError;
            var tenantId = ModuleGuards.TenantId(ctx)!;
            return await ModuleGuards.RequirePermissionAsync(ctx, http, cfg, tenantId, ProtectedPermission)
                ?? Results.Json(new { data = Content("protected", tenantId) });
        }).RequireAuthorization();
    }

    private static object Content(string kind, string tenantId) => new
    {
        module = ModuleId, version = Version, kind,
        generatedAt = DateTimeOffset.UtcNow.ToString("O"), tenantId,
        items = Items(kind),
    };

    private static object[] Items(string kind)
    {
        var suffix = kind == "protected" ? " (protected)" : "";
        return
        [
            new { id = "orders-1", name = $"Orders · Alpha{suffix}", value = 10 },
            new { id = "orders-2", name = $"Orders · Beta{suffix}", value = 20 },
            new { id = "orders-3", name = $"Orders · Gamma{suffix}", value = 30 },
        ];
    }
}
