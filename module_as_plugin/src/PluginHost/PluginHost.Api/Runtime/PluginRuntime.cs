using System.Diagnostics;
using ModulePlatform.Plugin.Abstractions;
using PluginHost.Api.Loading;

namespace PluginHost.Api.Runtime;

public enum PluginRuntimeState { Active, Draining, Disposed }

// A loaded (module, version): own load context, DI container and endpoints. Shared by every
// tenant whose shell maps the module to this version (data-model "Runtime").
public sealed class PluginRuntime : IAsyncDisposable
{
    private int inFlight;
    private int state; // PluginRuntimeState
    private PluginLoadContext? loadContext;

    internal PluginRuntime(PluginPackage package, PluginLoadContext loadContext)
    {
        ModuleId = package.ModuleId;
        Version = package.Version;
        PackageSha256 = package.PackageSha256;
        ContentRoot = package.ContentRoot;
        this.loadContext = loadContext;
    }

    public string ModuleId { get; }
    public string Version { get; }
    public string PackageSha256 { get; }
    public string ContentRoot { get; }
    public string Key => PluginIds.RuntimeKey(ModuleId, Version);
    public DateTimeOffset LoadedAt { get; } = DateTimeOffset.UtcNow;
    public PluginLoadContext? LoadContext => loadContext;
    public IModulePlugin Plugin { get; internal set; } = null!;
    public ServiceProvider Services { get; internal set; } = null!;
    public PluginContext Context { get; internal set; } = null!;
    public IReadOnlyList<Endpoint> Endpoints { get; internal set; } = [];
    public int InFlight => Volatile.Read(ref inFlight);
    public PluginRuntimeState State => (PluginRuntimeState)Volatile.Read(ref state);

    // Request admission. Increment first, then check state: paired with BeginDrain (state first,
    // then read InFlight) at least one side observes the other, so a drain never misses a request.
    public bool TryEnter()
    {
        Interlocked.Increment(ref inFlight);
        if (State == PluginRuntimeState.Active) return true;
        Interlocked.Decrement(ref inFlight);
        return false;
    }

    public void Exit() => Interlocked.Decrement(ref inFlight);

    public void BeginDrain() =>
        Interlocked.CompareExchange(ref state, (int)PluginRuntimeState.Draining, (int)PluginRuntimeState.Active);

    // Waits for in-flight requests (bounded by timeout); true when drained cleanly.
    public async Task<bool> WaitDrainedAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        while (InFlight > 0)
        {
            if (sw.Elapsed >= timeout) return false;
            await Task.Delay(25, ct);
        }
        return true;
    }

    public async Task<bool> DrainAndDisposeAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        BeginDrain();
        var drained = await WaitDrainedAsync(timeout, ct);
        await DisposeAsync();
        return drained;
    }

    // Disposes the plugin container and unloads the load context. The context is collected once
    // nothing references plugin types any more (endpoints must already be out of the data source).
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref state, (int)PluginRuntimeState.Disposed) == (int)PluginRuntimeState.Disposed) return;
        if (Services is not null) await Services.DisposeAsync();
        Endpoints = [];
        Plugin = null!;
        Context = null!;
        var alc = Interlocked.Exchange(ref loadContext, null);
        alc?.Unload();
    }
}
