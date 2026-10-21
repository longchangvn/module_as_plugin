using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Models;
using Platform.Api.Services;
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
            .Select(v => new { v.ModuleId, v.Version, v.ManifestYaml, v.ModuleType }).ToListAsync();
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
                    type = latest is null ? ModuleTypeNames.Remote : own.First(v => v.Version == latest).ModuleType,
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
                v.PublishedBy, v.PublishedAt, type = v.ModuleType, v.PackageSha256,
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

    app.MapPost("/api/v1/platform/modules", async (CatalogDbContext db, HttpContext ctx, PublishModuleRequest req, IWebHostEnvironment env, ModulePackageStore store) =>
    {
        if (ApiHelpers.RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Module publish requires Admin.", 403);
        ManifestFile manifest;
        try { manifest = ManifestParser.Parse(req.ManifestYaml); }
        catch (Exception ex) { return ApiErrors.Problem(ErrorCodes.ValidationError, $"Invalid YAML: {ex.Message}", 400); }
        if (ModuleTypeNames.Normalize(manifest.Module.Type) == ModuleTypeNames.Plugin)
            return ApiErrors.Problem(ErrorCodes.ValidationError,
                "Plugin modules must be registered by package upload (POST /api/v1/platform/modules/package).", 400,
                new[] { new { field = "module.type", message = "Use package upload for plugin modules." } });
        return await PublishVersionAsync(db, ctx, env, store, req.ManifestYaml, manifest, req.ArtifactApiUrl, req.ArtifactRemoteUrl, null, null);
    }).RequireAuthorization("PlatformAny");

    // FR-004..FR-009: register a version from a zip package (remote or plugin). The module type
    // comes from the packaged manifest; type-specific package rules live in ModulePackage.
    app.MapPost("/api/v1/platform/modules/package", async (CatalogDbContext db, HttpContext ctx, IWebHostEnvironment env,
        ModulePackageStore store, ModulePackageOptions options) =>
    {
        if (ApiHelpers.RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Module publish requires Admin.", 403);
        if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = options.MaxPackageBytes + 1024 * 1024;
        if (!ctx.Request.HasFormContentType)
            return ApiErrors.Problem(ErrorCodes.PackageInvalid, "Expected multipart/form-data with a 'package' file.", 400,
                new[] { new { field = "package", message = "Required." } });
        IFormCollection form;
        try { form = await ctx.Request.ReadFormAsync(ctx.RequestAborted); }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return ApiErrors.Problem(ErrorCodes.PackageInvalid, $"Package exceeds {options.MaxPackageBytes} bytes.", 413);
        }
        catch (InvalidDataException ex) { return ApiErrors.Problem(ErrorCodes.PackageInvalid, ex.Message, 413); }
        var file = form.Files.GetFile("package");
        if (file is null || file.Length == 0)
            return ApiErrors.Problem(ErrorCodes.PackageInvalid, "A non-empty 'package' file is required.", 400,
                new[] { new { field = "package", message = "Required." } });
        if (file.Length > options.MaxPackageBytes)
            return ApiErrors.Problem(ErrorCodes.PackageInvalid, $"Package exceeds {options.MaxPackageBytes} bytes.", 413);

        await using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ctx.RequestAborted);
        var (info, problems) = ModulePackage.Inspect(buffer, options);
        if (info is null)
            return ApiErrors.Problem(ErrorCodes.PackageInvalid, "Package validation failed.", 400,
                problems.Select(p => new { field = p.Field, message = p.Message }));
        return await PublishVersionAsync(db, ctx, env, store, info.ManifestYaml, info.Manifest,
            NullIfBlank(form["artifactApiUrl"]), NullIfBlank(form["artifactRemoteUrl"]), info, buffer);
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

    // Shared by JSON and package publish: validation (incl. type rules), immutability, storage,
    // audit. The package file is written only after validation and removed again if the insert
    // loses a concurrent race on the unique (ModuleId, Version) index.
    private static async Task<IResult> PublishVersionAsync(CatalogDbContext db, HttpContext ctx, IWebHostEnvironment env,
        ModulePackageStore store, string manifestYaml, ManifestFile manifest, string? artifactApiUrl, string? artifactRemoteUrl,
        ModulePackageInfo? package, Stream? packageContent)
    {
        var usedScopes = new HashSet<string>();
        // collect scopes from stored manifests (best-effort; full parse per version),
        // excluding this module's own versions (re-publish with same scope is allowed).
        foreach (var v in await db.ModuleVersions.AsNoTracking().Where(v => v.ModuleId != manifest.Module.Name).ToListAsync())
        {
            try { usedScopes.Add(ManifestParser.Parse(v.ManifestYaml).Frontend.Scope); } catch { /* ignore legacy rows */ }
        }
        var failures = ManifestValidator.Validate(manifest, usedScopes, env.IsDevelopment()).ToList();
        var type = ModuleTypeNames.Normalize(manifest.Module.Type);
        if (type == ModuleTypeNames.Remote)
        {
            // FR-026: manifest service.baseUrl is the default artifact URL; FR-025 transport rule.
            artifactApiUrl ??= NullIfBlank(manifest.Service.BaseUrl);
            if (RemoteModuleTypeRules.ValidateArtifactUrl(manifest, artifactApiUrl, env.IsDevelopment()) is { } urlError)
                failures.Add(new("artifactApiUrl", urlError));
        }
        else
        {
            artifactApiUrl = null; // hosted by the platform; the strategy supplies the target
        }
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
            return DuplicateVersion();

        string? packagePath = null;
        if (package is not null && packageContent is not null)
            packagePath = await store.SaveAsync(module.Id, manifest.Module.Version, package.Sha256, packageContent, ctx.RequestAborted);
        db.ModuleVersions.Add(new ModuleVersion
        {
            Id = Cuid.New("mv"), ModuleId = module.Id, Version = manifest.Module.Version,
            ManifestYaml = manifestYaml,
            ManifestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifestYaml))).ToLowerInvariant(),
            ArtifactApiUrl = artifactApiUrl, ArtifactRemoteUrl = artifactRemoteUrl,
            ModuleType = type, PackageSha256 = package?.Sha256, PackagePath = packagePath,
            PublishedBy = ApiHelpers.ActorId(ctx), PublishedAt = DateTimeOffset.UtcNow,
        });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            store.Delete(packagePath);
            if (await db.ModuleVersions.AsNoTracking().AnyAsync(v => v.ModuleId == module.Id && v.Version == manifest.Module.Version))
                return DuplicateVersion();
            throw;
        }
        await ApiHelpers.Audit(db, null, ApiHelpers.ActorId(ctx), ApiHelpers.RequireRole(ctx), "module.publish", "module",
            $"{module.Id}@{manifest.Module.Version}", package is null ? null : $"type={type} package={package.Sha256}", ctx);
        return Results.Json(new
        {
            data = new { id = module.Id, version = manifest.Module.Version, type, packageSha256 = package?.Sha256 },
        }, statusCode: 201);

        static IResult DuplicateVersion() =>
            ApiErrors.Problem(ErrorCodes.ValidationError, "Version already published (immutable).", 400, new { field = "module.version" });
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

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
