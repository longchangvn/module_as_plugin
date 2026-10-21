using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Routing;
using ModulePlatform.Plugin.Abstractions;
using PluginHost.Api.Loading;
using PluginHost.Api.Routing;
using SharedKernel;

namespace PluginHost.Api.Runtime;

// Builds a PluginRuntime from an extracted package (research R5/R7): load the entry assembly in
// a fresh collectible context, check the contract, build the runtime container from the shared
// host singletons + plugin services, and map the plugin's endpoints under /plugins/{moduleId}.
public sealed class PluginRuntimeFactory(
    IConfiguration configuration,
    ILoggerFactory loggerFactory,
    IHttpClientFactory httpClientFactory,
    IHostEnvironment environment)
{
    private readonly ILogger log = loggerFactory.CreateLogger<PluginRuntimeFactory>();

    public PluginRuntime Create(PluginPackage package)
    {
        var sw = Stopwatch.StartNew();
        PluginLoadContext? alc = null;
        try
        {
            var contract = string.IsNullOrWhiteSpace(package.Contract) ? PluginContract.Version : package.Contract.Trim();
            if (contract != PluginContract.Version)
                throw new PluginLoadException($"Unsupported plugin contract '{contract}' (host supports {PluginContract.Version}).");
            if (!PluginModuleTypeRules.IsValidAssemblyName(package.Assembly))
                throw new PluginLoadException($"Invalid plugin.assembly '{package.Assembly}'.");
            var entryPath = Path.GetFullPath(Path.Combine(package.ContentRoot, "plugin", package.Assembly));
            if (!File.Exists(entryPath))
                throw new PluginLoadException($"Plugin assembly '{package.Assembly}' not found in plugin/.");

            alc = new PluginLoadContext(entryPath, $"plugin:{PluginIds.RuntimeKey(package.ModuleId, package.Version)}");
            var assembly = alc.LoadFromAssemblyPath(entryPath);
            var plugin = (IModulePlugin)Activator.CreateInstance(FindEntryType(assembly, package.EntryType))!;

            var runtime = new PluginRuntime(package, alc);
            var context = new PluginContext(package.ModuleId, package.Version, configuration, package.ContentRoot);
            runtime.Plugin = plugin;
            runtime.Context = context;
            runtime.Services = BuildServices(plugin, context);
            try
            {
                runtime.Endpoints = MapEndpoints(runtime, plugin, context);
            }
            catch
            {
                runtime.Services.Dispose();
                throw;
            }
            Record(package, sw, "ok");
            return runtime;
        }
        catch (Exception ex)
        {
            alc?.Unload();
            Record(package, sw, "error", ex);
            throw ex is PluginLoadException ? ex : new PluginLoadException($"Loading {package.ModuleId}@{package.Version} failed: {ex.Message}", ex);
        }
    }

    private static Type FindEntryType(Assembly assembly, string? entryType)
    {
        if (entryType is not null)
        {
            var type = assembly.GetType(entryType, throwOnError: false)
                ?? throw new PluginLoadException($"plugin.entryType '{entryType}' not found in {assembly.GetName().Name}.");
            return IsPluginType(type) ? type
                : throw new PluginLoadException($"plugin.entryType '{entryType}' is not a public non-abstract IModulePlugin with a parameterless constructor.");
        }
        var candidates = assembly.GetExportedTypes().Where(IsPluginType).ToList();
        return candidates.Count == 1 ? candidates[0]
            : throw new PluginLoadException(candidates.Count == 0
                ? $"No public IModulePlugin implementation in {assembly.GetName().Name} (contract {PluginContract.Version})."
                : $"Multiple IModulePlugin implementations in {assembly.GetName().Name}; set plugin.entryType.");
    }

    private static bool IsPluginType(Type t) =>
        t is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(IModulePlugin).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null;

    private ServiceProvider BuildServices(IModulePlugin plugin, PluginContext context)
    {
        var services = new ServiceCollection();
        // Shared host singletons (registered as instances: the runtime container never disposes them).
        services.AddSingleton(configuration);
        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton(httpClientFactory);
        services.AddSingleton(environment);
        if (environment is IWebHostEnvironment web) services.AddSingleton(web);
        // Private to this plugin version.
        services.AddMemoryCache();
        services.AddOptions();
        services.AddRouting();
        plugin.ConfigureServices(services, context);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = environment.IsDevelopment() });
    }

    private static IReadOnlyList<Endpoint> MapEndpoints(PluginRuntime runtime, IModulePlugin plugin, PluginContext context)
    {
        var builder = new PluginRouteBuilder(runtime.Services);
        var group = builder.MapGroup($"/plugins/{context.ModuleId}");
        group.WithMetadata(new PluginEndpointMetadata(runtime));
        plugin.MapEndpoints(group, context);
        // Materialize once: RequestDelegates are built against the runtime container here.
        return builder.DataSources.SelectMany(ds => ds.Endpoints).ToList();
    }

    private void Record(PluginPackage package, Stopwatch sw, string result, Exception? error = null)
    {
        var seconds = sw.Elapsed.TotalSeconds;
        PluginHostMetrics.LoadCount.Add(1, new KeyValuePair<string, object?>("result", result));
        PluginHostMetrics.LoadDuration.Record(seconds, new KeyValuePair<string, object?>("result", result));
        if (error is null)
            log.LogInformation("plugin.load module={module} version={version} in={seconds:0.000}s result=ok", package.ModuleId, package.Version, seconds);
        else
            log.LogWarning("plugin.load module={module} version={version} in={seconds:0.000}s result=error error={error}", package.ModuleId, package.Version, seconds, error.Message);
    }

    // Private route builder: endpoints are created with the runtime container as ServiceProvider.
    private sealed class PluginRouteBuilder(IServiceProvider services) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider => services;
        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(services);
    }
}
