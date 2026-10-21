using PluginHost.Api;
using PluginHost.Api.Runtime;
using PluginHost.Api.Shells;
using Xunit;

namespace PluginHost.Tests;

public class TenantShellRegistryTests : IAsyncLifetime
{
    private const string M = TestPlugins.ModuleId;
    private readonly TenantShellRegistry registry;
    private readonly FolderPackageSource source;
    private readonly Api.Routing.PluginEndpointDataSource endpoints;

    public TenantShellRegistryTests() => (registry, source, endpoints) = TestPlugins.CreateRegistry();

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await registry.DisposeAsync();

    private static string Sha(string version) => TestPlugins.Sha($"{M}@{version}");
    private Task<PluginRuntime> Apply(string tenant, string version, bool enabled = true) =>
        registry.ApplyAsync(M, tenant, version, enabled, Sha(version));

    [Fact]
    public async Task Decide_reports_not_installed_disabled_and_enabled()
    {
        Assert.Equal(ShellDecisionKind.NotInstalled, registry.Decide("tnt_a", M).Kind);

        await Apply("tnt_a", "1.0.0", enabled: false);
        Assert.Equal(ShellDecisionKind.Disabled, registry.Decide("tnt_a", M).Kind);

        var runtime = await Apply("tnt_a", "1.0.0");
        var decision = registry.Decide("TNT_A", "Fixture"); // ids are case-insensitive
        Assert.Equal(ShellDecisionKind.Enabled, decision.Kind);
        Assert.Same(runtime, decision.Runtime);
        Assert.Equal(1, source.Calls); // second apply reused the loaded runtime
    }

    [Fact]
    public async Task Installing_for_one_tenant_does_not_change_another()
    {
        var b = await Apply("tnt_b", "1.0.0");
        await Apply("tnt_a", "2.0.0");
        await Apply("tnt_a", "2.0.0", enabled: false);
        await registry.RemoveAsync(M, "tnt_a");

        var decision = registry.Decide("tnt_b", M);
        Assert.Equal(ShellDecisionKind.Enabled, decision.Kind);
        Assert.Same(b, decision.Runtime);
        Assert.Equal(PluginRuntimeState.Active, b.State);
        Assert.Equal(ShellDecisionKind.NotInstalled, registry.Decide("tnt_b", "other").Kind);
    }

    [Fact]
    public async Task Tenants_on_different_versions_resolve_to_different_runtimes()
    {
        var v1 = await Apply("tnt_a", "1.0.0");
        var v2 = await Apply("tnt_b", "2.0.0");

        Assert.NotSame(v1, v2);
        Assert.Same(v1, registry.Decide("tnt_a", M).Runtime);
        Assert.Same(v2, registry.Decide("tnt_b", M).Runtime);
        Assert.Equal("2.0.0", v2.Version);
        Assert.Equal(2, endpoints.Endpoints.Count); // same route, two runtimes — disambiguated per request
        Assert.Equal(2, registry.View().Runtimes.Count);
    }

    [Fact]
    public async Task Failed_upgrade_keeps_the_previous_version()
    {
        var v1 = await Apply("tnt_a", "1.0.0");
        source.Contracts[$"{M}@1.1.0"] = "9.9";
        source.Unavailable[$"{M}@1.2.0"] = true;

        await Assert.ThrowsAsync<PluginLoadException>(() => Apply("tnt_a", "1.1.0"));
        await Assert.ThrowsAsync<PluginPackageUnavailableException>(() => Apply("tnt_a", "1.2.0"));

        Assert.Same(v1, registry.Decide("tnt_a", M).Runtime);
        Assert.Equal(PluginRuntimeState.Active, v1.State);
        var view = registry.View();
        Assert.Equal("1.0.0", view.Tenants["tnt_a"][M].Version);
        Assert.Equal("1.0.0", Assert.Single(view.Runtimes).Version);
        Assert.Single(endpoints.Endpoints);
    }

    [Fact]
    public async Task Runtime_is_released_when_its_last_tenant_is_removed()
    {
        var shared = await Apply("tnt_a", "1.0.0");
        Assert.Same(shared, await Apply("tnt_b", "1.0.0"));

        Assert.True(await registry.RemoveAsync(M, "tnt_a"));
        await registry.WhenReleasedAsync();
        Assert.Equal(PluginRuntimeState.Active, shared.State);

        Assert.True(await registry.RemoveAsync(M, "tnt_b"));
        await registry.WhenReleasedAsync();
        Assert.Equal(PluginRuntimeState.Disposed, shared.State);
        Assert.Empty(endpoints.Endpoints);
        Assert.Empty(registry.View().Runtimes);
        Assert.False(await registry.RemoveAsync(M, "tnt_b")); // idempotent
    }

