namespace Gateway.Api.Models;

public enum ModuleState { NotInstalled, Enabled, Disabled }

public sealed record GatewaySummary(
    string ServiceName,
    string Environment,
    int ServiceCount,
    int RouteCount,
    long TotalRequests,
    string ConfigPath,
    bool ExternalFileModified);

public sealed record ServiceUpsert(string Name, string Host, int Port, string Scheme, string? Description);
public sealed record RouteUpsert(string UpstreamPathTemplate, string DownstreamPathTemplate, string[] Methods, int Priority, string ServiceId);
public sealed record ModuleUpstream(string Upstream, string Downstream, string[] Methods);

// Internal API (X-Internal-Key): per-tenant module state pushed by the Platform.
public sealed record ModuleTenantUpsert(bool Enabled, string Version, string? BaseUrl, List<ModuleUpstream>? Upstreams);

// Full-state snapshot (Platform GET /internal/gateway/module-states, gateway PUT modules:snapshot).
public sealed record ModuleStatesSnapshot(List<ModuleSnapshot> Modules);
public sealed record ModuleSnapshot(string ModuleId, List<VersionSnapshot> Versions, List<TenantSnapshot> Tenants);
public sealed record VersionSnapshot(string Version, string? BaseUrl, List<ModuleUpstream> Upstreams);
public sealed record TenantSnapshot(string TenantId, bool Enabled, string Version);
