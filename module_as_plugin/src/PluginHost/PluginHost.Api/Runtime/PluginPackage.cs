using SharedKernel;

namespace PluginHost.Api.Runtime;

// An extracted, verified package ready to load: ContentRoot = {CacheRoot}/{moduleId}/{version}
// (contains plugin/ and optionally web/). Produced by an IPluginPackageSource.
public sealed record PluginPackage(
    string ModuleId,
    string Version,
    string PackageSha256,
    string ContentRoot,
    string Assembly,
    string? EntryType,
    string Contract)
{
    public const string MarkerFile = ".sha256";

    // Reads manifest.yml of an extracted package folder; manifest problems are load failures.
    public static PluginPackage FromDirectory(string moduleId, string version, string sha256, string contentRoot)
    {
        var manifestPath = Path.Combine(contentRoot, ModulePackage.ManifestEntry);
        if (!File.Exists(manifestPath))
            throw new PluginLoadException($"{moduleId}@{version}: manifest.yml missing from the extracted package.");
        ManifestFile manifest;
        try { manifest = ManifestParser.Parse(File.ReadAllText(manifestPath)) ?? throw new InvalidDataException("empty manifest"); }
        catch (Exception ex) when (ex is not PluginLoadException)
        {
            throw new PluginLoadException($"{moduleId}@{version}: invalid manifest.yml ({ex.Message}).", ex);
        }
        if (!string.IsNullOrWhiteSpace(manifest.Module.Name) && !string.Equals(manifest.Module.Name, moduleId, StringComparison.OrdinalIgnoreCase))
            throw new PluginLoadException($"Package manifest is for module '{manifest.Module.Name}', expected '{moduleId}'.");
        if (!string.IsNullOrWhiteSpace(manifest.Module.Version) && !string.Equals(manifest.Module.Version, version, StringComparison.OrdinalIgnoreCase))
            throw new PluginLoadException($"Package manifest is version '{manifest.Module.Version}', expected '{version}'.");
        if (ModuleTypeNames.Normalize(manifest.Module.Type) != ModuleTypeNames.Plugin || manifest.Plugin is null)
            throw new PluginLoadException($"{moduleId}@{version} is not a plugin module (module.type / plugin section).");
        return new PluginPackage(moduleId, version, sha256, contentRoot, manifest.Plugin.Assembly,
            string.IsNullOrWhiteSpace(manifest.Plugin.EntryType) ? null : manifest.Plugin.EntryType.Trim(),
            string.IsNullOrWhiteSpace(manifest.Plugin.Contract) ? PluginContractDefault : manifest.Plugin.Contract.Trim());
    }

    private const string PluginContractDefault = "1.0";
}

// Provides extracted packages (Platform download in production, a folder in tests).
public interface IPluginPackageSource
{
    Task<PluginPackage> GetAsync(string moduleId, string version, string packageSha256, CancellationToken ct);
}

// Load/contract/hash failure → 422 PLUGIN_LOAD_FAILED (tenant shell unchanged).
public sealed class PluginLoadException(string message, Exception? inner = null) : Exception(message, inner);

// Package could not be fetched from the Platform → 502 PLUGIN_HOST_UNAVAILABLE.
public sealed class PluginPackageUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
