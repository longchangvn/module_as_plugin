using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PluginHost.Api;
using PluginHost.Api.Routing;
using PluginHost.Api.Runtime;
using PluginHost.Api.Shells;

namespace PluginHost.Tests;

// Helpers around the fixture plugin (copied to {test output}/fixture-plugin by the csproj).
internal static class TestPlugins
{
    public const string ModuleId = "fixture";
    public const string AssemblyFile = "PluginHost.Tests.FixturePlugin.dll";

    public static string FixtureDir => Path.Combine(AppContext.BaseDirectory, "fixture-plugin");

    public static string Sha(string seed) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();

    public static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pluginhost-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // Lays out an extracted package exactly as PlatformPackageClient leaves it in the cache:
    // {cacheRoot}/{module}/{version}/{manifest.yml, plugin/**, web/**, .sha256}.
    public static string WritePackage(string cacheRoot, string moduleId, string version, string sha, string contract = "1.0")
    {
        var root = Path.Combine(cacheRoot, moduleId, version);
        if (Directory.Exists(root)) return root;
        var staging = root + ".tmp-" + Guid.NewGuid().ToString("N");
        var plugin = Path.Combine(staging, "plugin");
        Directory.CreateDirectory(plugin);
        foreach (var file in Directory.EnumerateFiles(FixtureDir, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(plugin, Path.GetRelativePath(FixtureDir, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }
        File.WriteAllText(Path.Combine(staging, "manifest.yml"), $"""
            manifestVersion: "1.0"
            module:
              name: {moduleId}
              type: plugin
              version: "{version}"
            plugin:
              assembly: {AssemblyFile}
              contract: "{contract}"
            """);
        Directory.CreateDirectory(Path.Combine(staging, "web", "assets"));
        File.WriteAllText(Path.Combine(staging, "web", "assets", "remoteEntry.js"), "export const remote = true;");
        File.WriteAllText(Path.Combine(staging, "web", "index.html"), "<html></html>");
        File.WriteAllText(Path.Combine(staging, PluginPackage.MarkerFile), sha);
        try { Directory.Move(staging, root); }
        catch (IOException) when (Directory.Exists(root)) { Directory.Delete(staging, true); } // concurrent writer won
        return root;
    }

    public static PluginPackage Package(string cacheRoot, string moduleId, string version, string? contract = null)
    {
        var sha = Sha($"{moduleId}@{version}");
        var root = WritePackage(cacheRoot, moduleId, version, sha, contract ?? "1.0");
        return PluginPackage.FromDirectory(moduleId, version, sha, root);
    }

    public static PluginRuntimeFactory CreateFactory(ILoggerFactory? loggers = null)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var http = new ServiceCollection().AddHttpClient().BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        var env = new HostingEnvironment { EnvironmentName = "Development", ApplicationName = "PluginHost.Tests", ContentRootPath = AppContext.BaseDirectory };
        return new PluginRuntimeFactory(cfg, loggers ?? NullLoggerFactory.Instance, http, env);
    }

    public static (TenantShellRegistry Registry, FolderPackageSource Source, PluginEndpointDataSource Endpoints) CreateRegistry(TimeSpan? drain = null)
    {
        var cacheRoot = NewTempDir();
        var source = new FolderPackageSource(cacheRoot);
        var endpoints = new PluginEndpointDataSource();
        var registry = new TenantShellRegistry(source, CreateFactory(), endpoints,
            new PluginHostOptions(cacheRoot, drain ?? TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5), ReconcileEnabled: false),
            NullLogger<TenantShellRegistry>.Instance);
        return (registry, source, endpoints);
    }
}

// In-memory stand-in for the Platform download: writes fixture packages into the cache folder.
// Contracts["module@version"] overrides plugin.contract; Unavailable simulates a download failure.
public sealed class FolderPackageSource(string cacheRoot) : IPluginPackageSource
{
    private int calls;

    public string CacheRoot { get; } = cacheRoot;
    public ConcurrentDictionary<string, string> Contracts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, bool> Unavailable { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Calls => Volatile.Read(ref calls);

    public Task<PluginPackage> GetAsync(string moduleId, string version, string packageSha256, CancellationToken ct)
    {
        Interlocked.Increment(ref calls);
        var key = PluginIds.RuntimeKey(moduleId, version);
        if (Unavailable.ContainsKey(key)) throw new PluginPackageUnavailableException($"{key} unavailable (test)");
        var contract = Contracts.GetValueOrDefault(key, "1.0");
        var root = TestPlugins.WritePackage(CacheRoot, moduleId, version, packageSha256, contract);
        return Task.FromResult(PluginPackage.FromDirectory(moduleId, version, packageSha256, root));
    }
}
