// Lifecycle round-trip tests over an in-memory IModuleLifecyclePorts fake (A8).
// Covers SR-5 (install pipeline + FAILED rollback), R6 (uninstall guard),
// R7/R8 (idempotent re-install), P-BR6 (dependency auto-install) and the
// upgrade guards from §9.1 of 01-module-platform-requirements.md.
using Platform.Application;
using Platform.Domain;
using SharedKernel;
using Xunit;

namespace Platform.Application.Tests;

/// <summary>In-memory port fake: registries, install rows and a call journal.</summary>
internal sealed class FakePorts : IModuleLifecyclePorts
{
    public sealed record Tenant(string Id, string Status);

    public Dictionary<string, Tenant> Tenants = new();
    public Dictionary<string, List<string>> Versions = new();                       // moduleId -> versions (ascending)
    public Dictionary<string, ManifestFile> Manifests = new();                      // moduleId@version -> manifest
    public Dictionary<string, (string Version, InstallState State, bool Enabled)> Installed = new(); // tenantId:module -> row

    public List<string> Journal = new();
    public List<(string Module, bool Enabled)> RoutesApplied = new();
    public List<string> Seeded = new();
    public bool FailGatewayOnModule = false;                                        // trigger SR-5 rollback path

    public string? InstalledVersion(string tenantId, string moduleId) =>
        Installed.TryGetValue(Key(tenantId, moduleId), out var row) ? row.Version : null;

    private static string Key(string tenantId, string moduleId) => $"{tenantId}:{moduleId.ToLowerInvariant()}";

    public Task<TenantView?> GetTenantAsync(string tenantId, CancellationToken ct) =>
        Task.FromResult(Tenants.TryGetValue(tenantId, out var t)
            ? new TenantView(t.Id, t.Status) : (TenantView?)null);

    public Task<IReadOnlyList<string>> AvailableVersionsAsync(string moduleId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Versions.TryGetValue(moduleId, out var v) ? v : new List<string>());

    public Task<ManifestFile?> GetManifestAsync(string moduleId, string version, CancellationToken ct) =>
        Task.FromResult(Manifests.TryGetValue($"{moduleId}@{version}", out var m) ? m : null);

    public Task<string?> GetManifestYamlAsync(string moduleId, string version, CancellationToken ct) =>
        Task.FromResult<string?>(null);

    public Task<string?> GetInstalledVersionAsync(string tenantId, string moduleId, CancellationToken ct) =>
        Task.FromResult(InstalledVersion(tenantId, moduleId));

    public Task RemoveModuleRoutesAsync(string tenantId, ManifestFile manifest, CancellationToken ct)
    {
        Journal.Add($"routes.remove:{manifest.Module.Name}");
        return Task.CompletedTask;
    }

    public Task RunMigrateHookAsync(string moduleId, string fromVersion, string toVersion, CancellationToken ct)
    {
        Journal.Add($"migrate:{moduleId}:{fromVersion}->{toVersion}");
        return Task.CompletedTask;
    }

    public Task<IDictionary<string, InstallState>> GetInstallStatesAsync(string tenantId, CancellationToken ct)
    {
        var map = Installed
            .Where(kv => kv.Key.StartsWith(tenantId + ":", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key[(tenantId.Length + 1)..], kv => kv.Value.State, StringComparer.OrdinalIgnoreCase);
        return Task.FromResult<IDictionary<string, InstallState>>(map);
    }

    public Task SaveInstallAsync(string tenantId, string moduleId, string version, InstallState state, bool enabled, string? error, CancellationToken ct)
    {
        Installed[Key(tenantId, moduleId)] = (version, state, enabled);
        Journal.Add($"save:{moduleId}@{version}:{state}" + (error is null ? "" : $":{error}"));
        return Task.CompletedTask;
    }

    public Task ApplyGatewayRoutesAsync(string tenantId, ManifestFile manifest, bool enabled, CancellationToken ct)
    {
        if (FailGatewayOnModule && manifest.Module.Name == "loyalty")
            throw new InvalidOperationException("gateway unavailable (simulated)");
        RoutesApplied.Add((manifest.Module.Name, enabled));
        Journal.Add($"routes:{manifest.Module.Name}:{enabled}");
        return Task.CompletedTask;
    }

    public Task SeedTenantAsync(string tenantId, ManifestFile manifest, CancellationToken ct)
    {
        Seeded.Add(manifest.Module.Name);
        Journal.Add($"seed:{manifest.Module.Name}");
        return Task.CompletedTask;
    }

    public Task RevokeSeedAsync(string tenantId, ManifestFile manifest, CancellationToken ct)
    {
        Journal.Add($"revoke:{manifest.Module.Name}");
        return Task.CompletedTask;
    }

    public Task AuditAsync(string? tenantId, string actorId, string actorRole, string action, string resourceType, string resourceId, string? reason, CancellationToken ct)
    {
        Journal.Add($"audit:{action}:{resourceId}");
        return Task.CompletedTask;
    }
}

public sealed class LifecycleServiceTests
{
    private const string TenantId = "tnt_demo";
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static ManifestFile Manifest(string name, string version, params (string Name, string Range)[] deps) => new()
    {
        Module = new ManifestModule { Name = name, DisplayName = name, Version = version, Owner = "team-x" },
        Dependencies = deps.Select(d => new ManifestDependency { Name = d.Name, VersionRange = d.Range }).ToList(),
        Api = new ManifestApi
        {
            Routes = new List<ManifestApiRoute>
            {
                new() { Upstream = $"/api/{name}/{{action}}", Downstream = $"/api/{name}/{{action}}", Methods = new List<string> { "GET" } },
            },
        },
    };

