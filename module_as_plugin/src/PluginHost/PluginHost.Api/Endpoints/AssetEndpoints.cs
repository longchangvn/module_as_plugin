using Microsoft.AspNetCore.StaticFiles;
using PluginHost.Api.Runtime;

namespace PluginHost.Api.Endpoints;

// Plugin frontends (research R11): GET /plugin-assets/{moduleId}/{version}/{**path} serves the
// package's web/ folder of a cached version. Anonymous, CORS *, immutable caching except the
// federation entry (remoteEntry.js) which must be revalidated.
public static class AssetEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/plugin-assets/{moduleId}/{version}/{**path}", (PluginHostOptions options, HttpContext ctx, string moduleId, string version, string? path) =>
        {
            ctx.Response.Headers.AccessControlAllowOrigin = "*";
            var file = ResolveAssetPath(options.CacheRoot, moduleId, version, path);
            if (file is null) return Results.NotFound();
            if (!ContentTypes.TryGetContentType(file, out var contentType)) contentType = "application/octet-stream";
            ctx.Response.Headers.CacheControl = string.Equals(Path.GetFileName(file), "remoteEntry.js", StringComparison.OrdinalIgnoreCase)
                ? "no-cache"
                : "public, max-age=31536000, immutable";
            return Results.File(file, contentType);
        }).AllowAnonymous();
    }

    // Absolute path of an existing file under {cacheRoot}/{moduleId}/{version}/web/, or null when
    // the version is not cached (no verified marker), the file is missing or the path escapes.
    public static string? ResolveAssetPath(string cacheRoot, string moduleId, string version, string? path)
    {
        if (!PluginIds.IsValidId(moduleId) || !PluginIds.IsValidVersion(version) || string.IsNullOrWhiteSpace(path)) return null;
        if (path.Contains('\\') || path.Contains('\0')) return null;
        var versionRoot = Path.GetFullPath(Path.Combine(cacheRoot, moduleId, version));
        if (!File.Exists(Path.Combine(versionRoot, PluginPackage.MarkerFile))) return null;
        var webRoot = Path.Combine(versionRoot, "web") + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(webRoot, path));
        return full.StartsWith(webRoot, StringComparison.Ordinal) && File.Exists(full) ? full : null;
    }
}
