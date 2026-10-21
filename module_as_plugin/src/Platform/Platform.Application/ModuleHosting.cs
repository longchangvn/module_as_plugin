// Module hosting strategies (FR-003, research R2/R9). Each module type owns how its backend is
// reached by the gateway and how it is activated/deactivated/migrated per tenant. The lifecycle
// (ModuleLifecycleService), catalog and gateway never branch on type: adding a type = one
// IModuleHostingStrategy registration (+ SharedKernel IModuleTypeRules).
using SharedKernel;

namespace Platform.Application;

public sealed record GatewayUpstream(string Upstream, string Downstream, string[] Methods);

public sealed record GatewayTarget(string? BaseUrl, IReadOnlyList<GatewayUpstream> Upstreams);

public sealed record ModuleVersionRef(string ModuleId, string Version, string ModuleType, string? ArtifactApiUrl, string? PackageSha256);

public interface IModuleHostingStrategy
{
    string Type { get; }

    // Where the gateway sends /api/v1/{module}/... for this version.
    GatewayTarget GatewayTarget(ManifestFile manifest, ModuleVersionRef version);

    // Make the version serve (enabled) or refuse (disabled) the tenant. Called BEFORE the
    // gateway publishes an enabled route and AFTER it publishes a disabled one (FR-018).
    Task ActivateAsync(string tenantId, ModuleVersionRef version, bool enabled, CancellationToken ct);

    // Tenant no longer has the module. Called AFTER the gateway route is removed.
    Task DeactivateAsync(string tenantId, ModuleVersionRef version, CancellationToken ct);

    // Upgrade data hook, invoked for the target version before it is activated.
    Task MigrateAsync(ModuleVersionRef target, string fromVersion, CancellationToken ct);
}

public sealed class ModuleHostingRegistry
{
    private readonly Dictionary<string, IModuleHostingStrategy> _strategies;

    public ModuleHostingRegistry(IEnumerable<IModuleHostingStrategy> strategies) =>
        _strategies = strategies.ToDictionary(s => ModuleTypeNames.Normalize(s.Type), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Types => _strategies.Keys;

    public IModuleHostingStrategy Get(string? moduleType) =>
        _strategies.TryGetValue(ModuleTypeNames.Normalize(moduleType), out var s)
            ? s
            : throw new InvalidOperationException(
                $"No hosting strategy for module type '{moduleType}'. Supported: {string.Join(", ", _strategies.Keys.Order())}.");
}

// Ordering rules shared by every type (FR-018): never route a tenant to a backend that is not ready.
public sealed class ModuleHostingCoordinator(ModuleHostingRegistry registry)
{
    public GatewayTarget Target(ManifestFile manifest, ModuleVersionRef version) =>
        registry.Get(version.ModuleType).GatewayTarget(manifest, version);

    public async Task ApplyAsync(string tenantId, ManifestFile manifest, ModuleVersionRef version, bool enabled,
        Func<GatewayTarget, Task> publishGateway, CancellationToken ct)
    {
        var strategy = registry.Get(version.ModuleType);
        var target = strategy.GatewayTarget(manifest, version);
        if (!enabled)
        {
            await publishGateway(target);
            await strategy.ActivateAsync(tenantId, version, enabled: false, ct);
            return;
        }
        await strategy.ActivateAsync(tenantId, version, enabled: true, ct);
        try { await publishGateway(target); }
        catch
        {
            // Gateway refused after activation: leave the tenant unserved (spec edge case).
            try { await strategy.ActivateAsync(tenantId, version, enabled: false, CancellationToken.None); }
            catch (Exception) { /* best effort; reconcile converges */ }
            throw;
        }
    }

