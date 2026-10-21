using Microsoft.Extensions.Primitives;
using PluginHost.Api.Runtime;

namespace PluginHost.Api.Routing;

// Tags every plugin endpoint with the runtime that owns it (matcher policy + services middleware).
public sealed class PluginEndpointMetadata(PluginRuntime runtime)
{
    public PluginRuntime Runtime { get; } = runtime;
}

// Dynamic endpoint source (research R7): Update swaps the list and fires the change token, and
// ASP.NET Core rebuilds its matcher atomically — no restart, in-flight requests unaffected.
public sealed class PluginEndpointDataSource : EndpointDataSource
{
    private readonly object gate = new();
    private IReadOnlyList<Endpoint> endpoints = [];
    private CancellationTokenSource cts = new();
    private IChangeToken changeToken;

    public PluginEndpointDataSource() => changeToken = new CancellationChangeToken(cts.Token);

    public override IReadOnlyList<Endpoint> Endpoints => Volatile.Read(ref endpoints);

    public override IChangeToken GetChangeToken()
    {
        lock (gate) return changeToken;
    }

    public void Update(IReadOnlyList<Endpoint> next)
    {
        CancellationTokenSource previous;
        lock (gate)
        {
            previous = cts;
            Volatile.Write(ref endpoints, next);
            cts = new CancellationTokenSource();
            changeToken = new CancellationChangeToken(cts.Token);
        }
        // Not disposed: late consumers may still register on the old (already fired) token.
        previous.Cancel();
    }
}
