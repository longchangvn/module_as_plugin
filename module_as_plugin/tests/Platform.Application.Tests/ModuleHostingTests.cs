// Module hosting strategies (specs/002-module-hosting-types FR-003, FR-018, SC-006).
using Platform.Application;
using Platform.Domain;
using SharedKernel;
using Xunit;

namespace Platform.Application.Tests;

internal sealed class FakePluginHost(List<string> journal) : IPluginHostClient
{
    public bool FailActivate;

    public Task ActivateAsync(string moduleId, string tenantId, string version, bool enabled, string packageSha256, CancellationToken ct)
    {
        journal.Add($"host.activate:{moduleId}@{version}:{tenantId}:{enabled}");
        return FailActivate && enabled ? throw new InvalidOperationException("PLUGIN_LOAD_FAILED") : Task.CompletedTask;
    }

    public Task DeactivateAsync(string moduleId, string tenantId, CancellationToken ct)
    {
        journal.Add($"host.deactivate:{moduleId}:{tenantId}");
        return Task.CompletedTask;
    }

    public Task MigrateAsync(string moduleId, string version, string fromVersion, string packageSha256, CancellationToken ct)
    {
        journal.Add($"host.migrate:{moduleId}:{fromVersion}->{version}");
        return Task.CompletedTask;
    }
}

internal sealed class FakeRemoteClient(List<string> journal) : IRemoteModuleClient
{
    public Task MigrateAsync(string baseUrl, string fromVersion, string toVersion, CancellationToken ct)
    {
        journal.Add($"remote.migrate:{baseUrl}:{fromVersion}->{toVersion}");
        return Task.CompletedTask;
    }
}

// A test-only third module type: proves new types plug in without lifecycle/catalog/gateway edits.
internal sealed class EdgeFunctionStrategy(List<string> journal) : IModuleHostingStrategy
{
    public const string Name = "edge-function";
    public string Type => Name;
    public GatewayTarget GatewayTarget(ManifestFile manifest, ModuleVersionRef version) =>
        new($"https://edge.example.com/{version.ModuleId}", GatewayUpstreams.From(manifest));
    public Task ActivateAsync(string tenantId, ModuleVersionRef version, bool enabled, CancellationToken ct)
    {
        journal.Add($"edge.activate:{version.ModuleId}:{tenantId}:{enabled}");
        return Task.CompletedTask;
    }
    public Task DeactivateAsync(string tenantId, ModuleVersionRef version, CancellationToken ct)
    {
        journal.Add($"edge.deactivate:{version.ModuleId}:{tenantId}");
        return Task.CompletedTask;
    }
    public Task MigrateAsync(ModuleVersionRef target, string fromVersion, CancellationToken ct) => Task.CompletedTask;
}

