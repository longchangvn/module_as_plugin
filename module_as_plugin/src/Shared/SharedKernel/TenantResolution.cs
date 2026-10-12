// Tenant resolution order per SR-4 / §5 (multiple_tenant Phase 1):
//  1. X-Tenant-Id header (or route value) → look up by id then slug.
//     Present but unknown → fail closed, no fall-through.
//  2. Host domain match (lowercased, port stripped).
//  3. /t/{slug} path prefix.
//  4. Unresolved.
// Pure logic over caller-supplied lookups so it is unit-testable.
namespace SharedKernel;

public enum TenantResolutionSource { Header, Host, Path }

public sealed record ResolvedTenantRef(TenantResolutionSource Source, string Key);

public static class TenantResolution
{
    public static string NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return "";
        var h = host.Trim().ToLowerInvariant();
        var colon = h.LastIndexOf(':');
        // Strip port (but keep IPv6 literals).
        if (colon > 0 && !h.Contains(']') && h.IndexOf(':') == colon)
            h = h[..colon];
        return h;
    }

    public static string? PathSlug(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0] == "t") return segments[1].ToLowerInvariant();
        return null;
    }

    // Returns the ordered candidate list. The caller looks each key up
    // (id then slug) and stops at the first hit; a header key that misses
    // must fail closed (UNKNOWN_TENANT) without consulting host/path.
    public static IReadOnlyList<ResolvedTenantRef> Candidates(string? headerTenant, string? host, string? path)
    {
        var list = new List<ResolvedTenantRef>();
        if (!string.IsNullOrWhiteSpace(headerTenant))
            list.Add(new ResolvedTenantRef(TenantResolutionSource.Header, headerTenant.Trim()));
        var h = NormalizeHost(host);
        if (!string.IsNullOrEmpty(h))
            list.Add(new ResolvedTenantRef(TenantResolutionSource.Host, h));
        var slug = PathSlug(path);
        if (!string.IsNullOrEmpty(slug))
            list.Add(new ResolvedTenantRef(TenantResolutionSource.Path, slug));
        return list;
    }
}
