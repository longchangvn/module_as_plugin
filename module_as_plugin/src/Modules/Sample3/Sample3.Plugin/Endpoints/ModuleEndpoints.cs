using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Sample3.Plugin.Endpoints;

public static class ModuleEndpoints
{
    public const string ModuleId = "sample3";
    public const string Version = "1.0.0";
    public const string ViewPermission = "SAMPLE_THREE_DATA_VIEW";
    public const string ProtectedPermission = "SAMPLE_THREE_DATA_PROTECTED";

    // Must equal manifest.yml api.seed / frontend.menus (Sample3ModuleTests parity).
    public static readonly string[] SeedRoles = ["Sample Module 3 Viewer"];
    public static readonly string[] SeedPermissions = [ViewPermission, ProtectedPermission];
    public static readonly string[] SeedMenus = ["sample3"];

    // Paths are relative to the plugin root (/plugins/sample3). Upgrades: IModulePlugin.MigrateAsync.
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
            new { id = "sample3-1", name = $"Sample Module 3 · Alpha{suffix}", value = 10 },
            new { id = "sample3-2", name = $"Sample Module 3 · Beta{suffix}", value = 20 },
            new { id = "sample3-3", name = $"Sample Module 3 · Gamma{suffix}", value = 30 },
        ];
    }
}
