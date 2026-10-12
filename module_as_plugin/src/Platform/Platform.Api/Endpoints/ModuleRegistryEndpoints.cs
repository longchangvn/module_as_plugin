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
    app.MapGet("/api/v1/platform/modules", async (CatalogDbContext db) =>
    {
        var modules = await db.Modules.AsNoTracking().ToListAsync();
        var versions = await db.ModuleVersions.AsNoTracking().ToListAsync();
        return Results.Json(new
        {
            data = modules.Select(m => new
            {
                m.Id, m.DisplayName, m.Description, m.Owner, m.Status,
                latestVersion = versions.Where(v => v.ModuleId == m.Id).OrderByDescending(v => v.Version).FirstOrDefault()?.Version,
            }),
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
}
