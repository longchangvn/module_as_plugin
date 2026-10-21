using System.Reflection;
using System.Runtime.Loader;

namespace PluginHost.Api.Loading;

// One collectible load context per (module, version) — research R5. Host-provided assemblies
// (framework, ASP.NET Core, Microsoft.Extensions.*, SharedKernel, Plugin.Abstractions — anything
// already in the default context or on the trusted platform list) are deferred to the default
// context so IModulePlugin, ModuleGuards, DI and routing types keep one identity. Everything else
// resolves from the plugin folder and is private to this version.
public sealed class PluginLoadContext(string entryAssemblyPath, string name) : AssemblyLoadContext(name, isCollectible: true)
{
    private static readonly HashSet<string> AlwaysShared = new(StringComparer.OrdinalIgnoreCase)
    {
        "ModulePlatform.Plugin.Abstractions", "SharedKernel",
    };

    private static readonly Lazy<HashSet<string>> TrustedPlatformAssemblies = new(() =>
        new HashSet<string>(
            ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>(),
            StringComparer.OrdinalIgnoreCase));

    private readonly AssemblyDependencyResolver resolver = new(entryAssemblyPath);

    public static bool IsHostProvided(AssemblyName assemblyName)
    {
        var simple = assemblyName.Name;
        if (string.IsNullOrEmpty(simple)) return false;
        return AlwaysShared.Contains(simple)
            || TrustedPlatformAssemblies.Value.Contains(simple)
            || Default.Assemblies.Any(a => string.Equals(a.GetName().Name, simple, StringComparison.OrdinalIgnoreCase));
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (IsHostProvided(assemblyName)) return null;
        var path = resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
