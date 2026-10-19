// Per-tenant permission version counter — A-BR5 cache invalidation.
// The resolve endpoint caches effective permissions for 60s; every seed
// apply/revoke bumps the tenant's version, which changes the cache key and
// makes stale entries unreachable (they expire by TTL).
using System.Collections.Concurrent;

namespace Auth.Api.Infrastructure;

public sealed class PermissionVersionCache
{
    private readonly ConcurrentDictionary<string, long> _versions = new(StringComparer.Ordinal);

    public long For(string tenantId) => _versions.GetOrAdd(tenantId, _ => 0L);

    public void Bump(string tenantId) => _versions.AddOrUpdate(tenantId, 1L, (_, v) => v + 1);
}