    // Returns a warning when the backend could not be deactivated (tenant is already unrouted;
    // the host/module reconcile converges), so uninstall does not fail after the route is gone.
    public async Task<string?> RemoveAsync(string tenantId, ModuleVersionRef version, Func<Task> removeGateway, CancellationToken ct)
    {
        await removeGateway();
        try
        {
            await registry.Get(version.ModuleType).DeactivateAsync(tenantId, version, ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"Deactivate of {version.ModuleId}@{version.Version} for tenant {tenantId} failed: {ex.Message}";
        }
    }

    public Task MigrateAsync(ModuleVersionRef target, string fromVersion, CancellationToken ct) =>
        fromVersion == target.Version ? Task.CompletedTask : registry.Get(target.ModuleType).MigrateAsync(target, fromVersion, ct);
}

public static class GatewayUpstreams
{
    public static IReadOnlyList<GatewayUpstream> From(ManifestFile manifest, string downstreamPrefix = "") =>
        manifest.Api.Routes.Select(r => new GatewayUpstream(r.Upstream, downstreamPrefix + r.Downstream, r.Methods.ToArray())).ToList();
}

// ---- remote: standalone host (today's behaviour) ----

public interface IRemoteModuleClient
{
    Task MigrateAsync(string baseUrl, string fromVersion, string toVersion, CancellationToken ct);
}

public sealed class RemoteHostingStrategy(IRemoteModuleClient client) : IModuleHostingStrategy
{
    public string Type => ModuleTypeNames.Remote;

    // Downstream = published artifact URL, else manifest service.baseUrl (gateway falls back
    // to its registry/config when both are absent).
    public GatewayTarget GatewayTarget(ManifestFile manifest, ModuleVersionRef version) =>
        new(string.IsNullOrWhiteSpace(version.ArtifactApiUrl) ? manifest.Service.BaseUrl : version.ArtifactApiUrl,
            GatewayUpstreams.From(manifest));

    public Task ActivateAsync(string tenantId, ModuleVersionRef version, bool enabled, CancellationToken ct) => Task.CompletedTask;

    public Task DeactivateAsync(string tenantId, ModuleVersionRef version, CancellationToken ct) => Task.CompletedTask;

    public Task MigrateAsync(ModuleVersionRef target, string fromVersion, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(target.ArtifactApiUrl)
            ? Task.CompletedTask
            : client.MigrateAsync(target.ArtifactApiUrl, fromVersion, target.Version, ct);
}

// ---- plugin: in-process in the shared Module Host ----

public sealed class PluginHostOptions
{
    // Cluster-internal URL used by the Platform and the Gateway.
    public string BaseUrl { get; set; } = "http://localhost:5300";
    // Browser-facing URL for plugin UI assets (frontend bootstrap).
    public string PublicBaseUrl { get; set; } = "http://localhost:5300";

    public string RemoteEntryUrl(string moduleId, string version) =>
        $"{PublicBaseUrl.TrimEnd('/')}/plugin-assets/{Uri.EscapeDataString(moduleId)}/{Uri.EscapeDataString(version)}/assets/remoteEntry.js";
}

public interface IPluginHostClient
{
    Task ActivateAsync(string moduleId, string tenantId, string version, bool enabled, string packageSha256, CancellationToken ct);
    Task DeactivateAsync(string moduleId, string tenantId, CancellationToken ct);
    Task MigrateAsync(string moduleId, string version, string fromVersion, string packageSha256, CancellationToken ct);
}

public sealed class PluginHostingStrategy(IPluginHostClient host, PluginHostOptions options) : IModuleHostingStrategy
{
    public string Type => ModuleTypeNames.Plugin;

    public GatewayTarget GatewayTarget(ManifestFile manifest, ModuleVersionRef version) =>
        new(options.BaseUrl, GatewayUpstreams.From(manifest, $"/plugins/{version.ModuleId}"));

    public Task ActivateAsync(string tenantId, ModuleVersionRef version, bool enabled, CancellationToken ct) =>
        host.ActivateAsync(version.ModuleId, tenantId, version.Version, enabled, RequireSha(version), ct);

    public Task DeactivateAsync(string tenantId, ModuleVersionRef version, CancellationToken ct) =>
        host.DeactivateAsync(version.ModuleId, tenantId, ct);

    public Task MigrateAsync(ModuleVersionRef target, string fromVersion, CancellationToken ct) =>
        host.MigrateAsync(target.ModuleId, target.Version, fromVersion, RequireSha(target), ct);

    private static string RequireSha(ModuleVersionRef v) =>
        string.IsNullOrWhiteSpace(v.PackageSha256)
            ? throw new InvalidOperationException($"Plugin {v.ModuleId}@{v.Version} has no package; re-register it by package upload.")
            : v.PackageSha256;
}