    private static FakePorts Ports(bool withDependency = false)
    {
        var f = new FakePorts();
        f.Tenants[TenantId] = new FakePorts.Tenant(TenantId, "Ready");
        f.Versions["loyalty"] = new List<string> { "1.0.0", "1.1.0" };
        f.Manifests["loyalty@1.0.0"] = Manifest("loyalty", "1.0.0");
        f.Manifests["loyalty@1.1.0"] = Manifest("loyalty", "1.1.0");
        if (withDependency)
        {
            f.Versions["payment"] = new List<string> { "2.0.0" };
            f.Manifests["loyalty@1.0.0"] = Manifest("loyalty", "1.0.0", ("payment", "^2.0.0"));
            f.Manifests["payment@2.0.0"] = Manifest("payment", "2.0.0");
        }
        return f;
    }

    // ---- install (SR-5) ----

    [Fact]
    public async Task Install_happy_path_applies_routes_seed_state_and_audit()
    {
        var ports = Ports();
        var svc = new ModuleLifecycleService(ports);

        var result = await svc.InstallAsync(TenantId, "loyalty", null, "u_admin", "Admin", Ct);

        Assert.True(result.Ok);
        Assert.Equal("1.1.0", result.Value); // latest when version omitted
        Assert.Equal(InstallState.Installed, ports.Installed[$"{TenantId}:loyalty"].State);
        Assert.True(ports.Installed[$"{TenantId}:loyalty"].Enabled);
        Assert.Contains(("loyalty", true), ports.RoutesApplied);
        Assert.Contains("loyalty", ports.Seeded);
        Assert.Contains("audit:install:loyalty", ports.Journal);
    }

    [Fact]
    public async Task Install_unknown_tenant_or_module_fails_with_contract_codes()
    {
        var ports = Ports();
        var svc = new ModuleLifecycleService(ports);

        var noTenant = await svc.InstallAsync("tnt_nope", "loyalty", null, "u", "Admin", Ct);
        var noModule = await svc.InstallAsync(TenantId, "ghost", null, "u", "Admin", Ct);

        Assert.False(noTenant.Ok);
        Assert.Equal(ErrorCodes.UnknownTenant, noTenant.Error!.Code);
        Assert.False(noModule.Ok);
        Assert.Equal(ErrorCodes.UnknownModule, noModule.Error!.Code);
        Assert.Empty(ports.RoutesApplied); // no side effects on validation failure
    }

    [Fact]
    public async Task Install_disabled_tenant_is_rejected()
    {
        var ports = Ports();
        ports.Tenants[TenantId] = new FakePorts.Tenant(TenantId, "Disabled");
        var svc = new ModuleLifecycleService(ports);

        var result = await svc.InstallAsync(TenantId, "loyalty", null, "u", "Admin", Ct);

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.TenantDisabled, result.Error!.Code);
    }

    [Fact]
    public async Task Reinstall_same_version_is_idempotent_no_side_effects()
    {
        var ports = Ports();
        ports.Installed[$"{TenantId}:loyalty"] = ("1.1.0", InstallState.Installed, true);
        var svc = new ModuleLifecycleService(ports);

        var result = await svc.InstallAsync(TenantId, "loyalty", "1.1.0", "u", "Admin", Ct);

        Assert.True(result.Ok);
        Assert.Equal("1.1.0", result.Value);
        Assert.Empty(ports.RoutesApplied); // R7/R8: no duplicate work
        Assert.DoesNotContain("audit:install:loyalty", ports.Journal);
    }

