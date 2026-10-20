using System.Net.Http.Json;
using Gateway.Api.Endpoints;
using Gateway.Api.Models;

namespace Gateway.Api.Services;

// G4 reconcile: pull the Platform's per-tenant module snapshot on startup and
// every ModuleReconcile:IntervalSeconds (default 300), replace the gateway
// state, log drift. Heals failed/missed pushes and migrates legacy v1 state.
// Platform down → keep serving the last known state (NFR6), retry sooner.
public sealed class ModuleStateReconciler(
    GatewayStore store, IHttpClientFactory http, IConfiguration cfg, ILogger<ModuleStateReconciler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(10, cfg.GetValue("ModuleReconcile:IntervalSeconds", 300)));
        var retry = TimeSpan.FromSeconds(Math.Max(1, cfg.GetValue("ModuleReconcile:RetrySeconds", 15)));
        while (!ct.IsCancellationRequested)
        {
            var ok = await ReconcileOnceAsync(ct);
            try { await Task.Delay(ok ? interval : retry, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    public async Task<bool> ReconcileOnceAsync(CancellationToken ct)
    {
        var generation = store.PushGeneration;
        ModuleStatesSnapshot? snapshot;
        try
        {
            var baseUrl = (cfg["Platform:BaseUrl"] ?? "http://localhost:5121").TrimEnd('/');
            using var client = http.CreateClient("platform");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/internal/gateway/module-states");
            req.Headers.Add("X-Internal-Key", cfg["InternalApi:Key"]);
            using var res = await client.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            snapshot = (await res.Content.ReadFromJsonAsync<Envelope>(ct))?.Data;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (ct.IsCancellationRequested) return false;
            log.LogWarning("module state reconcile: platform unavailable ({error}); keeping current state", ex.Message);
            return false;
        }
        var invalid = snapshot is null ? "empty response" : AdminEndpoints.SnapshotError(snapshot);
        if (invalid is not null)
        {
            log.LogWarning("module state reconcile: invalid snapshot ({error}); keeping current state", invalid);
            return false;
        }
        var (ok, skipped, drift, err) = await store.ReplaceModuleStatesAsync(ModuleTenantState.FromSnapshot(snapshot!.Modules), generation);
        if (!ok) { log.LogError("module state reconcile: apply failed: {error}", err); return false; }
        if (skipped) { log.LogInformation("module state reconcile: newer push arrived during fetch; retrying"); return false; }
        if (drift > 0) log.LogWarning("module state reconcile: repaired drift={drift} (module, tenant) entries", drift);
        else log.LogDebug("module state reconcile: in sync");
        return true;
    }

    private sealed record Envelope(ModuleStatesSnapshot? Data);
}