    [Fact]
    public async Task Upgrade_releases_the_old_version_after_in_flight_requests_drain()
    {
        var v1 = await Apply("tnt_a", "1.0.0");
        Assert.True(v1.TryEnter()); // a request still running on 1.0.0

        var v2 = await Apply("tnt_a", "2.0.0");
        Assert.Same(v2, registry.Decide("tnt_a", M).Runtime);
        await Task.Delay(150);
        Assert.Equal(PluginRuntimeState.Draining, v1.State);
        Assert.False(v1.TryEnter()); // no new admissions while draining
        Assert.Equal(2, endpoints.Endpoints.Count); // old endpoints stay until drained

        v1.Exit();
        await registry.WhenReleasedAsync();
        Assert.Equal(PluginRuntimeState.Disposed, v1.State);
        Assert.Single(endpoints.Endpoints);
    }

    [Fact]
    public async Task Reconcile_is_skipped_when_a_push_happened_after_the_fetch()
    {
        var generation = registry.PushGeneration;
        await Apply("tnt_a", "1.0.0");

        var stale = new[] { new DesiredPlugin(M, "2.0.0", Sha("2.0.0"), [new DesiredTenant("tnt_b", true)]) };
        var skipped = await registry.ReplaceAsync(stale, generation);
        Assert.True(skipped.Skipped);
        Assert.Equal(ShellDecisionKind.NotInstalled, registry.Decide("tnt_b", M).Kind);
        Assert.Equal(ShellDecisionKind.Enabled, registry.Decide("tnt_a", M).Kind);

        var applied = await registry.ReplaceAsync(stale, registry.PushGeneration);
        Assert.False(applied.Skipped);
        Assert.Equal(2, applied.Drift); // tnt_a removed, tnt_b added
        Assert.Equal("2.0.0", registry.Decide("tnt_b", M).Runtime!.Version);
        Assert.Equal(ShellDecisionKind.NotInstalled, registry.Decide("tnt_a", M).Kind);
    }

    [Fact]
    public async Task Reconcile_keeps_current_entries_for_versions_that_fail_to_load()
    {
        var v1 = await Apply("tnt_a", "1.0.0");
        source.Contracts[$"{M}@3.0.0"] = "9.9";
        var desired = new[]
        {
            new DesiredPlugin(M, "3.0.0", Sha("3.0.0"), [new DesiredTenant("tnt_a", true)]),
            new DesiredPlugin(M, "2.0.0", Sha("2.0.0"), [new DesiredTenant("tnt_c", false)]),
        };

        var result = await registry.ReplaceAsync(desired, registry.PushGeneration);

        Assert.Single(result.Failures);
        Assert.Same(v1, registry.Decide("tnt_a", M).Runtime);
        Assert.Equal(ShellDecisionKind.Disabled, registry.Decide("tnt_c", M).Kind);
    }

    [Fact]
    public async Task EnsureRuntime_pins_until_the_next_swap()
    {
        var pinned = await registry.EnsureRuntimeAsync(M, "4.0.0", Sha("4.0.0"));
        Assert.Contains(registry.View().Runtimes, r => r.Version == "4.0.0");
        Assert.Equal(ShellDecisionKind.NotInstalled, registry.Decide("tnt_a", M).Kind);

        Assert.Same(pinned, await Apply("tnt_a", "4.0.0")); // the migrate-then-activate flow reuses it

        var other = await registry.EnsureRuntimeAsync(M, "5.0.0", Sha("5.0.0"));
        await Apply("tnt_b", "1.0.0"); // next swap: 5.0.0 is unreferenced → collected
        await registry.WhenReleasedAsync();
        Assert.Equal(PluginRuntimeState.Disposed, other.State);
        Assert.Equal(PluginRuntimeState.Active, pinned.State);
    }

    [Fact]
    public void Ids_and_versions_must_be_path_safe()
    {
        Assert.True(PluginIds.IsValidId("tnt_demo-1"));
        Assert.False(PluginIds.IsValidId("../x"));
        Assert.False(PluginIds.IsValidId(""));
        Assert.True(PluginIds.IsValidVersion("1.0.0-beta.1+build"));
        Assert.False(PluginIds.IsValidVersion(".."));
        Assert.False(PluginIds.IsValidVersion("1..0"));
        Assert.False(PluginIds.IsValidVersion("1/0"));
    }
}
