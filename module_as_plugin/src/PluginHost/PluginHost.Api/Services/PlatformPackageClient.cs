using PluginHost.Api.Runtime;
using SharedKernel;

namespace PluginHost.Api.Services;

// Package source backed by the Platform (research R9): GET /internal/modules/{id}/versions/{v}/package
// with X-Internal-Key, verify SHA-256 against the catalog value, extract with the shared safe
// extractor into {CacheRoot}/{id}/{version} and mark the folder with the verified hash.
// A folder whose marker matches is reused (restart / second tenant needs no download).
public sealed class PlatformPackageClient(
    IHttpClientFactory http, IConfiguration cfg, PluginHostOptions options, ILogger<PlatformPackageClient> log)
    : IPluginPackageSource, IModuleAssetSource
{
    public const string HttpClientName = "platform-internal";
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<PluginPackage> GetAsync(string moduleId, string version, string packageSha256, CancellationToken ct)
    {
        var (target, sha) = await EnsureExtractedAsync(moduleId, version, packageSha256, ct);
        return PluginPackage.FromDirectory(moduleId, version, sha, target);
    }

    public async Task EnsureAssetsAsync(string moduleId, string version, string packageSha256, CancellationToken ct)
    {
        var (target, _) = await EnsureExtractedAsync(moduleId, version, packageSha256, ct);
        PluginPackage.ReadManifest(moduleId, version, target);
    }

    // Extracted + hash-verified package folder {CacheRoot}/{id}/{version} (downloaded when missing).
    private async Task<(string Target, string Sha)> EnsureExtractedAsync(string moduleId, string version, string packageSha256, CancellationToken ct)
    {
        if (!PluginIds.IsValidId(moduleId) || !PluginIds.IsValidVersion(version))
            throw new PluginLoadException($"Invalid module id or version '{moduleId}@{version}'.");
        if (!PluginIds.IsValidSha256(packageSha256))
            throw new PluginLoadException("packageSha256 must be a 64-character hex SHA-256.");
        var expected = packageSha256.ToLowerInvariant();
        var target = Path.Combine(options.CacheRoot, moduleId, version);

        await gate.WaitAsync(ct);
        try
        {
            if (ReadMarker(target) == expected) return (target, expected);

            var downloads = Path.Combine(options.CacheRoot, ".downloads");
            Directory.CreateDirectory(downloads);
            var zipPath = Path.Combine(downloads, $"{moduleId}-{version}-{Guid.NewGuid():N}.zip");
            try
            {
                await DownloadAsync(moduleId, version, zipPath, ct);
                string actual;
                await using (var zip = File.OpenRead(zipPath)) actual = ModulePackage.Sha256Hex(zip);
                if (actual != expected)
                    throw new PluginLoadException($"Package hash mismatch for {moduleId}@{version} (expected {expected}, got {actual}).");
                ExtractReplacing(zipPath, target, expected);
            }
            finally
            {
                TryDelete(() => File.Delete(zipPath));
            }
            log.LogInformation("plugin.package module={module} version={version} sha256={sha} result=extracted", moduleId, version, expected);
            return (target, expected);
        }
        finally { gate.Release(); }
    }

    private async Task DownloadAsync(string moduleId, string version, string zipPath, CancellationToken ct)
    {
        var baseUrl = (cfg["Platform:BaseUrl"] ?? "http://localhost:5121").TrimEnd('/');
        var url = $"{baseUrl}/internal/modules/{Uri.EscapeDataString(moduleId)}/versions/{Uri.EscapeDataString(version)}/package";
        try
        {
            using var client = http.CreateClient(HttpClientName);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("X-Internal-Key", cfg["InternalApi:Key"]);
            using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode)
                throw new PluginPackageUnavailableException($"Platform returned {(int)res.StatusCode} for package {moduleId}@{version}.");
            await using var body = await res.Content.ReadAsStreamAsync(ct);
            await using var file = File.Create(zipPath);
            await body.CopyToAsync(file, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException && !ct.IsCancellationRequested)
        {
            throw new PluginPackageUnavailableException($"Package download for {moduleId}@{version} failed: {ex.Message}", ex);
        }
    }

    // Extract next to the target, then swap folders, so a crash never leaves a half-written
    // folder behind a valid marker. The marker is written before the folder becomes visible.
    private static void ExtractReplacing(string zipPath, string target, string sha)
    {
        var staging = $"{target}.new-{Guid.NewGuid():N}";
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            ModulePackage.ExtractTo(zipPath, staging);
        }
        catch (InvalidDataException ex)
        {
            throw new PluginLoadException($"Package extraction failed: {ex.Message}", ex);
        }
        File.WriteAllText(Path.Combine(staging, PluginPackage.MarkerFile), sha);
        if (Directory.Exists(target))
        {
            var old = $"{target}.old-{Guid.NewGuid():N}";
            Directory.Move(target, old);
            TryDelete(() => Directory.Delete(old, recursive: true));
        }
        Directory.Move(staging, target);
    }

    public static string? ReadMarker(string folder)
    {
        var marker = Path.Combine(folder, PluginPackage.MarkerFile);
        return File.Exists(marker) ? File.ReadAllText(marker).Trim().ToLowerInvariant() : null;
    }

    private static void TryDelete(Action delete)
    {
        try { delete(); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
