using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Models;
using Platform.Api.Services;
using Platform.Application;
using Platform.Domain;
using Platform.Infrastructure;
using SharedKernel;

using System.Text.Json;
namespace Platform.Api.Endpoints;

public static class InternalEndpoints
{
    public static void Map(WebApplication app)
    {
    // Cluster-internal tenant resolution by human identifier (slug or name,
    // case-insensitive) for login UX — callers that only know the tenant name
    // resolve it to the opaque id first, then use the id everywhere else.
    // Distinct `:resolve` path (never collides with {id} routes).
    app.MapGet("/internal/tenants:resolve", async (CatalogDbContext db, string? slug, string? name) =>
    {
        Tenant? t = null;
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var s = slug.Trim().ToLowerInvariant();
            t = await db.Tenants.FirstOrDefaultAsync(x => x.Slug.ToLower() == s);
        }
        t ??= !string.IsNullOrWhiteSpace(name)
            ? await db.Tenants.FirstOrDefaultAsync(x => x.Name.ToLower() == name.Trim().ToLower())
            : null;
        return t is null
            ? Results.NotFound()
            : Results.Json(new { data = new
            {
                id = t.Id, status = t.Status.ToString(), dbStatus = t.DbStatus.ToString(),
                deploymentMode = t.DeploymentMode, dbName = t.DbName, schema = t.Schema,
                hasCustomConnection = t.HasCustomConnection,
                bootstrapAdminEmail = t.BootstrapAdminEmail, bootstrapAdminName = t.BootstrapAdminName,
            } });
    });

    app.MapGet("/internal/tenants/{id}/status", async (CatalogDbContext db, string id) =>
    {
        var t = await db.Tenants.FindAsync(id);
        return t is null
            ? Results.NotFound()
            : Results.Json(new { data = new
            {
                id = t.Id, status = t.Status.ToString(), dbStatus = t.DbStatus.ToString(),
                deploymentMode = t.DeploymentMode, dbName = t.DbName, schema = t.Schema,
                hasCustomConnection = t.HasCustomConnection,
                bootstrapAdminEmail = t.BootstrapAdminEmail, bootstrapAdminName = t.BootstrapAdminName,
            } });
    });

    // Cluster-internal custom connection hand-off to Auth. Decrypts server-side
    // and returns the plaintext connection string. Requires the shared
    // X-Internal-Key (InternalApi:Key from env — never the DB, never browsers).
    // NOT routed by the gateway.
    app.MapGet("/internal/tenants/{id}/connection", async (
        CatalogDbContext db, HttpContext ctx, IConfiguration cfg, TenantConnectionProtector protector, string id) =>
    {
        if (!IsInternalCaller(ctx, cfg))
            return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid internal key.", 401);
        var t = await db.Tenants.FindAsync(id);
        if (t is null)
            return ApiErrors.Problem(ErrorCodes.UnknownTenant, $"Unknown tenant '{id}'.", 404);
        if (!t.HasCustomConnection)
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Tenant has no custom connection.", 404);
        var plain = protector.TryUnprotect(t.DbConnectionEncrypted);
        if (plain is null)
            return ApiErrors.Problem(ErrorCodes.ProvisionFailed, "Stored connection cannot be decrypted.", 502, retryable: false);
        return Results.Json(new { data = new { tenantId = t.Id, connectionString = plain } });
    });
    app.MapPost("/internal/tenants/{id}/provisioned", async (CatalogDbContext db, string id, JsonElement body) =>
    {
        var t = await db.Tenants.FindAsync(id);
        if (t is null) return Results.NotFound();
        t.DbStatus = TenantDbStatus.Ready;
        t.SchemaVersion = ApiHelpers.JsonString(body, "schemaVersion");
        if (t.Status is TenantStatus.Provisioning or TenantStatus.Failed) t.Status = TenantStatus.Active;
        t.DbError = null;
        t.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.Json(new { data = new { id = t.Id, status = t.Status.ToString(), dbStatus = t.DbStatus.ToString() } });
    });

    app.MapPost("/internal/tenants/{id}/provision-failed", async (CatalogDbContext db, string id, JsonElement body) =>
    {
        var t = await db.Tenants.FindAsync(id);
        if (t is null) return Results.NotFound();
        t.DbStatus = TenantDbStatus.Failed;
        t.Status = TenantStatus.Failed;
        t.DbError = ApiHelpers.JsonString(body, "error") ?? "Unknown provisioning error.";
        t.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.Json(new { data = new { id = t.Id, status = t.Status.ToString(), dbStatus = t.DbStatus.ToString() } });
    });