// Lifecycle ports that route gateway side effects through the coordinator, exactly like
// EfLifecyclePorts does; everything else delegates to the in-memory FakePorts.
internal sealed class HostingPorts(FakePorts inner, ModuleHostingCoordinator hosting, Func<ManifestFile, ModuleVersionRef> refOf, List<string> journal)
    : IModuleLifecyclePorts
{
    public List<GatewayTarget> Published = new();

    public async Task ApplyGatewayRoutesAsync(string tenantId, ManifestFile manifest, bool enabled, CancellationToken ct) =>
        await hosting.ApplyAsync(tenantId, manifest, refOf(manifest), enabled, async target =>
        {
            Published.Add(target);
            journal.Add($"gateway.publish:{manifest.Module.Name}:{enabled}");
            await inner.ApplyGatewayRoutesAsync(tenantId, manifest, enabled, ct);
        }, ct);

    public async Task RemoveModuleRoutesAsync(string tenantId, ManifestFile manifest, CancellationToken ct) =>
        await hosting.RemoveAsync(tenantId, refOf(manifest), async () =>
        {
            journal.Add($"gateway.remove:{manifest.Module.Name}");
            await inner.RemoveModuleRoutesAsync(tenantId, manifest, ct);
        }, ct);

    public Task<TenantView?> GetTenantAsync(string tenantId, CancellationToken ct) => inner.GetTenantAsync(tenantId, ct);
    public Task<IReadOnlyList<string>> AvailableVersionsAsync(string moduleId, CancellationToken ct) => inner.AvailableVersionsAsync(moduleId, ct);
    public Task<ManifestFile?> GetManifestAsync(string moduleId, string version, CancellationToken ct) => inner.GetManifestAsync(moduleId, version, ct);
    public Task<string?> GetManifestYamlAsync(string moduleId, string version, CancellationToken ct) => inner.GetManifestYamlAsync(moduleId, version, ct);
    public Task<string?> GetInstalledVersionAsync(string tenantId, string moduleId, CancellationToken ct) => inner.GetInstalledVersionAsync(tenantId, moduleId, ct);
    public Task RunMigrateHookAsync(string moduleId, string fromVersion, string toVersion, CancellationToken ct) => inner.RunMigrateHookAsync(moduleId, fromVersion, toVersion, ct);
    public Task<IDictionary<string, InstallState>> GetInstallStatesAsync(string tenantId, CancellationToken ct) => inner.GetInstallStatesAsync(tenantId, ct);
    public Task SaveInstallAsync(string tenantId, string moduleId, string version, InstallState state, bool enabled, string? error, CancellationToken ct) => inner.SaveInstallAsync(tenantId, moduleId, version, state, enabled, error, ct);
    public Task SeedTenantAsync(string tenantId, ManifestFile manifest, CancellationToken ct) => inner.SeedTenantAsync(tenantId, manifest, ct);
    public Task RevokeSeedAsync(string tenantId, ManifestFile manifest, CancellationToken ct) => inner.RevokeSeedAsync(tenantId, manifest, ct);
    public Task AuditAsync(string? tenantId, string actorId, string actorRole, string action, string resourceType, string resourceId, string? reason, CancellationToken ct) => inner.AuditAsync(tenantId, actorId, actorRole, action, resourceType, resourceId, reason, ct);
}

public sealed class ModuleHostingTests
{
    private const string TenantId = "tnt_demo";
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly PluginHostOptions HostOptions = new() { BaseUrl = "http://plugin-host:8080", PublicBaseUrl = "https://modules.example.com" };

    private static ManifestFile Manifest(string name, string version, string type = "remote") => new()
    {
        Module = new ManifestModule { Name = name, DisplayName = name, Version = version, Owner = "team", Type = type },
        Api = new ManifestApi
        {
            Routes = [new() { Upstream = $"/api/v1/{name}/{{rest}}", Downstream = "/api/v1/{rest}", Methods = ["GET", "POST"] }],
        },
    };

    private static ModuleVersionRef Ref(string name, string version, string type, string? artifact = null, string? sha = "abc") =>
        new(name, version, type, artifact, sha);

    private static (ModuleHostingCoordinator Coordinator, List<string> Journal, FakePluginHost Host) Build(bool withEdge = false)
    {
        var journal = new List<string>();
        var host = new FakePluginHost(journal);
        var strategies = new List<IModuleHostingStrategy>
        {
            new RemoteHostingStrategy(new FakeRemoteClient(journal)),
            new PluginHostingStrategy(host, HostOptions),
        };
        if (withEdge) strategies.Add(new EdgeFunctionStrategy(journal));
        return (new ModuleHostingCoordinator(new ModuleHostingRegistry(strategies)), journal, host);
    }

    [Fact]
    public void Registry_resolves_by_type_case_insensitively_and_defaults_blank_to_remote()
    {
        var registry = new ModuleHostingRegistry([new RemoteHostingStrategy(new FakeRemoteClient([])), new PluginHostingStrategy(new FakePluginHost([]), HostOptions)]);
        Assert.IsType<PluginHostingStrategy>(registry.Get("PLUGIN"));
        Assert.IsType<RemoteHostingStrategy>(registry.Get(null));
        Assert.IsType<RemoteHostingStrategy>(registry.Get(""));
        var ex = Assert.Throws<InvalidOperationException>(() => registry.Get("lambda"));
        Assert.Contains("plugin, remote", ex.Message);
    }

