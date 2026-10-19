using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Models;
using Platform.Application;
using Platform.Domain;
using Platform.Infrastructure;
using SharedKernel;

namespace Platform.Api.Endpoints;

public static class LifecycleEndpoints
{
    private static SemaphoreSlim InstallLock(
        ConcurrentDictionary<string, SemaphoreSlim> locks, string tenant, string module) =>
        locks.GetOrAdd($"{tenant}:{module.ToLowerInvariant()}", _ => new SemaphoreSlim(1, 1));

    public static async Task<IResult> RunLifecycle(ConcurrentDictionary<string, SemaphoreSlim> installLocks, HttpContext ctx, CatalogDbContext db, ModuleLifecycleService svc,
        string tenantId, string moduleId, string? version, string action)
    {
        var (_, tenantError) = await ApiHelpers.RequireUsableTenantAsync(db, tenantId);
        if (tenantError is not null) return tenantError;
        if (ApiHelpers.CheckTenantHeader(ctx, tenantId) is { } headerError) return headerError;
        var gate = action is "install" or "upgrade";
        if (gate && ApiHelpers.RequireRole(ctx) is not ("Admin" or "TENANT_ADMIN"))
        {
            // platform Admin always allowed; tenant admin allowed via tenant JWT (claim tenant_admin=true + matching tenant)
            var isTenantAdmin = ctx.User.FindFirst("tenant_admin")?.Value == "true"
                && ctx.User.FindFirst("tenantId")?.Value == tenantId;
            if (!isTenantAdmin && ApiHelpers.RequireRole(ctx) != "Admin")
                return ApiErrors.Problem(ErrorCodes.Forbidden, "Not allowed.", 403);
        }
        var sem = InstallLock(installLocks, tenantId, moduleId);
        await sem.WaitAsync();
        // R8 across replicas: PG advisory lock keyed on (tenant, module).
        await using var distLock = await Platform.Api.Services.InstallLock.AcquireAsync(db, tenantId, moduleId, ctx.RequestAborted);
        try
        {
            var result = await svc.InstallAsync(tenantId, moduleId, version, ApiHelpers.ActorId(ctx),
                string.IsNullOrEmpty(ApiHelpers.RequireRole(ctx)) ? "TENANT_ADMIN" : ApiHelpers.RequireRole(ctx), ctx.RequestAborted);
            return result.Ok
                ? Results.Json(new { data = new { moduleId, version = result.Value, state = "INSTALLED" } }, statusCode: 202)
                : ApiHelpers.MapLifecycleError(result.Error!);
        }
        finally { sem.Release(); }
    }

    public static async Task<IResult> EnableModuleAsync(CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string tid, string id)
    {
        if (!ApiHelpers.CanWrite(ctx, tid)) return ApiErrors.Problem(ErrorCodes.Forbidden, "Not allowed.", 403);
        var (_, tenantError) = await ApiHelpers.RequireUsableTenantAsync(db, tid);
        if (tenantError is not null) return tenantError;
        if (ApiHelpers.CheckTenantHeader(ctx, tid) is { } headerError) return headerError;
        await using var distLock = await Platform.Api.Services.InstallLock.AcquireAsync(db, tid, id, ctx.RequestAborted); // R8 across replicas
        var install = await db.Installs.FindAsync(tid, id);
        if (install is null) return ApiErrors.Problem(ErrorCodes.ModuleNotInstalled, $"Module '{id}' not installed.", 404);
        // R7: dependencies enabled — gate on install states (health check is the module's /readyz).
        var states = await db.Installs.Where(i => i.TenantId == tid).AsNoTracking().ToListAsync();
        var manifest = await ports.GetManifestAsync(id, install.Version, ctx.RequestAborted);
        if (manifest is not null)
        {
            var missing = manifest.Dependencies
                .Where(d => !states.Any(s => s.ModuleId.Equals(d.Name, StringComparison.OrdinalIgnoreCase)
                    && s.State == InstallState.Installed && s.Enabled))
                .Select(d => d.Name).ToList();
            if (missing.Count > 0)
                return ApiErrors.Problem(ErrorCodes.ModuleNotReady,
                    $"Dependencies not enabled: {string.Join(", ", missing)}.", 422, new { missing });
            try { await ports.ApplyGatewayRoutesAsync(tid, manifest, enabled: true, ctx.RequestAborted); }
            catch (Exception ex) { return ApiErrors.Problem(ErrorCodes.GatewayApplyFailed, ex.Message, 502, retryable: true); }
        }
        install.State = InstallState.Installed; install.Enabled = true; install.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await ApiHelpers.Audit(db, tid, ApiHelpers.ActorId(ctx), ApiHelpers.ActorRole(ctx), "module.enable", "module", id, null, ctx);
        return Results.Json(new { data = install });
    }

