namespace Platform.Api.Services;

// Immutable package files under ModulePackages:Root. Each upload gets a unique file
// ({id}/{version}/{sha256}-{nonce}.zip) so a losing concurrent upload of the same version only
// deletes its own file (analysis A1). Paths are always resolved under the root.
public sealed class ModulePackageStore
{
    private readonly string _root;

    public ModulePackageStore(IConfiguration cfg, IWebHostEnvironment env)
    {
        _root = Path.GetFullPath(cfg["ModulePackages:Root"] ?? "./module-packages", env.ContentRootPath);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(string moduleId, string version, string sha256, Stream content, CancellationToken ct)
    {
        var relative = Path.Combine(moduleId, version, $"{sha256}-{Guid.NewGuid():N}.zip");
        var full = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + ".tmp";
        content.Position = 0;
        await using (var file = File.Create(temp)) await content.CopyToAsync(file, ct);
        File.Move(temp, full);
        return relative.Replace('\\', '/');
    }

    public Stream? OpenRead(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var full = Resolve(relativePath);
        return File.Exists(full) ? File.OpenRead(full) : null;
    }

    public void Delete(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;
        var full = Resolve(relativePath);
        if (File.Exists(full)) File.Delete(full);
    }

    private string Resolve(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        return full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? full
            : throw new InvalidOperationException("Package path escapes the package root.");
    }
}
