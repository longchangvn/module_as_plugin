using System.Collections.Concurrent;

namespace Gateway.Api.Services;

public sealed class TrafficRecorder
{
    private long _total;
    private readonly ConcurrentDictionary<string, long> _byRoute = new();
    public long Total => _total;
    public void Record(string route) { Interlocked.Increment(ref _total); _byRoute.AddOrUpdate(route, 1, (_, v) => v + 1); }
    public void Clear() { Interlocked.Exchange(ref _total, 0); _byRoute.Clear(); }
    public object Snapshot(int windowMinutes) => new
    {
        windowMinutes,
        total = _total,
        routes = _byRoute.Select(kv => new { route = kv.Key, count = kv.Value }).ToList(),
    };
}
