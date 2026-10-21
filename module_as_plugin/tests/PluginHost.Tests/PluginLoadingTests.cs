using System.Runtime.Loader;
using Microsoft.AspNetCore.Routing;
using ModulePlatform.Plugin.Abstractions;
using PluginHost.Api.Runtime;
using SharedKernel;
using Xunit;

namespace PluginHost.Tests;

public class PluginLoadingTests
{
    private readonly string cacheRoot = TestPlugins.NewTempDir();
    private readonly PluginRuntimeFactory factory = TestPlugins.CreateFactory();

    [Fact]
    public async Task Each_runtime_loads_the_plugin_in_its_own_collectible_context()
    {
        var v1 = factory.Create(TestPlugins.Package(cacheRoot, TestPlugins.ModuleId, "1.0.0"));
        var v2 = factory.Create(TestPlugins.Package(cacheRoot, TestPlugins.ModuleId, "2.0.0"));
        try
        {
            var asm1 = v1.Plugin.GetType().Assembly;
            var asm2 = v2.Plugin.GetType().Assembly;
            Assert.NotSame(asm1, asm2);
            Assert.NotSame(v1.LoadContext, v2.LoadContext);
            Assert.Same(v1.LoadContext, AssemblyLoadContext.GetLoadContext(asm1));
            Assert.Same(v2.LoadContext, AssemblyLoadContext.GetLoadContext(asm2));
            Assert.True(v1.LoadContext!.IsCollectible);
            Assert.DoesNotContain(AssemblyLoadContext.Default.Assemblies, a => a.GetName().Name == asm1.GetName().Name);

            var endpoint = Assert.IsType<RouteEndpoint>(Assert.Single(v1.Endpoints));
            Assert.Equal("/plugins/fixture/api/v1/data", endpoint.RoutePattern.RawText);
            Assert.Equal("1.0.0", v1.Context.Version);
            Assert.Equal("2.0.0", v2.Context.Version);
        }
        finally
        {
            await v1.DisposeAsync();
            await v2.DisposeAsync();
        }
    }

    [Fact]
    public void Unsupported_contract_is_refused()
    {
        var ex = Assert.Throws<PluginLoadException>(() =>
            factory.Create(TestPlugins.Package(cacheRoot, TestPlugins.ModuleId, "1.0.0", contract: "9.9")));
        Assert.Contains("9.9", ex.Message);
    }

    [Fact]
    public void Missing_entry_assembly_is_a_load_failure()
    {
        var package = TestPlugins.Package(cacheRoot, TestPlugins.ModuleId, "1.0.0") with { Assembly = "Missing.Plugin.dll" };
        Assert.Throws<PluginLoadException>(() => factory.Create(package));
    }

    [Fact]
    public void Unknown_entry_type_is_a_load_failure()
    {
        var package = TestPlugins.Package(cacheRoot, TestPlugins.ModuleId, "1.0.0") with { EntryType = "Nope.Plugin" };
        Assert.Throws<PluginLoadException>(() => factory.Create(package));
    }

    [Fact]
    public async Task Shared_assemblies_resolve_from_the_host_even_when_copied_into_the_plugin_folder()
    {
        var package = TestPlugins.Package(cacheRoot, TestPlugins.ModuleId, "1.0.0");
        var pluginDir = Path.Combine(package.ContentRoot, "plugin");
        File.Copy(typeof(IModulePlugin).Assembly.Location, Path.Combine(pluginDir, Path.GetFileName(typeof(IModulePlugin).Assembly.Location)), overwrite: true);
        File.Copy(typeof(ModuleGuards).Assembly.Location, Path.Combine(pluginDir, Path.GetFileName(typeof(ModuleGuards).Assembly.Location)), overwrite: true);

        var runtime = factory.Create(package);
        try
        {
            Assert.Contains(typeof(IModulePlugin), runtime.Plugin.GetType().GetInterfaces());
            Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(typeof(IModulePlugin).Assembly));
            var privateNames = runtime.LoadContext!.Assemblies.Select(a => a.GetName().Name).ToList();
            Assert.DoesNotContain("ModulePlatform.Plugin.Abstractions", privateNames);
            Assert.DoesNotContain("SharedKernel", privateNames);
            Assert.Contains("PluginHost.Tests.FixturePlugin", privateNames);
        }
        finally { await runtime.DisposeAsync(); }
    }

    [Fact]
    public void Fixture_output_does_not_ship_host_provided_assemblies()
    {
        Assert.True(File.Exists(Path.Combine(TestPlugins.FixtureDir, TestPlugins.AssemblyFile)));
        Assert.False(File.Exists(Path.Combine(TestPlugins.FixtureDir, "ModulePlatform.Plugin.Abstractions.dll")));
        Assert.False(File.Exists(Path.Combine(TestPlugins.FixtureDir, "SharedKernel.dll")));
    }

    // Release contract: container disposed, endpoints dropped, unload requested on the collectible
    // context. Memory reclaim of the assembly is NOT asserted: ASP.NET Core 8 minimal-API
    // RequestDelegateFactory keeps process-wide static caches keyed by handler/parameter types that
    // keep the context reachable (verified: a bare context and one with only the DI container are
    // collected; one with built endpoints is not). See docs "Known limitations".
    [Fact]
    public async Task Disposed_runtime_is_released_and_its_load_context_unloading()
    {
        var runtime = factory.Create(TestPlugins.Package(cacheRoot, TestPlugins.ModuleId, "1.0.0"));
        var unloading = false;
        runtime.LoadContext!.Unloading += _ => unloading = true;
        var services = runtime.Services;

        await runtime.DisposeAsync();

        Assert.Equal(PluginRuntimeState.Disposed, runtime.State);
        Assert.Null(runtime.LoadContext);
        Assert.Empty(runtime.Endpoints);
        Assert.True(unloading);
        Assert.Throws<ObjectDisposedException>(() => services.GetService(typeof(object)));
        Assert.False(runtime.TryEnter());
    }
}
