using Microsoft.AspNetCore.Routing.Matching;
using PluginHost.Api.Middleware;

namespace PluginHost.Api.Routing;

// Per-tenant version selection (research R7): a plugin candidate is valid only when its runtime is
// the one the tenant shell pinned for this request, so two versions of a module with identical
// routes never produce an ambiguous match and a tenant never reaches another tenant's version.
public sealed class PluginTenantMatcherPolicy : MatcherPolicy, IEndpointSelectorPolicy
{
    public override int Order => 0;

    public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints) =>
        endpoints.Any(e => e.Metadata.GetMetadata<PluginEndpointMetadata>() is not null);

    public Task ApplyAsync(HttpContext httpContext, CandidateSet candidates)
    {
        var pinned = TenantShellMiddleware.PinnedRuntime(httpContext);
        for (var i = 0; i < candidates.Count; i++)
        {
            if (!candidates.IsValidCandidate(i)) continue;
            var meta = candidates[i].Endpoint.Metadata.GetMetadata<PluginEndpointMetadata>();
            if (meta is not null && !ReferenceEquals(meta.Runtime, pinned))
                candidates.SetValidity(i, false);
        }
        return Task.CompletedTask;
    }
}
