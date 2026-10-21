using System.Text.RegularExpressions;

namespace PluginHost.Api;

// Resolved PluginHost:* settings. CacheRoot is made absolute against the content root.
public sealed record PluginHostOptions(string CacheRoot, TimeSpan DrainTimeout, TimeSpan ReconcileInterval, bool ReconcileEnabled)
{
    public static PluginHostOptions From(IConfiguration cfg, IHostEnvironment env) => new(
        Path.GetFullPath(Path.Combine(env.ContentRootPath, cfg["PluginHost:CacheRoot"] ?? "./plugin-cache")),
        TimeSpan.FromSeconds(Math.Max(0, cfg.GetValue("PluginHost:DrainTimeoutSeconds", 30))),
        TimeSpan.FromMinutes(Math.Max(1, cfg.GetValue("PluginHost:ReconcileIntervalMinutes", 5))),
        // Off switch for tests / standalone runs without a Platform.
        cfg.GetValue("PluginHost:ReconcileEnabled", true));
}

// Identifier rules shared by the internal API, the cache layout and the asset endpoint:
// module/tenant ids and versions become path segments, so they must be path-safe.
public static partial class PluginIds
{
    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$")]
    private static partial Regex IdRegex();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}$")]
    private static partial Regex VersionRegex();

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Regex();

    public static bool IsValidId(string? id) => id is not null && IdRegex().IsMatch(id);
    public static bool IsValidVersion(string? v) => v is not null && VersionRegex().IsMatch(v) && !v.Contains("..");
    public static bool IsValidSha256(string? sha) => sha is not null && Sha256Regex().IsMatch(sha);
    public static string RuntimeKey(string moduleId, string version) => $"{moduleId}@{version}";
}