    [Fact]
    public async Task Install_with_missing_dependency_installs_dependency_first()
    {
        var ports = Ports(withDependency: true);
        var svc = new ModuleLifecycleService(ports);

        var result = await svc.InstallAsync(TenantId, "loyalty", "1.0.0", "u", "Admin", Ct);

        Assert.True(result.Ok);
        // P-BR6 topo order: payment (dep) lands before loyalty
        var seedIdx = ports.Journal.IndexOf("seed:payment");
        var loyaltyIdx = ports.Journal.IndexOf("seed:loyalty");
        Assert.True(seedIdx >= 0 && loyaltyIdx > seedIdx, "dependency must be seeded before the dependent");
        Assert.Equal(InstallState.Installed, ports.Installed[$"{TenantId}:payment"].State);
        Assert.Equal(InstallState.Installed, ports.Installed[$"{TenantId}:loyalty"].State);
    }

    [Fact]
    public async Task Install_side_effect_failure_marks_FAILED_and_audits()
    {
        var ports = Ports();
        ports.FailGatewayOnModule = true;
        var svc = new ModuleLifecycleService(ports);

        var result = await svc.InstallAsync(TenantId, "loyalty", "1.0.0", "u", "Admin", Ct);

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.ProvisionFailed, result.Error!.Code);
        Assert.True(result.Error.Retryable);
        Assert.Equal(InstallState.Failed, ports.Installed[$"{TenantId}:loyalty"].State);
        Assert.Contains("audit:install_failed:loyalty", ports.Journal);
    }

    // ---- upgrade (§9.1) ----

    [Fact]
    public async Task Upgrade_requires_installed_module()
    {
        var ports = Ports();
        var svc = new ModuleLifecycleService(ports);

        var result = await svc.UpgradeAsync(TenantId, "loyalty", "1.1.0", "u", "Admin", Ct);

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.ModuleNotInstalled, result.Error!.Code);
    }

    [Fact]
    public async Task Upgrade_must_target_newer_version()
    {
        var ports = Ports();
        ports.Installed[$"{TenantId}:loyalty"] = ("1.1.0", InstallState.Installed, true);
        var svc = new ModuleLifecycleService(ports);

        var result = await svc.UpgradeAsync(TenantId, "loyalty", "1.0.0", "u", "Admin", Ct);

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.ValidationError, result.Error!.Code);
    }

    [Fact]
    public async Task Upgrade_happy_path_runs_migrate_hook_and_persists()
    {
        var ports = Ports();
        ports.Installed[$"{TenantId}:loyalty"] = ("1.0.0", InstallState.Installed, true);
        var svc = new ModuleLifecycleService(ports);

        var result = await svc.UpgradeAsync(TenantId, "loyalty", "1.1.0", "u", "Admin", Ct);

        Assert.True(result.Ok);
        Assert.Equal("1.1.0", ports.Installed[$"{TenantId}:loyalty"].Version);
        Assert.Contains("migrate:loyalty:1.0.0->1.1.0", ports.Journal);
        Assert.Contains("audit:upgrade:loyalty@1.0.0->1.1.0", ports.Journal);
    }

    [Fact]
    public async Task Upgrade_with_uninstalled_dependency_is_blocked()
    {
        var ports = Ports(withDependency: true);
        ports.Installed[$"{TenantId}:loyalty"] = ("1.0.0", InstallState.Installed, true);
        var svc = new ModuleLifecycleService(ports);

        // 1.1.0 manifest has no deps in this fixture, so target 1.0.0 re-check… instead
        // simulate a new manifest with a missing dependency directly:
        ports.Manifests["loyalty@1.1.0"] = Manifest("loyalty", "1.1.0", ("payment", "^2.0.0"));
        var result = await svc.UpgradeAsync(TenantId, "loyalty", "1.1.0", "u", "Admin", Ct);

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.DependencyMissing, result.Error!.Code);
        Assert.False(ports.Journal.Contains("migrate:loyalty:1.0.0->1.1.0")); // blocked before side effects
    }

    // ---- uninstall guard (R6/R2) ----

    [Fact]
    public void Uninstall_blocked_when_an_installed_dependent_needs_the_module()
    {
        var states = new Dictionary<string, InstallState>(StringComparer.OrdinalIgnoreCase)
        {
            ["payment"] = InstallState.Installed,
            ["loyalty"] = InstallState.Installed,
        };

        var guard = LifecycleGuards.CanUninstall("payment", states,
            (id, _) => id == "loyalty" ? new[] { "payment" } : Array.Empty<string>());

        Assert.False(guard.Ok);
        Assert.Equal(ErrorCodes.DependencyInUse, guard.Error!.Code);
    }

    [Fact]
    public void Uninstall_allowed_when_no_installed_dependent_needs_the_module()
    {
        var states = new Dictionary<string, InstallState>(StringComparer.OrdinalIgnoreCase)
        {
            ["payment"] = InstallState.Installed,
            ["loyalty"] = InstallState.NotInstalled, // not installed → does not block
        };

        var guard = LifecycleGuards.CanUninstall("payment", states,
            (id, _) => id == "loyalty" ? new[] { "payment" } : Array.Empty<string>());

        Assert.True(guard.Ok);
    }
}
