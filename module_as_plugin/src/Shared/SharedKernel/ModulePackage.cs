using System.IO.Compression;
using System.Security.Cryptography;

namespace SharedKernel;

// Module package (zip) inspection + safe extraction — FR-004..FR-007, FR-017. Shared by the
// Platform (upload validation) and the Module Host (extraction before load) so both apply
// exactly the same rules. Layout: manifest.yml (root, required), README.md, plugin/**, web/**.
public sealed record ModulePackageOptions
{
    public long MaxPackageBytes { get; init; } = 50L * 1024 * 1024;
    public long MaxUncompressedBytes { get; init; } = 200L * 1024 * 1024;
    public int MaxEntries { get; init; } = 2000;
}

public sealed record PackageFailure(string Field, string Message);

public sealed record ModulePackageInfo(
    string ManifestYaml,
    ManifestFile Manifest,
    string Sha256,
    bool HasPluginAssembly,
    bool HasWeb,
    bool HasRemoteEntry,
    int EntryCount,
    long UncompressedBytes);

public static class ModulePackage
{
    public const string ManifestEntry = "manifest.yml";
    public const string ReadmeEntry = "README.md";
    public const string PluginRoot = "plugin/";
    public const string WebRoot = "web/";
    public const string RemoteEntry = "web/assets/remoteEntry.js";

    // Validates the zip (layout, path safety, limits, type-specific package rules) and parses
    // the manifest. Manifest semantics are left to ManifestValidator. The stream must be seekable.
    public static (ModulePackageInfo? Info, IReadOnlyList<PackageFailure> Failures) Inspect(Stream package, ModulePackageOptions? options = null)
    {
        options ??= new ModulePackageOptions();
        var failures = new List<PackageFailure>();
        if (!package.CanSeek) throw new ArgumentException("Package stream must be seekable.", nameof(package));
        if (package.Length > options.MaxPackageBytes)
            return (null, [new("package", $"Package exceeds {options.MaxPackageBytes} bytes.")]);
        if (package.Length == 0)
            return (null, [new("package", "Package is empty.")]);

        package.Position = 0;
        var sha = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant();
        package.Position = 0;

        ZipArchive zip;
        try { zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true); }
        catch (InvalidDataException) { return (null, [new("package", "Not a valid zip archive.")]); }

        using (zip)
        {
            if (zip.Entries.Count > options.MaxEntries)
                return (null, [new("package", $"Package has {zip.Entries.Count} entries (max {options.MaxEntries}).")]);

            long total = 0;
            var files = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in zip.Entries)
            {
                if (CheckEntry(entry) is { } problem) { failures.Add(new(entry.FullName, problem)); continue; }
                total += entry.Length;
                if (!entry.FullName.EndsWith('/')) files.Add(entry.FullName);
            }
            if (total > options.MaxUncompressedBytes)
                failures.Add(new("package", $"Uncompressed size {total} exceeds {options.MaxUncompressedBytes} bytes."));
            if (failures.Count > 0) return (null, failures);

            var manifestEntry = zip.GetEntry(ManifestEntry);
            if (manifestEntry is null) return (null, [new(ManifestEntry, "manifest.yml is required at the package root.")]);
            string yaml;
            using (var reader = new StreamReader(manifestEntry.Open())) yaml = reader.ReadToEnd();
            ManifestFile manifest;
            try { manifest = ManifestParser.Parse(yaml) ?? throw new InvalidDataException("empty manifest"); }
            catch (Exception ex) { return (null, [new(ManifestEntry, $"Invalid YAML: {ex.Message}")]); }

            var type = ModuleTypeNames.Normalize(manifest.Module.Type);
            var hasPluginFiles = files.Any(f => f.StartsWith(PluginRoot, StringComparison.Ordinal));
            var hasWeb = files.Any(f => f.StartsWith(WebRoot, StringComparison.Ordinal));
            var hasRemoteEntry = files.Contains(RemoteEntry);
            var assembly = manifest.Plugin?.Assembly;
            var hasAssembly = PluginModuleTypeRules.IsValidAssemblyName(assembly) && files.Contains(PluginRoot + assembly);

            if (type == ModuleTypeNames.Plugin)
            {
                if (PluginModuleTypeRules.IsValidAssemblyName(assembly) && !hasAssembly)
                    failures.Add(new(PluginRoot + assembly, $"Plugin assembly '{assembly}' named in plugin.assembly is missing from plugin/."));
                if (manifest.Frontend.Routes.Count > 0 && string.IsNullOrWhiteSpace(manifest.Frontend.RemoteUrl) && !hasRemoteEntry)
                    failures.Add(new(RemoteEntry, "Plugin with frontend routes and empty frontend.remoteUrl must ship web/assets/remoteEntry.js."));
            }
            else if (hasPluginFiles)
            {
                failures.Add(new(PluginRoot, $"plugin/ is only allowed for plugin modules (module.type is '{type}')."));
            }
            if (failures.Count > 0) return (null, failures);

            return (new ModulePackageInfo(yaml, manifest, sha, hasAssembly, hasWeb, hasRemoteEntry, files.Count, total), failures);
        }
    }

    // Extracts into targetDir (must not exist) via a sibling temp folder + move, re-checking every
    // entry and enforcing limits on the actual decompressed bytes (zip-bomb safe).
    public static void ExtractTo(string zipPath, string targetDir, ModulePackageOptions? options = null)
    {
        options ??= new ModulePackageOptions();
        var target = Path.GetFullPath(targetDir);
        if (Directory.Exists(target)) throw new IOException($"Target '{target}' already exists.");
        var temp = $"{target}.tmp-{Guid.NewGuid():N}";
        var root = temp + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(temp);
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            if (zip.Entries.Count > options.MaxEntries) throw new InvalidDataException("Too many entries.");
            long written = 0;
            var buffer = new byte[81920];
            foreach (var entry in zip.Entries)
            {
                if (CheckEntry(entry) is { } problem) throw new InvalidDataException($"{entry.FullName}: {problem}");
                var dest = Path.GetFullPath(Path.Combine(temp, entry.FullName));
                if (!dest.StartsWith(root, StringComparison.Ordinal)) throw new InvalidDataException($"{entry.FullName}: escapes the extraction folder.");
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(dest); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using var input = entry.Open();
                using var output = File.Create(dest);
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    written += read;
                    if (written > options.MaxUncompressedBytes) throw new InvalidDataException("Uncompressed size limit exceeded.");
                    output.Write(buffer, 0, read);
                }
            }
            Directory.Move(temp, target);
        }
        catch
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    public static string Sha256Hex(Stream stream) => Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

    // Path safety + allowed roots; null = OK.
    private static string? CheckEntry(ZipArchiveEntry entry)
    {
        var name = entry.FullName;
        if (string.IsNullOrEmpty(name)) return "Empty entry name.";
        if (name.Contains('\\') || name.Contains(':') || name.Contains('\0')) return "Entry name contains an illegal character.";
        if (name.StartsWith('/')) return "Absolute paths are not allowed.";
        if (name.Split('/').Any(s => s is ".." or ".")) return "Relative path segments ('.', '..') are not allowed.";
        // Unix symlink entries (st_mode S_IFLNK in the high 16 bits of external attributes).
        if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) return "Symbolic links are not allowed.";
        var allowed = name is ManifestEntry or ReadmeEntry
            || name.StartsWith(PluginRoot, StringComparison.Ordinal)
            || name.StartsWith(WebRoot, StringComparison.Ordinal);
        return allowed ? null : "Entry is outside the allowed roots (manifest.yml, README.md, plugin/, web/).";
    }
}