    [Fact]
    public void Remote_target_uses_artifact_url_then_manifest_base_url()
    {
        var (c, _, _) = Build();
        var m = Manifest("inventory", "1.0.0");
        m.Service.BaseUrl = "http://inventory:8080";
        Assert.Equal("http://inv-artifact:9000", c.Target(m, Ref("inventory", "1.0.0", "remote", "http://inv-artifact:9000")).BaseUrl);
        var target = c.Target(m, Ref("inventory", "1.0.0", "remote"));
        Assert.Equal("http://inventory:8080", target.BaseUrl);
        var up = Assert.Single(target.Upstreams);
        Assert.Equal("/api/v1/inventory/{rest}", up.Upstream);
        Assert.Equal("/api/v1/{rest}", up.Downstream);
        Assert.Equal(["GET", "POST"], up.Methods);
    }

    [Fact]
    public void Plugin_target_is_the_module_host_with_plugin_prefixed_downstream()
    {
        var (c, _, _) = Build();
        var target = c.Target(Manifest("sample3", "1.0.0", "plugin"), Ref("sample3", "1.0.0", "plugin"));
        Assert.Equal("http://plugin-host:8080", target.BaseUrl);
        var up = Assert.Single(target.Upstreams);
        Assert.Equal("/api/v1/sample3/{rest}", up.Upstream);
        Assert.Equal("/plugins/sample3/api/v1/{rest}", up.Downstream);
        Assert.Equal("https://modules.example.com/plugin-assets/sample3/1.0.0/assets/remoteEntry.js", HostOptions.RemoteEntryUrl("sample3", "1.0.0"));
    }

    [Fact]
    public async Task Enabled_apply_activates_plugin_before_gateway_publish()
    {
        var (c, journal, _) = Build();
        await c.ApplyAsync(TenantId, Manifest("sample3", "1.0.0", "plugin"), Ref("sample3", "1.0.0", "plugin"), true,
            _ => { journal.Add("gateway"); return Task.CompletedTask; }, Ct);
        Assert.Equal(["host.activate:sample3@1.0.0:tnt_demo:True", "gateway"], journal);
    }

    [Fact]
    public async Task Disabled_apply_unpublishes_gateway_before_host_disable()
    {
        var (c, journal, _) = Build();
        await c.ApplyAsync(TenantId, Manifest("sample3", "1.0.0", "plugin"), Ref("sample3", "1.0.0", "plugin"), false,
            _ => { journal.Add("gateway"); return Task.CompletedTask; }, Ct);
        Assert.Equal(["gateway", "host.activate:sample3@1.0.0:tnt_demo:False"], journal);
    }