    // Gateway reconcile source (R-GW1, G4/P2): per-tenant module state from the
    // catalog. A tenant entry exists while the install row exists; enabled only
    // when Installed/Installing (upgrade) and Enabled. X-Internal-Key required.
    // NOT routed by the gateway.
    app.MapGet("/internal/gateway/module-states", async (CatalogDbContext db, HttpContext ctx, IConfiguration cfg, ModuleHostingCoordinator hosting) =>
    {
        if (!IsInternalCaller(ctx, cfg))
            return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid internal key.", 401);
        var installs = await db.Installs.AsNoTracking().Where(i => i.State != InstallState.NotInstalled).ToListAsync();
        var keys = installs.Select(i => i.ModuleId + "@" + i.Version).Distinct().ToList();
        var versions = await db.ModuleVersions.AsNoTracking()
            .Where(v => keys.Contains(v.ModuleId + "@" + v.Version)).ToListAsync();
        var modules = installs.GroupBy(i => i.ModuleId, StringComparer.OrdinalIgnoreCase).Select(g => new
        {
            moduleId = g.Key,
            versions = versions.Where(v => v.ModuleId.Equals(g.Key, StringComparison.OrdinalIgnoreCase)).Select(v =>
            {
                ManifestFile? m = null;
                try { m = ManifestParser.Parse(v.ManifestYaml); } catch { /* unparseable manifest → no routes */ }
                // Target per hosting strategy (remote: artifact URL; plugin: Module Host).
                var target = m is null ? null : hosting.Target(m, EfLifecyclePorts.ToRef(v));
                return new
                {
                    version = v.Version, baseUrl = target?.BaseUrl ?? v.ArtifactApiUrl,
                    upstreams = target?.Upstreams ?? Array.Empty<GatewayUpstream>(),
                };
            }).ToList(),
            tenants = g.Select(i => new
            {
                tenantId = i.TenantId, version = i.Version,
                enabled = i.Enabled && (i.State is InstallState.Installed or InstallState.Installing),
            }).ToList(),
        }).ToList();
        return Results.Json(new { data = new { modules } });
    });

    // Module Host package download (FR-007/FR-017): the host verifies X-Package-Sha256 against
    // the value pushed with the activation before loading. NOT routed by the gateway.
    app.MapGet("/internal/modules/{id}/versions/{version}/package", async (
        CatalogDbContext db, HttpContext ctx, IConfiguration cfg, ModulePackageStore store, string id, string version) =>
    {
        if (!IsInternalCaller(ctx, cfg))
            return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid internal key.", 401);
        var v = await db.ModuleVersions.AsNoTracking().FirstOrDefaultAsync(x => x.ModuleId == id && x.Version == version);
        var stream = v is null ? null : store.OpenRead(v.PackagePath);
        if (stream is null)
            return ApiErrors.Problem(ErrorCodes.UnknownVersion, $"No package for '{id}@{version}'.", 404);
        ctx.Response.Headers["X-Package-Sha256"] = v!.PackageSha256;
        return Results.Stream(stream, "application/zip", $"{id}-{version}.zip");
    });

    // Module Host reconcile source (FR-021): every (plugin module, version) used by an install
    // row, with tenants; enabled rule identical to the gateway snapshot. assets = hybrid versions
    // in use whose UI the host must keep cached. NOT routed by the gateway.
    app.MapGet("/internal/plugin-host/states", async (CatalogDbContext db, HttpContext ctx, IConfiguration cfg) =>
    {
        if (!IsInternalCaller(ctx, cfg))
            return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid internal key.", 401);
        var rows = await (
            from i in db.Installs.AsNoTracking()
            join v in db.ModuleVersions.AsNoTracking() on new { i.ModuleId, i.Version } equals new { v.ModuleId, v.Version }
            where i.State != InstallState.NotInstalled
                && (v.ModuleType == ModuleTypeNames.Plugin || v.ModuleType == ModuleTypeNames.Hybrid)
            select new { i.ModuleId, i.Version, v.PackageSha256, v.ModuleType, i.TenantId, i.Enabled, i.State }).ToListAsync();
        // Hybrid versions only need their UI assets cached (no tenant shell, no assembly load).
        var assets = rows.Where(r => r.ModuleType == ModuleTypeNames.Hybrid && !string.IsNullOrWhiteSpace(r.PackageSha256))
            .Select(r => new { moduleId = r.ModuleId, version = r.Version, packageSha256 = r.PackageSha256 })
            .Distinct().ToList();
        var plugins = rows.Where(r => r.ModuleType == ModuleTypeNames.Plugin).GroupBy(r => new { r.ModuleId, r.Version, r.PackageSha256 }).Select(g => new
        {
            moduleId = g.Key.ModuleId, version = g.Key.Version, packageSha256 = g.Key.PackageSha256,
            tenants = g.Select(r => new
            {
                tenantId = r.TenantId,
                enabled = r.Enabled && (r.State is InstallState.Installed or InstallState.Installing),
            }).ToList(),
        }).ToList();
        return Results.Json(new { data = new { plugins, assets } });
    });
    }

    private static bool IsInternalCaller(HttpContext ctx, IConfiguration cfg)
    {
        var expected = cfg["InternalApi:Key"];
        var presented = ctx.Request.Headers["X-Internal-Key"].FirstOrDefault() ?? "";
        return !string.IsNullOrWhiteSpace(expected) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(presented));
    }
}
