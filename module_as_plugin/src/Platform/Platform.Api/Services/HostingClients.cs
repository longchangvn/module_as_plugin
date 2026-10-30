using Platform.Application;

namespace Platform.Api.Services;

// Module Host internal API (X-Internal-Key) — contracts/plugin-host-api.md. Non-success
// responses throw with the host's message so the lifecycle marks the install FAILED.
public sealed class HttpPluginHostClient(IHttpClientFactory http, IConfiguration cfg, PluginHostOptions options) : IPluginHostClient
{
    public Task ActivateAsync(string moduleId, string tenantId, string version, bool enabled, string packageSha256, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, $"/internal/plugins/{Esc(moduleId)}/tenants/{Esc(tenantId)}",
            new { version, enabled, packageSha256 }, "activate", ct);

    public Task DeactivateAsync(string moduleId, string tenantId, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"/internal/plugins/{Esc(moduleId)}/tenants/{Esc(tenantId)}", null, "deactivate", ct);

    public Task MigrateAsync(string moduleId, string version, string fromVersion, string packageSha256, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"/internal/plugins/{Esc(moduleId)}/versions/{Esc(version)}/migrate",
            new { fromVersion, toVersion = version, packageSha256 }, "migrate", ct);

    public Task StageAssetsAsync(string moduleId, string version, string packageSha256, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, $"/internal/plugins/{Esc(moduleId)}/versions/{Esc(version)}/assets",
            new { packageSha256 }, "stage assets", ct);

    private async Task SendAsync(HttpMethod method, string path, object? body, string action, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, $"{options.BaseUrl.TrimEnd('/')}{path}")
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        req.Headers.Add("X-Internal-Key", cfg["InternalApi:Key"]);
        HttpResponseMessage res;
        try { res = await http.CreateClient("plugin-host").SendAsync(req, ct); }
        catch (HttpRequestException ex) { throw new InvalidOperationException($"Module Host unreachable ({action}): {ex.Message}", ex); }
        using (res)
        {
            if (res.IsSuccessStatusCode) return;
            var text = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Module Host {action} failed ({(int)res.StatusCode}): {text}");
        }
    }

    private static string Esc(string s) => Uri.EscapeDataString(s);
}

// Standalone module upgrade hook: POST {artifactApiUrl}/internal/migrate.
public sealed class HttpRemoteModuleClient(IHttpClientFactory http) : IRemoteModuleClient
{
    public async Task MigrateAsync(string baseUrl, string fromVersion, string toVersion, CancellationToken ct)
    {
        using var res = await http.CreateClient("lifecycle").PostAsJsonAsync($"{baseUrl.TrimEnd('/')}/internal/migrate",
            new { fromVersion, toVersion }, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Migrate hook rejected ({(int)res.StatusCode}): {body}");
        }
    }
}