    [Fact]
    public async Task Plugin_load_failure_never_publishes_a_gateway_route()
    {
        var (c, journal, host) = Build();
        host.FailActivate = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.ApplyAsync(TenantId, Manifest("sample3", "1.0.0", "plugin"),
            Ref("sample3", "1.0.0", "plugin"), true, _ => { journal.Add("gateway"); return Task.CompletedTask; }, Ct));
        Assert.DoesNotContain("gateway", journal);
    }

    [Fact]
    public async Task Gateway_failure_after_activation_leaves_tenant_unserved()
    {
        var (c, journal, _) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.ApplyAsync(TenantId, Manifest("sample3", "1.0.0", "plugin"),
            Ref("sample3", "1.0.0", "plugin"), true, _ => throw new InvalidOperationException("gateway down"), Ct));
        Assert.Equal(["host.activate:sample3@1.0.0:tnt_demo:True", "host.activate:sample3@1.0.0:tnt_demo:False"], journal);
    }

    [Fact]
    public async Task Remove_unroutes_gateway_before_deactivating_and_tolerates_host_failure()
    {
        var (c, journal, _) = Build();
        var warning = await c.RemoveAsync(TenantId, Ref("sample3", "1.0.0", "plugin"), () => { journal.Add("gateway.remove"); return Task.CompletedTask; }, Ct);
        Assert.Null(warning);
        Assert.Equal(["gateway.remove", "host.deactivate:sample3:tnt_demo"], journal);

        var failing = new ModuleHostingCoordinator(new ModuleHostingRegistry([new ThrowingDeactivate()]));
        var warn = await failing.RemoveAsync(TenantId, Ref("x", "1.0.0", "plugin"), () => Task.CompletedTask, Ct);
        Assert.NotNull(warn);
    }

    [Fact]
    public async Task Plugin_without_package_is_refused()
    {
        var (c, _, _) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.ApplyAsync(TenantId, Manifest("sample3", "1.0.0", "plugin"),
            Ref("sample3", "1.0.0", "plugin", sha: null), true, _ => Task.CompletedTask, Ct));
    }

    [Fact]
    public async Task Migrate_dispatches_per_type_and_skips_same_version()
    {
        var (c, journal, _) = Build();
        await c.MigrateAsync(Ref("sample3", "1.1.0", "plugin"), "1.0.0", Ct);
        await c.MigrateAsync(Ref("inventory", "1.1.0", "remote", "http://inv:8080"), "1.0.0", Ct);
        await c.MigrateAsync(Ref("inventory", "1.1.0", "remote"), "1.0.0", Ct); // no artifact URL → skipped
        await c.MigrateAsync(Ref("sample3", "1.1.0", "plugin"), "1.1.0", Ct);   // same version → skipped
        Assert.Equal(["host.migrate:sample3:1.0.0->1.1.0", "remote.migrate:http://inv:8080:1.0.0->1.1.0"], journal);
    }

    // SC-006: a new type registered next to remote/plugin installs through the unchanged lifecycle.
    [Fact]
    public async Task Third_module_type_installs_and_uninstalls_without_lifecycle_changes()
    {
        var (c, journal, _) = Build(withEdge: true);
        var fake = new FakePorts();
        fake.Tenants[TenantId] = new FakePorts.Tenant(TenantId, "Ready");
        fake.Versions["edgy"] = ["1.0.0"];
        fake.Manifests["edgy@1.0.0"] = Manifest("edgy", "1.0.0", EdgeFunctionStrategy.Name);
        var ports = new HostingPorts(fake, c, m => Ref(m.Module.Name, m.Module.Version, m.Module.Type), journal);

        var result = await new ModuleLifecycleService(ports).InstallAsync(TenantId, "edgy", null, "u_admin", "Admin", Ct);

        Assert.True(result.Ok);
        Assert.Equal(InstallState.Installed, fake.Installed[$"{TenantId}:edgy"].State);
        Assert.Equal("https://edge.example.com/edgy", Assert.Single(ports.Published).BaseUrl);
        Assert.True(journal.IndexOf("edge.activate:edgy:tnt_demo:True") < journal.IndexOf("gateway.publish:edgy:True"));

        await ports.RemoveModuleRoutesAsync(TenantId, fake.Manifests["edgy@1.0.0"], Ct);
        Assert.True(journal.IndexOf("gateway.remove:edgy") < journal.IndexOf("edge.deactivate:edgy:tnt_demo"));
    }

    [Fact]
    public async Task Plugin_install_failure_marks_install_failed_without_gateway_route()
    {
        var (c, journal, host) = Build();
        host.FailActivate = true;
        var fake = new FakePorts();
        fake.Tenants[TenantId] = new FakePorts.Tenant(TenantId, "Ready");
        fake.Versions["sample3"] = ["1.0.0"];
        fake.Manifests["sample3@1.0.0"] = Manifest("sample3", "1.0.0", "plugin");
        var ports = new HostingPorts(fake, c, m => Ref(m.Module.Name, m.Module.Version, m.Module.Type), journal);

        var result = await new ModuleLifecycleService(ports).InstallAsync(TenantId, "sample3", null, "u_admin", "Admin", Ct);

        Assert.False(result.Ok);
        Assert.Equal(InstallState.Failed, fake.Installed[$"{TenantId}:sample3"].State);
        Assert.Empty(ports.Published);
    }

    private sealed class ThrowingDeactivate : IModuleHostingStrategy
    {
        public string Type => "plugin";
        public GatewayTarget GatewayTarget(ManifestFile manifest, ModuleVersionRef version) => new(null, []);
        public Task ActivateAsync(string tenantId, ModuleVersionRef version, bool enabled, CancellationToken ct) => Task.CompletedTask;
        public Task DeactivateAsync(string tenantId, ModuleVersionRef version, CancellationToken ct) => throw new HttpRequestException("host down");
        public Task MigrateAsync(ModuleVersionRef target, string fromVersion, CancellationToken ct) => Task.CompletedTask;
    }
}
