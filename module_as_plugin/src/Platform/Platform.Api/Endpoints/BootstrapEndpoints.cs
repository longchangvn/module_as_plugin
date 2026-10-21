using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Models;
using Platform.Application;
using Platform.Domain;
using Platform.Infrastructure;
using SharedKernel;

namespace Platform.Api.Endpoints;

public static class BootstrapEndpoints
{
    public static void Map(WebApplication app)
    {
    // ---- frontend bootstrap (SR-6b): Vite-only payload, never the manifest (R10) ----
    app.MapGet("/api/v1/tenant/frontend-bootstrap", async (CatalogDbContext db, HttpContext ctx, PluginHostOptions pluginHost) =>
    {
        var tenantId = ctx.User.FindFirst("tenantId")?.Value;
        if (tenantId is null) return ApiErrors.Problem(ErrorCodes.ForbiddenTenant, "Tenant context required.", 403);
        if (ApiHelpers.CheckTenantHeader(ctx, tenantId) is { } bootstrapHeaderError) return bootstrapHeaderError;
        var (_, bootstrapTenantError) = await ApiHelpers.RequireUsableTenantAsync(db, tenantId);
        if (bootstrapTenantError is not null) return bootstrapTenantError;
        var installs = await db.Installs
            .Where(i => i.TenantId == tenantId && i.State == InstallState.Installed && i.Enabled)
            .AsNoTracking().ToListAsync();
        var modules = new List<object>();
        foreach (var ins in installs)
        {
            var ver = await db.ModuleVersions.AsNoTracking()
                .FirstOrDefaultAsync(v => v.ModuleId == ins.ModuleId && v.Version == ins.Version);
            if (ver is null) continue;
            ManifestFile manifest;
            try { manifest = ManifestParser.Parse(ver.ManifestYaml); }
            catch { continue; }
            // FR-022: plugin UI served by the Module Host from the package's web/ folder.
            var remoteUrl = string.IsNullOrWhiteSpace(manifest.Frontend.RemoteUrl) && ver.ModuleType == ModuleTypeNames.Plugin
                ? pluginHost.RemoteEntryUrl(ins.ModuleId, ins.Version)
                : manifest.Frontend.RemoteUrl;
            modules.Add(new
            {
                moduleId = ins.ModuleId, version = ins.Version,
                scope = manifest.Frontend.Scope, remoteUrl,
                modulePrefix = manifest.Frontend.ModulePrefix,
                routes = manifest.Frontend.Routes.Select(r => new { r.Path, r.Exposes, r.RequiredPermission, r.MenuId }),
                menus = manifest.Frontend.Menus.Select(m => new { m.Id, m.ParentId, m.Title, m.Icon, m.Route, m.RequiredPermission, m.Order }),
                uiPermissions = manifest.Frontend.UiPermissions,
            });
        }
        ctx.Response.Headers.CacheControl = "private, max-age=30";
        return Results.Json(new { data = new { tenantId, modules } });
    }).RequireAuthorization();
    }
}