    public static async Task<IResult> DisableModuleAsync(CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string tid, string id, string? reason)
    {
        if (!ApiHelpers.CanWrite(ctx, tid)) return ApiErrors.Problem(ErrorCodes.Forbidden, "Not allowed.", 403);
        var (_, tenantError) = await ApiHelpers.RequireUsableTenantAsync(db, tid);
        if (tenantError is not null) return tenantError;
        if (ApiHelpers.CheckTenantHeader(ctx, tid) is { } headerError) return headerError;
        await using var distLock = await Platform.Api.Services.InstallLock.AcquireAsync(db, tid, id, ctx.RequestAborted); // R8 across replicas
        var install = await db.Installs.FindAsync(tid, id);
        if (install is null) return ApiErrors.Problem(ErrorCodes.ModuleNotInstalled, $"Module '{id}' not installed.", 404);
        var manifest = await ports.GetManifestAsync(id, install.Version, ctx.RequestAborted);
        if (manifest is not null)
        {
            try { await ports.ApplyGatewayRoutesAsync(tid, manifest, enabled: false, ctx.RequestAborted); }
            catch (Exception ex) { return ApiErrors.Problem(ErrorCodes.GatewayApplyFailed, ex.Message, 502, retryable: true); }
        }
        install.State = InstallState.Disabled; install.Enabled = false; install.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await ApiHelpers.Audit(db, tid, ApiHelpers.ActorId(ctx), ApiHelpers.ActorRole(ctx), "module.disable", "module", id, reason, ctx);
        return Results.Json(new { data = install });
    }

