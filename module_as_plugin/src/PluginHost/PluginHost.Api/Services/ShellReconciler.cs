using System.Net.Http.Json;
using PluginHost.Api.Runtime;
using PluginHost.Api.Shells;

namespace PluginHost.Api.Services;

// Reconcile (research R9): pull GET /internal/plugin-host/states on start-up (retry with backoff
// until the Platform answers) and every PluginHost:ReconcileIntervalMinutes, then replace the
// tenant shells. Heals missed pushes after a restart. Platform down → keep the current shells.
public sealed class ShellReconciler(
    TenantShellRegistry registry, IModuleAssetSource assets, IHttpClientFactory http, IConfiguration cfg, PluginHostOptions options,
    ILogger<ShellReconciler> log)
    : BackgroundService
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.ReconcileEnabled)
        {
            log.LogInformation("plugin.reconcile disabled (PluginHost:ReconcileEnabled=false)");
            return;
        }
        var backoff = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            bool ok;
            try { ok = await ReconcileOnceAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                log.LogError(ex, "plugin.reconcile result=error");
                ok = false;
            }
            var delay = ok ? options.ReconcileInterval : backoff;
            backoff = ok ? TimeSpan.FromSeconds(2) : TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    public async Task<bool> ReconcileOnceAsync(CancellationToken ct)
    {
        var generation = registry.PushGeneration;
        StatesData? data;
        try
        {
            var baseUrl = (cfg["Platform:BaseUrl"] ?? "http://localhost:5121").TrimEnd('/');
            using var client = http.CreateClient(PlatformPackageClient.HttpClientName);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/internal/plugin-host/states");
            req.Headers.Add("X-Internal-Key", cfg["InternalApi:Key"]);
            using var res = await client.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            data = (await res.Content.ReadFromJsonAsync<StatesEnvelope>(ct))?.Data;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            log.LogWarning("plugin.reconcile result=unavailable error={error}; keeping current shells", ex.Message);
            return false;
        }
        if (data?.Plugins is null)
        {
            log.LogWarning("plugin.reconcile result=invalid error=empty response; keeping current shells");
            return false;
        }
        var desired = new List<DesiredPlugin>();
        foreach (var p in data.Plugins)
        {
            if (!PluginIds.IsValidId(p.ModuleId) || !PluginIds.IsValidVersion(p.Version) || !PluginIds.IsValidSha256(p.PackageSha256)
                || p.Tenants?.Any(t => !PluginIds.IsValidId(t.TenantId)) == true)
            {
                log.LogWarning("plugin.reconcile result=invalid entry={module}@{version}; keeping current shells", p.ModuleId, p.Version);
                return false;
            }
            desired.Add(new DesiredPlugin(p.ModuleId!, p.Version!, p.PackageSha256!,
                (p.Tenants ?? []).Select(t => new DesiredTenant(t.TenantId!, t.Enabled)).ToList()));
        }

        var assetFailures = await EnsureAssetsAsync(data.Assets ?? [], ct);
        var result = await registry.ReplaceAsync(desired, generation, ct);
        if (result.Skipped)
        {
            log.LogInformation("plugin.reconcile result=skipped reason=newer push during fetch");
            return false;
        }
        foreach (var failure in result.Failures) log.LogError("plugin.reconcile load failed: {failure}", failure);
        if (result.Drift > 0) log.LogWarning("plugin.reconcile result=repaired drift={drift}", result.Drift);
        else log.LogDebug("plugin.reconcile result=in-sync");
        return result.Failures.Count == 0 && assetFailures == 0;
    }

    // Hybrid UIs: keep every in-use version cached (re-downloads after a cache wipe). A failure only
    // affects that module's UI; it is logged and retried on the next (backed-off) reconcile.
    private async Task<int> EnsureAssetsAsync(List<StateAsset> wanted, CancellationToken ct)
    {
        var failures = 0;
        foreach (var a in wanted)
        {
            if (!PluginIds.IsValidId(a.ModuleId) || !PluginIds.IsValidVersion(a.Version) || !PluginIds.IsValidSha256(a.PackageSha256))
            {
                log.LogWarning("plugin.reconcile assets invalid entry={module}@{version}", a.ModuleId, a.Version);
                failures++;
                continue;
            }
            try { await assets.EnsureAssetsAsync(a.ModuleId!, a.Version!, a.PackageSha256!, ct); }
            catch (Exception ex) when (ex is PluginLoadException or PluginPackageUnavailableException)
            {
                log.LogError("plugin.reconcile assets failed {module}@{version}: {error}", a.ModuleId, a.Version, ex.Message);
                failures++;
            }
        }
        return failures;
    }

    private sealed record StatesEnvelope(StatesData? Data);
    private sealed record StatesData(List<StatePlugin>? Plugins, List<StateAsset>? Assets);
    private sealed record StateAsset(string? ModuleId, string? Version, string? PackageSha256);
    private sealed record StatePlugin(string? ModuleId, string? Version, string? PackageSha256, List<StateTenant>? Tenants);
    private sealed record StateTenant(string? TenantId, bool Enabled);
}
