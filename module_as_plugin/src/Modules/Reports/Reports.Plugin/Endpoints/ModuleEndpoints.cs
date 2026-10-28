using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Reports.Plugin.Endpoints;

public static class ModuleEndpoints
{
    public const string ModuleId = "reports";
    public const string Version = "1.0.0";
    public const string ViewPermission = "REPORTS_DATA_VIEW";
    public const string ProtectedPermission = "REPORTS_DATA_PROTECTED";

    // Must equal manifest.yml api.seed / frontend.menus (ReportsModuleTests parity).
    public static readonly string[] SeedRoles = ["Reports Viewer"];
    public static readonly string[] SeedPermissions = [ViewPermission, ProtectedPermission];
    public static readonly string[] SeedMenus = ["reports"];

    // Paths are relative to the plugin root (/plugins/reports). Upgrades: IModulePlugin.MigrateAsync.
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", () => Results.Json(new { status = "ok", module = ModuleId, version = Version }));
        app.MapGet("/readyz", () => Results.Json(new { ready = true, checks = new { db = "n/a", dependencies = "ok" } }));

        app.MapPost("/internal/seed:preview", () => Results.Json(new
        {
            data = new { roles = SeedRoles, permissions = SeedPermissions, menus = SeedMenus },
        }));

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
            new { id = "reports-1", name = $"Reports · Alpha{suffix}", value = 10 },
            new { id = "reports-2", name = $"Reports · Beta{suffix}", value = 20 },
            new { id = "reports-3", name = $"Reports · Gamma{suffix}", value = 30 },
        ];
    }
}
