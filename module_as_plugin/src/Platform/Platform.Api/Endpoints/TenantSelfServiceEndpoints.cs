using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Models;
using Platform.Application;
using Platform.Domain;
using Platform.Infrastructure;
using SharedKernel;

namespace Platform.Api.Endpoints;

public static class TenantSelfServiceEndpoints
{
    public static void Map(WebApplication app, ConcurrentDictionary<string, SemaphoreSlim> installLocks)
    {
    // ---- tenant self-service catalog (§9.2: same lifecycle, tenant JWT) ----
    app.MapGet("/api/v1/tenant/modules", async (CatalogDbContext db, HttpContext ctx) =>
    {
        var tenantId = ctx.User.FindFirst("tenantId")?.Value;
        if (tenantId is null) return ApiErrors.Problem(ErrorCodes.ForbiddenTenant, "Tenant context required.", 403);
        if (ApiHelpers.CheckTenantHeader(ctx, tenantId) is { } headerError) return headerError;
        var (_, tenantError) = await ApiHelpers.RequireUsableTenantAsync(db, tenantId);
        if (tenantError is not null) return tenantError;
        var modules = await db.Modules.Where(m => m.Status == ModuleStatus.Available).AsNoTracking().ToListAsync();
        var installs = await db.Installs.Where(i => i.TenantId == tenantId).AsNoTracking().ToListAsync();
        return Results.Json(new
        {
            data = modules.Select(m => new
            {
                m.Id, m.DisplayName, m.Description, m.Owner,
                installState = installs.FirstOrDefault(i => i.ModuleId == m.Id)?.State.ToString() ?? "NOT_INSTALLED",
            }),
        });
    }).RequireAuthorization();

    app.MapPost("/api/v1/tenant/modules/{id}/install",
        (HttpContext ctx, CatalogDbContext db, ModuleLifecycleService svc, string id, InstallRequest req) =>
            LifecycleEndpoints.RunLifecycle(installLocks, ctx, db, svc, ctx.User.FindFirst("tenantId")?.Value ?? "", id, req.Version, "install"))
        .RequireAuthorization();

    app.MapGet("/api/v1/tenant/modules/{id}", async (CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string id) =>
    {
        var tid = ApiHelpers.TenantId(ctx);
        if (string.IsNullOrEmpty(tid)) return ApiErrors.Problem(ErrorCodes.ForbiddenTenant, "Tenant context required.", 403);
        if (ApiHelpers.CheckTenantHeader(ctx, tid) is { } detailHeaderError) return detailHeaderError;
        var (_, detailTenantError) = await ApiHelpers.RequireUsableTenantAsync(db, tid);
        if (detailTenantError is not null) return detailTenantError;
        var module = await db.Modules.FindAsync(id);
        if (module is null) return ApiErrors.Problem(ErrorCodes.UnknownModule, $"Unknown module '{id}'.", 404);
        var install = await db.Installs.FindAsync(tid, id);
        var versions = await db.ModuleVersions.Where(v => v.ModuleId == id).OrderBy(v => v.Version)
            .Select(v => v.Version).ToListAsync();
        // Install plan preview: resolve deps for the requested (or latest) version without applying.
        var preview = await ports.AvailableVersionsAsync(id, ctx.RequestAborted);
        var planTarget = preview.Count == 0 ? null : SemverOrdering.Latest(preview);
        IReadOnlyList<DependencyResolver.PlanStep> plan = Array.Empty<DependencyResolver.PlanStep>();
        if (planTarget is not null)
        {
            var planResult = DependencyResolver.Resolve(id, $"={planTarget}",
                n => ports.AvailableVersionsAsync(n, CancellationToken.None).GetAwaiter().GetResult(),
                (n, v) => ports.GetManifestAsync(n, v, CancellationToken.None).GetAwaiter().GetResult()?.Dependencies
                    ?? new List<ManifestDependency>());
            if (planResult.Ok) plan = planResult.Value!;
        }
        return Results.Json(new
        {
            data = new
            {
                module, installState = install?.State.ToString() ?? "NOT_INSTALLED",
                installedVersion = install?.Version, versions,
                installPlan = plan.Select(s => new { module = s.Name, version = s.Version }),
            },
        });
    }).RequireAuthorization();

    app.MapPost("/api/v1/tenant/modules/{id}/enable",
        (CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string id) =>
            LifecycleEndpoints.EnableModuleAsync(db, ctx, ports, ApiHelpers.TenantId(ctx), id)).RequireAuthorization();
    app.MapPost("/api/v1/tenant/modules/{id}/disable",
        (CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string id, DisableRequest req) =>
            LifecycleEndpoints.DisableModuleAsync(db, ctx, ports, ApiHelpers.TenantId(ctx), id, req?.Reason)).RequireAuthorization();
    app.MapPost("/api/v1/tenant/modules/{id}/uninstall",
        (CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string id) =>
            LifecycleEndpoints.UninstallModuleAsync(db, ctx, ports, ApiHelpers.TenantId(ctx), id)).RequireAuthorization();
    app.MapPost("/api/v1/tenant/modules/{id}/upgrade",
        (HttpContext ctx, CatalogDbContext db, ModuleLifecycleService svc, string id, UpgradeRequest req) =>
            LifecycleEndpoints.UpgradeModuleAsync(installLocks, ctx, db, svc, ApiHelpers.TenantId(ctx), id, req.ToVersion)).RequireAuthorization();
    }
}
