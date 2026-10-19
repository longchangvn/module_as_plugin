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
public sealed record ModuleRoutesRequest(string ModuleId, List<ModuleUpstream> Upstreams, bool Enabled, bool Remove = false, string? BaseUrl = null);
public sealed record ModuleUpstream(string Upstream, string Downstream, string[] Methods);
