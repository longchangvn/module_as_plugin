using Microsoft.Extensions.Caching.Memory;
using Sample2.Api.Models;
using SharedKernel;

namespace Sample2.Api.Endpoints;

public static class ModuleEndpoints
{
    public const string ModuleId = "sample2";
    public const string Version = "1.0.0";
    public const string ProtectedPermission = "SAMPLE_TWO_DATA_PROTECTED";

    // Must equal manifest.yml api.seed / frontend.menus (SampleModuleTests parity).
    public static readonly string[] SeedRoles = ["Sample 2 Viewer"];
    public static readonly string[] SeedPermissions = ["SAMPLE_TWO_DATA_VIEW", ProtectedPermission];
    public static readonly string[] SeedMenus = ["sample2"];

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

        app.MapGet("/api/v1/data", async (HttpContext ctx, IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg) =>
            await ModuleGuards.RequireTenantAsync(ctx, http, cache, cfg)
                ?? Results.Json(new { data = Content("sample", ModuleGuards.TenantId(ctx)!) }))
            .RequireAuthorization();

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
            new { id = "s2-1", name = $"Sample 2 · Alpha{suffix}", value = 100 },
            new { id = "s2-2", name = $"Sample 2 · Beta{suffix}", value = 200 },
            new { id = "s2-3", name = $"Sample 2 · Gamma{suffix}", value = 300 },
        ];
    }
}
