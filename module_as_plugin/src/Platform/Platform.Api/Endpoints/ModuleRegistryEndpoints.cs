using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Models;
using Platform.Application;
using Platform.Domain;
using Platform.Infrastructure;
using SharedKernel;

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
namespace Platform.Api.Endpoints;

public static class ModuleRegistryEndpoints
{
    public static void Map(WebApplication app)
    {
    // ---- module registry (§9.1) ----
    // Global registry view (P-BR5): versions newest-first (semver precedence, not
    // string order), dependencies of the latest version, tenant install count.
    app.MapGet("/api/v1/platform/modules", async (CatalogDbContext db) =>
    {
        var modules = await db.Modules.AsNoTracking().OrderBy(m => m.DisplayName).ToListAsync();
        var versions = await db.ModuleVersions.AsNoTracking()
            .Select(v => new { v.ModuleId, v.Version, v.ManifestYaml }).ToListAsync();
        var installCounts = await db.Installs.AsNoTracking()
            .Where(i => i.State != InstallState.NotInstalled)
            .GroupBy(i => i.ModuleId).Select(g => new { ModuleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ModuleId, x => x.Count);
        return Results.Json(new
        {
            data = modules.Select(m =>
            {
                var own = versions.Where(v => v.ModuleId == m.Id).ToList();
                var ordered = SemverOrdering.Descending(own.Select(v => v.Version));
                var latest = ordered.FirstOrDefault();
                return new
                {
                    m.Id, m.DisplayName, m.Description, m.Owner, m.Icon, m.Category,
                    status = m.Status.ToString(),
                    latestVersion = latest,
                    versions = ordered,
                    dependencies = latest is null ? Array.Empty<object>() : LatestDependencies(own.First(v => v.Version == latest).ManifestYaml),
                    installedTenantCount = installCounts.GetValueOrDefault(m.Id),
                };
            }),
        });
    }).RequireAuthorization("PlatformAny");

    // Per-module install states across tenants (multi-tenant install dialog).
    app.MapGet("/api/v1/platform/modules/{id}/installs", async (CatalogDbContext db, string id) =>
    {
        if (!await db.Modules.AnyAsync(m => m.Id == id))
            return ApiErrors.Problem(ErrorCodes.UnknownModule, $"Unknown module '{id}'.", 404);
        var rows = await (
            from i in db.Installs.AsNoTracking()
            join t in db.Tenants.AsNoTracking() on i.TenantId equals t.Id
            where i.ModuleId == id && i.State != InstallState.NotInstalled
            orderby t.Name
            select new { i.TenantId, TenantName = t.Name, i.State, i.Version, i.Enabled }).ToListAsync();
        return Results.Json(new
        {
            data = rows.Select(r => new
            {
                r.TenantId, r.TenantName, state = InstallStateLabel(r.State), r.Version, r.Enabled,
            }),
        });
    }).RequireAuthorization("PlatformAny");

    // §9.1 registry detail: module + immutable versions[] (manifest hash, artifacts, publisher).
    app.MapGet("/api/v1/platform/modules/{id}", async (CatalogDbContext db, HttpContext ctx, string id) =>
    {
        var module = await db.Modules.FindAsync(id);
        if (module is null) return ApiErrors.Problem(ErrorCodes.UnknownModule, $"Unknown module '{id}'.", 404);
        var versions = await db.ModuleVersions.AsNoTracking()
            .Where(v => v.ModuleId == id)
            .OrderByDescending(v => v.Version)
            .Select(v => new
            {
                v.Id, v.Version, v.ManifestHash, v.ArtifactApiUrl, v.ArtifactRemoteUrl,
                v.PublishedBy, v.PublishedAt,
            }).ToListAsync();
        return Results.Json(new
        {
            data = new
            {
                module.Id, module.DisplayName, module.Description, module.Owner,
                module.Icon, module.Category, status = module.Status.ToString(),
                module.CreatedAt, module.UpdatedAt,
                versions,
            },
        });
    }).RequireAuthorization("PlatformAny");

    app.MapPost("/api/v1/platform/modules", async (CatalogDbContext db, HttpContext ctx, PublishModuleRequest req, IWebHostEnvironment env) =>
    {
        if (ApiHelpers.RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Module publish requires Admin.", 403);
        ManifestFile manifest;
        try { manifest = ManifestParser.Parse(req.ManifestYaml); }
        catch (Exception ex) { return ApiErrors.Problem(ErrorCodes.ValidationError, $"Invalid YAML: {ex.Message}", 400); }

        var usedScopes = new HashSet<string>();
        // collect scopes from stored manifests (best-effort; full parse per version),
        // excluding this module's own versions (re-publish with same scope is allowed).
        foreach (var v in await db.ModuleVersions.AsNoTracking().Where(v => v.ModuleId != manifest.Module.Name).ToListAsync())
        {
            try { usedScopes.Add(ManifestParser.Parse(v.ManifestYaml).Frontend.Scope); } catch { /* ignore legacy rows */ }
        }
        var failures = ManifestValidator.Validate(manifest, usedScopes, env.IsDevelopment());
        if (failures.Count > 0)
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Manifest validation failed.",
                400, failures.Select(f => new { field = f.Field, message = f.Message }));

        // circular dependency across registry: publish rejected (edge case 4)
        if (manifest.Dependencies.Any(d => string.Equals(d.Name, manifest.Module.Name, StringComparison.OrdinalIgnoreCase)))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Circular dependency: module depends on itself.", 400);

        var module = await db.Modules.FindAsync(manifest.Module.Name);
        if (module is null)
        {
            module = new PlatformModule
            {
                Id = manifest.Module.Name, DisplayName = manifest.Module.DisplayName,
                Description = manifest.Module.Description, Owner = manifest.Module.Owner,
                Icon = manifest.Module.Icon, Category = manifest.Module.Category,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.Modules.Add(module);
        }
        if (await db.ModuleVersions.AnyAsync(v => v.ModuleId == module.Id && v.Version == manifest.Module.Version))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Version already published (immutable).", 400,
                new { field = "module.version" });

        using var sha = SHA256.Create();
        db.ModuleVersions.Add(new ModuleVersion
        {
            Id = Cuid.New("mv"), ModuleId = module.Id, Version = manifest.Module.Version,
            ManifestYaml = req.ManifestYaml,
            ManifestHash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(req.ManifestYaml))).ToLowerInvariant(),
            ArtifactApiUrl = req.ArtifactApiUrl, ArtifactRemoteUrl = req.ArtifactRemoteUrl,
            PublishedBy = ApiHelpers.ActorId(ctx), PublishedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        await ApiHelpers.Audit(db, null, ApiHelpers.ActorId(ctx), ApiHelpers.RequireRole(ctx), "module.publish", "module", $"{module.Id}@{manifest.Module.Version}", null, ctx);
        return Results.Json(new { data = new { id = module.Id, version = manifest.Module.Version } }, statusCode: 201);
    }).RequireAuthorization("PlatformAny");

    app.MapPost("/api/v1/platform/modules/{id}/deprecate", async (CatalogDbContext db, HttpContext ctx, string id) =>
    {
        if (ApiHelpers.RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Deprecate requires Admin.", 403);
        var module = await db.Modules.FindAsync(id);
        if (module is null) return ApiErrors.Problem(ErrorCodes.UnknownModule, $"Unknown module '{id}'.", 404);
        module.Status = ModuleStatus.Deprecated;
        module.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await ApiHelpers.Audit(db, null, ApiHelpers.ActorId(ctx), ApiHelpers.RequireRole(ctx), "module.deprecate", "module", id, null, ctx);
        return Results.Json(new { data = module });
    }).RequireAuthorization("PlatformAny");
    }

    internal static string InstallStateLabel(InstallState state) => state switch
    {
        InstallState.NotInstalled => "NOT_INSTALLED",
        InstallState.Installing => "INSTALLING",
        InstallState.Installed => "INSTALLED",
        InstallState.Disabled => "DISABLED",
        InstallState.Failed => "FAILED",
        InstallState.Uninstalling => "UNINSTALLING",
        _ => state.ToString().ToUpperInvariant(),
    };

    private static object[] LatestDependencies(string manifestYaml)
    {
        try
        {
            return ManifestParser.Parse(manifestYaml).Dependencies
                .Select(d => (object)new { d.Name, d.VersionRange }).ToArray();
        }
        catch { return []; }
    }
}
