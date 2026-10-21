using System.IO.Compression;
using System.Text;
using Xunit;

namespace SharedKernel.Tests;

// Package inspection + safe extraction (specs/002-module-hosting-types FR-004..FR-007, SC-005).
public sealed class ModulePackageTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), $"pkg-tests-{Guid.NewGuid():N}");

    public ModulePackageTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }

    private const string PluginManifest = """
        manifestVersion: "1.0"
        module:
          name: sample3
          type: plugin
          displayName: Sample Module 3
          version: 1.0.0
          owner: team@example.com
        plugin:
          assembly: Sample3.Plugin.dll
        frontend:
          remoteUrl: ""
          scope: sample3_remote
          modulePrefix: /sample3
          routes:
            - path: /sample3
              exposes: ./Page
        """;

    private const string RemoteManifest = """
        manifestVersion: "1.0"
        module:
          name: inventory
          displayName: Inventory
          version: 1.0.0
          owner: team@example.com
        service:
          baseUrl: http://inventory:8080
        frontend:
          remoteUrl: http://localhost:5178/assets/remoteEntry.js
          scope: inventory_remote
          modulePrefix: /inventory
        """;

    private static MemoryStream Zip(params (string Name, string Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
                w.Write(content);
            }
        }
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream ValidPlugin() => Zip(
        ("manifest.yml", PluginManifest),
        ("plugin/Sample3.Plugin.dll", "MZ"),
        ("plugin/Sample3.Plugin.deps.json", "{}"),
        ("web/assets/remoteEntry.js", "export {}"));

    private static IReadOnlyList<PackageFailure> Failures(Stream s, ModulePackageOptions? o = null)
    {
        var (info, failures) = ModulePackage.Inspect(s, o);
        Assert.Null(info);
        Assert.NotEmpty(failures);
        return failures;
    }

    [Fact]
    public void Valid_plugin_package_is_inspected()
    {
        using var zip = ValidPlugin();
        var (info, failures) = ModulePackage.Inspect(zip);
        Assert.Empty(failures);
        Assert.NotNull(info);
        Assert.Equal("plugin", info!.Manifest.Module.Type);
        Assert.True(info.HasPluginAssembly);
        Assert.True(info.HasRemoteEntry);
        Assert.Equal(64, info.Sha256.Length);
        zip.Position = 0;
        Assert.Equal(ModulePackage.Sha256Hex(zip), info.Sha256);
    }

    [Fact]
    public void Manifest_only_remote_package_is_valid()
    {
        using var zip = Zip(("manifest.yml", RemoteManifest), ("README.md", "# Inventory"));
        var (info, failures) = ModulePackage.Inspect(zip);
        Assert.Empty(failures);
        Assert.Equal("remote", ModuleTypeNames.Normalize(info!.Manifest.Module.Type));
        Assert.False(info.HasPluginAssembly);
    }

    [Fact]
    public void Missing_manifest_rejected() =>
        Assert.Contains(Failures(Zip(("README.md", "x"))), f => f.Field == "manifest.yml");

    [Fact]
    public void Invalid_yaml_rejected() =>
        Assert.Contains(Failures(Zip(("manifest.yml", "module: [unclosed"))), f => f.Message.StartsWith("Invalid YAML"));

    [Fact]
    public void Not_a_zip_rejected() =>
        Assert.Contains(Failures(new MemoryStream(Encoding.UTF8.GetBytes("not a zip"))), f => f.Field == "package");

    [Fact]
    public void Missing_plugin_assembly_rejected() =>
        Assert.Contains(Failures(Zip(("manifest.yml", PluginManifest), ("web/assets/remoteEntry.js", "x"))),
            f => f.Field == "plugin/Sample3.Plugin.dll");

    [Fact]
    public void Plugin_with_routes_and_no_remote_entry_rejected() =>
        Assert.Contains(Failures(Zip(("manifest.yml", PluginManifest), ("plugin/Sample3.Plugin.dll", "MZ"))),
            f => f.Field == ModulePackage.RemoteEntry);

    [Fact]
    public void Remote_package_must_not_carry_plugin_binaries() =>
        Assert.Contains(Failures(Zip(("manifest.yml", RemoteManifest), ("plugin/x.dll", "MZ"))), f => f.Field == "plugin/");

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("plugin/../../evil.dll")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/windows/evil.dll")]
    [InlineData("plugin\\evil.dll")]
    [InlineData("bin/evil.dll")]
    [InlineData("./manifest.yml")]
    public void Unsafe_or_disallowed_entries_rejected(string entry)
    {
        var failures = Failures(Zip(("manifest.yml", PluginManifest), ("plugin/Sample3.Plugin.dll", "MZ"), (entry, "x")));
        Assert.Contains(failures, f => f.Field == entry);
    }

    [Fact]
    public void Oversize_package_rejected()
    {
        using var zip = ValidPlugin();
        Assert.Contains(Failures(zip, new ModulePackageOptions { MaxPackageBytes = 10 }), f => f.Field == "package");
    }

    [Fact]
    public void Uncompressed_size_limit_enforced()
    {
        using var zip = Zip(("manifest.yml", PluginManifest), ("plugin/Sample3.Plugin.dll", new string('A', 10_000)),
            ("web/assets/remoteEntry.js", "x"));
        Assert.Contains(Failures(zip, new ModulePackageOptions { MaxUncompressedBytes = 1_000 }), f => f.Field == "package");
    }

    [Fact]
    public void Entry_count_limit_enforced()
    {
        using var zip = ValidPlugin();
        Assert.Contains(Failures(zip, new ModulePackageOptions { MaxEntries = 2 }), f => f.Field == "package");
    }

    [Fact]
    public void ExtractTo_writes_files_and_refuses_existing_target()
    {
        var zipPath = Path.Combine(_temp, "p.zip");
        using (var zip = ValidPlugin()) File.WriteAllBytes(zipPath, zip.ToArray());
        var target = Path.Combine(_temp, "out");
        ModulePackage.ExtractTo(zipPath, target);
        Assert.True(File.Exists(Path.Combine(target, "plugin", "Sample3.Plugin.dll")));
        Assert.True(File.Exists(Path.Combine(target, "web", "assets", "remoteEntry.js")));
        Assert.Throws<IOException>(() => ModulePackage.ExtractTo(zipPath, target));
    }

    [Fact]
    public void ExtractTo_rejects_traversal_and_leaves_nothing_behind()
    {
        var zipPath = Path.Combine(_temp, "evil.zip");
        using (var zip = Zip(("manifest.yml", PluginManifest), ("../evil.txt", "x"))) File.WriteAllBytes(zipPath, zip.ToArray());
        var target = Path.Combine(_temp, "evil-out");
        Assert.Throws<InvalidDataException>(() => ModulePackage.ExtractTo(zipPath, target));
        Assert.False(Directory.Exists(target));
        Assert.False(File.Exists(Path.Combine(_temp, "evil.txt")));
        Assert.Empty(Directory.GetDirectories(_temp));
    }

    [Fact]
    public void ExtractTo_enforces_actual_decompressed_size()
    {
        var zipPath = Path.Combine(_temp, "big.zip");
        using (var zip = Zip(("manifest.yml", PluginManifest), ("plugin/Sample3.Plugin.dll", new string('A', 50_000))))
            File.WriteAllBytes(zipPath, zip.ToArray());
        Assert.Throws<InvalidDataException>(() =>
            ModulePackage.ExtractTo(zipPath, Path.Combine(_temp, "big-out"), new ModulePackageOptions { MaxUncompressedBytes = 1_000 }));
    }
}
