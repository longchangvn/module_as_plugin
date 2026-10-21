using System.Collections.Immutable;
using PluginHost.Api.Routing;
using PluginHost.Api.Runtime;

namespace PluginHost.Api.Shells;

public sealed record ShellEntry(string Version, bool Enabled);

public enum ShellDecisionKind { NotInstalled, Disabled, Enabled }

public readonly record struct ShellDecision(ShellDecisionKind Kind, PluginRuntime? Runtime = null);

// Desired state from the Platform reconcile snapshot (GET /internal/plugin-host/states).
public sealed record DesiredPlugin(string ModuleId, string Version, string PackageSha256, IReadOnlyList<DesiredTenant> Tenants);

public sealed record DesiredTenant(string TenantId, bool Enabled);

public sealed record ReconcileResult(bool Skipped, int Drift, IReadOnlyList<string> Failures);

public sealed record RuntimeView(
    string ModuleId, string Version, string PackageSha256, string State, int InFlight, DateTimeOffset LoadedAt, IReadOnlyList<string> Tenants);

public sealed record RegistryView(IReadOnlyList<RuntimeView> Runtimes, IReadOnlyDictionary<string, IReadOnlyDictionary<string, ShellEntry>> Tenants);

// Tenant shells (research R8): an immutable snapshot tenants[tenantId][moduleId] = {version, enabled}
// + runtimes["module@version"], swapped atomically. Single writer (SemaphoreSlim), lock-free reads.
// Runtimes are loaded BEFORE the swap, so a failed load leaves every shell unchanged. Runtimes no
// longer referenced are drained (in-flight → 0, bounded) and then disposed / unloaded.
public sealed class TenantShellRegistry(
    IPluginPackageSource packages,
    PluginRuntimeFactory factory,
    PluginEndpointDataSource dataSource,
    PluginHostOptions options,
    ILogger<TenantShellRegistry> log) : IAsyncDisposable
{
    private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;
    private static readonly ImmutableDictionary<string, ShellEntry> NoModules = ImmutableDictionary.Create<string, ShellEntry>(Cmp);

    private sealed record Snapshot(
        ImmutableDictionary<string, ImmutableDictionary<string, ShellEntry>> Tenants,
        ImmutableDictionary<string, PluginRuntime> Runtimes)
    {
        public static readonly Snapshot Empty = new(
            ImmutableDictionary.Create<string, ImmutableDictionary<string, ShellEntry>>(Cmp),
            ImmutableDictionary.Create<string, PluginRuntime>(Cmp));
    }

    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly object endpointsGate = new();
    private readonly HashSet<PluginRuntime> draining = new(ReferenceEqualityComparer.Instance); // guarded by endpointsGate
    private readonly List<Task> releases = []; // guarded by itself
    private volatile Snapshot snapshot = Snapshot.Empty;
    private long pushGeneration;

    // Incremented by every push (Apply/Remove); reconcile skips when it changed since its fetch.
    public long PushGeneration => Interlocked.Read(ref pushGeneration);

    public ShellDecision Decide(string tenantId, string moduleId)
    {
        var s = snapshot;
        if (!s.Tenants.TryGetValue(tenantId, out var modules) || !modules.TryGetValue(moduleId, out var entry))
            return new(ShellDecisionKind.NotInstalled);
        if (!entry.Enabled) return new(ShellDecisionKind.Disabled);
        return s.Runtimes.TryGetValue(PluginIds.RuntimeKey(moduleId, entry.Version), out var runtime)
            ? new(ShellDecisionKind.Enabled, runtime)
            : new(ShellDecisionKind.NotInstalled);
    }

    public async Task<PluginRuntime> ApplyAsync(string moduleId, string tenantId, string version, bool enabled, string packageSha256, CancellationToken ct = default)
    {
        await writer.WaitAsync(ct);
        try
        {
            Interlocked.Increment(ref pushGeneration);
            var current = snapshot;
            var runtime = await EnsureLoadedAsync(current.Runtimes, moduleId, version, packageSha256, ct);
            var modules = current.Tenants.GetValueOrDefault(tenantId) ?? NoModules;
            var tenants = current.Tenants.SetItem(tenantId, modules.SetItem(moduleId, new ShellEntry(version, enabled)));
            Swap(tenants, current.Runtimes.SetItem(runtime.Key, runtime));
            log.LogInformation("plugin.shell.apply module={module} tenant={tenant} version={version} enabled={enabled}", moduleId, tenantId, version, enabled);
            return runtime;
        }
        finally { writer.Release(); }
    }

    public async Task<bool> RemoveAsync(string moduleId, string tenantId, CancellationToken ct = default)
    {
        await writer.WaitAsync(ct);
        try
        {
            Interlocked.Increment(ref pushGeneration);
            var current = snapshot;
            if (!current.Tenants.TryGetValue(tenantId, out var modules) || !modules.ContainsKey(moduleId)) return false;
            var rest = modules.Remove(moduleId);
            Swap(rest.IsEmpty ? current.Tenants.Remove(tenantId) : current.Tenants.SetItem(tenantId, rest), current.Runtimes);
            log.LogInformation("plugin.shell.remove module={module} tenant={tenant}", moduleId, tenantId);
            return true;
        }
        finally { writer.Release(); }
    }

    // Loads (and pins until the next swap) a runtime without changing any shell — used by migrate.
    public async Task<PluginRuntime> EnsureRuntimeAsync(string moduleId, string version, string packageSha256, CancellationToken ct = default)
    {
        await writer.WaitAsync(ct);
        try
        {
            var current = snapshot;
            var runtime = await EnsureLoadedAsync(current.Runtimes, moduleId, version, packageSha256, ct);
            if (!current.Runtimes.TryGetValue(runtime.Key, out var existing) || !ReferenceEquals(existing, runtime))
                Swap(current.Tenants, current.Runtimes.SetItem(runtime.Key, runtime), pin: runtime.Key);
            return runtime;
        }
        finally { writer.Release(); }
    }

    // Full replace from the Platform snapshot. Skipped when a push happened after expectedGeneration
    // was read (the snapshot may be older than that push). A plugin version that fails to load keeps
    // its tenants on whatever they currently run; the failure is reported, the rest is applied.
    public async Task<ReconcileResult> ReplaceAsync(IReadOnlyList<DesiredPlugin> desired, long expectedGeneration, CancellationToken ct = default)
    {
        await writer.WaitAsync(ct);
        try
        {
            if (PushGeneration != expectedGeneration) return new(true, 0, []);
            var current = snapshot;
            var runtimes = current.Runtimes;
            var failures = new List<string>();
            var failed = new HashSet<string>(Cmp);
            foreach (var plugin in desired)
            {
                var key = PluginIds.RuntimeKey(plugin.ModuleId, plugin.Version);
                if (failed.Contains(key)) continue;
                try
                {
                    var runtime = await EnsureLoadedAsync(runtimes, plugin.ModuleId, plugin.Version, plugin.PackageSha256, ct);
                    runtimes = runtimes.SetItem(runtime.Key, runtime);
                }
                catch (Exception ex) when (ex is PluginLoadException or PluginPackageUnavailableException)
                {
                    failed.Add(key);
                    failures.Add($"{key}: {ex.Message}");
                }
                catch
                {
                    // Aborted (e.g. shutdown): nothing was swapped, unload what this call loaded.
                    foreach (var loaded in runtimes.Values.Where(r => !current.Runtimes.Values.Contains(r)))
                        await loaded.DisposeAsync();
                    throw;
                }
            }

            var next = new Dictionary<string, Dictionary<string, ShellEntry>>(Cmp);
            foreach (var plugin in desired)
            {
                var loadFailed = failed.Contains(PluginIds.RuntimeKey(plugin.ModuleId, plugin.Version));
                foreach (var tenant in plugin.Tenants)
                {
                    ShellEntry? entry = new(plugin.Version, tenant.Enabled);
                    if (loadFailed)
                        entry = current.Tenants.GetValueOrDefault(tenant.TenantId)?.GetValueOrDefault(plugin.ModuleId);
                    if (entry is null) continue;
                    if (!next.TryGetValue(tenant.TenantId, out var modules)) next[tenant.TenantId] = modules = new(Cmp);
                    modules[plugin.ModuleId] = entry;
                }
            }
            var tenants = next.ToImmutableDictionary(t => t.Key, t => t.Value.ToImmutableDictionary(Cmp), Cmp);
            var drift = CountDrift(current.Tenants, tenants);
            Swap(tenants, runtimes);
            return new(false, drift, failures);
        }
        finally { writer.Release(); }
    }

    public RegistryView View()
    {
        var s = snapshot;
        List<PluginRuntime> drainingNow;
        lock (endpointsGate) drainingNow = draining.Where(d => !s.Runtimes.Values.Contains(d)).ToList();
        var views = s.Runtimes.Values.Concat(drainingNow)
            .Select(r => new RuntimeView(r.ModuleId, r.Version, r.PackageSha256, r.State.ToString().ToUpperInvariant(), r.InFlight, r.LoadedAt,
                s.Tenants.Where(t => t.Value.TryGetValue(r.ModuleId, out var e) && Cmp.Equals(e.Version, r.Version)
                        && s.Runtimes.TryGetValue(r.Key, out var owner) && ReferenceEquals(owner, r))
                    .Select(t => t.Key).Order(StringComparer.Ordinal).ToList()))
            .OrderBy(v => v.ModuleId, StringComparer.Ordinal).ThenBy(v => v.Version, StringComparer.Ordinal)
            .ToList();
        var tenants = s.Tenants.ToDictionary(t => t.Key,
            t => (IReadOnlyDictionary<string, ShellEntry>)t.Value.ToDictionary(m => m.Key, m => m.Value, Cmp), Cmp);
        return new(views, tenants);
    }

    // Completes when every runtime released so far is drained and unloaded (tests, shutdown).
    public Task WhenReleasedAsync()
    {
        lock (releases) return Task.WhenAll(releases.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        await WhenReleasedAsync();
        foreach (var runtime in snapshot.Runtimes.Values) await runtime.DisposeAsync();
        writer.Dispose();
    }

    private async Task<PluginRuntime> EnsureLoadedAsync(
        ImmutableDictionary<string, PluginRuntime> runtimes, string moduleId, string version, string packageSha256, CancellationToken ct)
    {
        if (runtimes.TryGetValue(PluginIds.RuntimeKey(moduleId, version), out var existing)
            && existing.State == PluginRuntimeState.Active
            && Cmp.Equals(existing.PackageSha256, packageSha256))
            return existing;
        var package = await packages.GetAsync(moduleId, version, packageSha256, ct);
        return factory.Create(package);
    }

    // Writer only. Publishes endpoints of the new runtime set (plus still-draining ones) BEFORE the
    // snapshot becomes visible, so a request never resolves a runtime whose endpoints are missing.
    private void Swap(
        ImmutableDictionary<string, ImmutableDictionary<string, ShellEntry>> tenants,
        ImmutableDictionary<string, PluginRuntime> candidates,
        string? pin = null)
    {
        var referenced = new HashSet<string>(tenants.SelectMany(t => t.Value.Select(m => PluginIds.RuntimeKey(m.Key, m.Value.Version))), Cmp);
        if (pin is not null) referenced.Add(pin);
        var keep = candidates.Where(kv => referenced.Contains(kv.Key)).ToImmutableDictionary(kv => kv.Key, kv => kv.Value, Cmp);
        var next = new Snapshot(tenants, keep);
        var kept = new HashSet<PluginRuntime>(keep.Values, ReferenceEqualityComparer.Instance);
        var dropped = snapshot.Runtimes.Values.Concat(candidates.Values)
            .Where(r => !kept.Contains(r)).Distinct<PluginRuntime>(ReferenceEqualityComparer.Instance).ToList();
        lock (endpointsGate)
        {
            foreach (var runtime in dropped) draining.Add(runtime);
            PublishEndpoints(next);
            snapshot = next;
        }
        foreach (var runtime in dropped) StartRelease(runtime);
    }

    private void PublishEndpoints(Snapshot s) =>
        dataSource.Update(s.Runtimes.Values.Concat(draining).Distinct<PluginRuntime>(ReferenceEqualityComparer.Instance)
            .SelectMany(r => r.Endpoints).ToList());

    private void StartRelease(PluginRuntime runtime)
    {
        // BeginDrain only after the new snapshot is visible: a request that still admitted itself
        // on the old snapshot is counted (TryEnter), later ones see Draining and re-resolve.
        runtime.BeginDrain();
        var task = Task.Run(() => ReleaseAsync(runtime));
        lock (releases)
        {
            releases.RemoveAll(t => t.IsCompleted);
            releases.Add(task);
        }
    }

    private async Task ReleaseAsync(PluginRuntime runtime)
    {
        try
        {
            var drained = await runtime.WaitDrainedAsync(options.DrainTimeout);
            lock (endpointsGate)
            {
                draining.Remove(runtime);
                PublishEndpoints(snapshot);
            }
            await runtime.DisposeAsync();
            PluginHostMetrics.UnloadCount.Add(1);
            if (drained)
                log.LogInformation("plugin.unload module={module} version={version} result=ok", runtime.ModuleId, runtime.Version);
            else
                log.LogWarning("plugin.unload module={module} version={version} result=timeout inFlight={inFlight}", runtime.ModuleId, runtime.Version, runtime.InFlight);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "plugin.unload module={module} version={version} result=error", runtime.ModuleId, runtime.Version);
        }
    }

    // Number of (tenant, module) entries added, removed or changed.
    private static int CountDrift(
        ImmutableDictionary<string, ImmutableDictionary<string, ShellEntry>> before,
        ImmutableDictionary<string, ImmutableDictionary<string, ShellEntry>> after)
    {
        static ShellEntry? Find(ImmutableDictionary<string, ImmutableDictionary<string, ShellEntry>> s, (string T, string M) k) =>
            s.GetValueOrDefault(k.T)?.GetValueOrDefault(k.M);
        return before.Concat(after)
            .SelectMany(t => t.Value.Keys.Select(m => (T: t.Key.ToLowerInvariant(), M: m.ToLowerInvariant())))
            .Distinct()
            .Count(k => Find(before, k) is var b && Find(after, k) is var a
                && !(a is not null && b is not null && a.Enabled == b.Enabled && Cmp.Equals(a.Version, b.Version)));
    }
}