    public static async Task<IResult> UninstallModuleAsync(CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string tid, string id)
    {
        if (!ApiHelpers.CanWrite(ctx, tid)) return ApiErrors.Problem(ErrorCodes.Forbidden, "Not allowed.", 403);
        var (_, tenantError) = await ApiHelpers.RequireUsableTenantAsync(db, tid);
        if (tenantError is not null) return tenantError;
        if (ApiHelpers.CheckTenantHeader(ctx, tid) is { } headerError) return headerError;
        await using var distLock = await Platform.Api.Services.InstallLock.AcquireAsync(db, tid, id, ctx.RequestAborted); // R8 across replicas
        var states = await db.Installs.Where(i => i.TenantId == tid).AsNoTracking().ToListAsync();
        // R2 guard: implemented against stored manifests' dependency lists
        var blockers = new List<string>();
        foreach (var s in states)
        {
            if (s.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
            if (s.State is not (InstallState.Installed or InstallState.Disabled)) continue;
            var ver = await db.ModuleVersions.AsNoTracking()
                .FirstOrDefaultAsync(v => v.ModuleId == s.ModuleId && v.Version == s.Version);
            if (ver is null) continue;
            try
            {
                var deps = ManifestParser.Parse(ver.ManifestYaml).Dependencies;
                if (deps.Any(d => d.Name.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    blockers.Add(s.ModuleId);
            }
            catch { /* ignore */ }
        }
        if (blockers.Count > 0)
            return ApiErrors.Problem(ErrorCodes.DependencyInUse,
                $"Module '{id}' is required by installed module(s).", 409, new { dependents = blockers });

        var install = await db.Installs.FindAsync(tid, id);
        if (install is null) return ApiErrors.Problem(ErrorCodes.ModuleNotInstalled, $"Module '{id}' not installed.", 404);
        // R6: revoke routes + seed grants; data archived per retention (history stays in audit).
        var uninstallManifest = await ports.GetManifestAsync(id, install.Version, ctx.RequestAborted);
        if (uninstallManifest is not null)
        {
            // Seed revoke first: it can refuse (LAST_ADMIN) before any gateway change.
            try
            {
                await ports.RevokeSeedAsync(tid, uninstallManifest, ctx.RequestAborted);
                await ports.RemoveModuleRoutesAsync(tid, uninstallManifest, ctx.RequestAborted);
            }
            catch (LifecycleRejectedException ex) { return ApiErrors.Problem(ex.Code, ex.Message, 409); }
            catch (Exception ex) { return ApiErrors.Problem(ErrorCodes.ProvisionFailed, ex.Message, 502, retryable: true); }
        }
        db.Installs.Remove(install);
        await db.SaveChangesAsync();
        await ApiHelpers.Audit(db, tid, ApiHelpers.ActorId(ctx), ApiHelpers.ActorRole(ctx), "module.uninstall", "module", id, null, ctx);
        return Results.Json(new { data = new { moduleId = id, state = "NOT_INSTALLED" } });
    }

    public static async Task<IResult> UpgradeModuleAsync(ConcurrentDictionary<string, SemaphoreSlim> installLocks, HttpContext ctx, CatalogDbContext db, ModuleLifecycleService svc, string tid, string id, string toVersion)
    {
        if (!ApiHelpers.CanWrite(ctx, tid)) return ApiErrors.Problem(ErrorCodes.Forbidden, "Not allowed.", 403);
        var (_, tenantError) = await ApiHelpers.RequireUsableTenantAsync(db, tid);
        if (tenantError is not null) return tenantError;
        if (ApiHelpers.CheckTenantHeader(ctx, tid) is { } headerError) return headerError;
        var sem = InstallLock(installLocks, tid, id);
        await sem.WaitAsync();
        await using var distLock = await Platform.Api.Services.InstallLock.AcquireAsync(db, tid, id, ctx.RequestAborted);
        try
        {
            var result = await svc.UpgradeAsync(tid, id, toVersion, ApiHelpers.ActorId(ctx), ApiHelpers.ActorRole(ctx), ctx.RequestAborted);
            return result.Ok
                ? Results.Json(new { data = new { moduleId = id, version = result.Value, state = "INSTALLED" } }, statusCode: 202)
                : ApiHelpers.MapLifecycleError(result.Error!);
        }
        finally { sem.Release(); }
    }
    public static void Map(WebApplication app, ConcurrentDictionary<string, SemaphoreSlim> installLocks)

    {
    // ---- per-tenant lifecycle (§9.1 platform view + §9.2 tenant self-service share the service) ----

    app.MapGet("/api/v1/platform/tenants/{tid}/modules", async (CatalogDbContext db, string tid) =>
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tid))
            return ApiErrors.Problem(ErrorCodes.UnknownTenant, $"Unknown tenant '{tid}'.", 404);
        var installs = await db.Installs.Where(i => i.TenantId == tid).AsNoTracking().ToListAsync();
        return Results.Json(new { data = installs });
    }).RequireAuthorization("PlatformAny");

    app.MapPost("/api/v1/platform/tenants/{tid}/modules/{id}/install",
        (HttpContext ctx, CatalogDbContext db, ModuleLifecycleService svc, string tid, string id, InstallRequest req) =>
            RunLifecycle(installLocks, ctx, db, svc, tid, id, req.Version, "install")).RequireAuthorization();

    app.MapPost("/api/v1/platform/tenants/{tid}/modules/{id}/upgrade",
        (HttpContext ctx, CatalogDbContext db, ModuleLifecycleService svc, string tid, string id, UpgradeRequest req) =>
            UpgradeModuleAsync(installLocks, ctx, db, svc, tid, id, req.ToVersion)).RequireAuthorization();

    app.MapPost("/api/v1/platform/tenants/{tid}/modules/{id}/enable",
        (CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string tid, string id) =>
            EnableModuleAsync(db, ctx, ports, tid, id)).RequireAuthorization();

    app.MapPost("/api/v1/platform/tenants/{tid}/modules/{id}/disable",
        (CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string tid, string id, DisableRequest req) =>
            DisableModuleAsync(db, ctx, ports, tid, id, req?.Reason)).RequireAuthorization();

    app.MapPost("/api/v1/platform/tenants/{tid}/modules/{id}/uninstall",
        (CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string tid, string id) =>
            UninstallModuleAsync(db, ctx, ports, tid, id)).RequireAuthorization();
    }
}

