using Microsoft.EntityFrameworkCore;
using Platform.Application;
using Platform.Domain;
using Platform.Infrastructure;
using SharedKernel;

namespace Platform.Api.Services;

// EF-backed lifecycle ports: physical side effects go over HTTP —
// gateway routes via Gateway.Api internal API (source=module-install),
// tenant seed via Auth.Api internal API (idempotent upserts). Failures throw
// so the lifecycle marks the install FAILED per SR-5 (rollback of that module).
public sealed class EfLifecyclePorts(CatalogDbContext db, IHttpClientFactory http, IConfiguration cfg, ILogger<EfLifecyclePorts> log) : IModuleLifecyclePorts
{
    public async Task<TenantView?> GetTenantAsync(string tenantId, CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        return t is null ? null : new TenantView(t.Id, t.Status.ToString());
    }

    public async Task<IReadOnlyList<string>> AvailableVersionsAsync(string moduleId, CancellationToken ct) =>
        await db.ModuleVersions.Where(v => v.ModuleId == moduleId).OrderBy(v => v.Version)
            .Select(v => v.Version).ToListAsync(ct);

    public async Task<ManifestFile?> GetManifestAsync(string moduleId, string version, CancellationToken ct)
    {
        var v = await db.ModuleVersions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ModuleId == moduleId && x.Version == version, ct);
        if (v is null) return null;
        try { return ManifestParser.Parse(v.ManifestYaml); } catch { return null; }
    }

    public async Task<IDictionary<string, InstallState>> GetInstallStatesAsync(string tenantId, CancellationToken ct) =>
        (await db.Installs.Where(i => i.TenantId == tenantId).AsNoTracking().ToListAsync(ct))
            .ToDictionary(i => i.ModuleId, i => i.State, StringComparer.OrdinalIgnoreCase);

    public async Task SaveInstallAsync(string tenantId, string moduleId, string version, InstallState state, bool enabled, string? error, CancellationToken ct)
    {
        var existing = await db.Installs.FindAsync([tenantId, moduleId], ct);
        if (existing is null)
            db.Installs.Add(new TenantModuleInstall
            {
                TenantId = tenantId, ModuleId = moduleId, Version = version,
                State = state, Enabled = enabled, InstalledAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow, Error = error,
            });
        else
        {
            existing.Version = version; existing.State = state; existing.Enabled = enabled;
            existing.Error = error; existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetManifestYamlAsync(string moduleId, string version, CancellationToken ct) =>
        (await db.ModuleVersions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ModuleId == moduleId && x.Version == version, ct))?.ManifestYaml;

    public async Task<string?> GetInstalledVersionAsync(string tenantId, string moduleId, CancellationToken ct) =>
        (await db.Installs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ModuleId == moduleId, ct))?.Version;

    public async Task RunMigrateHookAsync(string moduleId, string fromVersion, string toVersion, CancellationToken ct)
    {
        if (fromVersion == toVersion) return;
        var hookBase = (await db.ModuleVersions.AsNoTracking()
            .Where(x => x.ModuleId == moduleId && x.Version == toVersion)
            .Select(x => x.ArtifactApiUrl).FirstOrDefaultAsync(ct))?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(hookBase))
        {
            log.LogInformation("No artifactApiUrl for {module} — migrate hook skipped", moduleId);
            return;
        }
        using var client = http.CreateClient("lifecycle");
        using var res = await client.PostAsJsonAsync($"{hookBase}/internal/migrate",
            new { fromVersion, toVersion }, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Migrate hook rejected ({(int)res.StatusCode}): {body}");
        }
    }

    public async Task ApplyGatewayRoutesAsync(string tenantId, ManifestFile manifest, bool enabled, CancellationToken ct)
    {
        // A3 fix: the gateway must not guess the module's downstream — send the
        // published artifact API base URL; falls back to registry/config inside
        // the gateway when the artifact URL is absent.
        var artifactApiUrl = await db.ModuleVersions.AsNoTracking()
            .Where(x => x.ModuleId == manifest.Module.Name && x.Version == manifest.Module.Version)
            .Select(x => x.ArtifactApiUrl).FirstOrDefaultAsync(ct);
        // R-GW1/R-GW2: per-tenant state on the X-Internal-Key protected internal API.
        using var res = await SendGatewayAsync(HttpMethod.Put, manifest.Module.Name, tenantId,
            JsonContent.Create(new
            {
                enabled,
                version = manifest.Module.Version,
                baseUrl = artifactApiUrl,
                upstreams = GatewayUpstreams(manifest),
            }), ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Gateway apply failed ({(int)res.StatusCode}): {body}");
        }
        // Platform-level rule audit (never provisioned into tenant databases).
        foreach (var r in manifest.Api.Routes)
        {
            var existing = await db.GatewayRules.FirstOrDefaultAsync(
                x => x.ModuleId == manifest.Module.Name && x.UpstreamTemplate == r.Upstream, ct);
            if (existing is null)
            {
                db.GatewayRules.Add(new GatewayRule
                {
                    Id = Cuid.New("gwr"), ModuleId = manifest.Module.Name,
                    UpstreamTemplate = r.Upstream, DownstreamTemplate = r.Downstream,
                    Methods = System.Text.Json.JsonSerializer.Serialize(r.Methods),
                    Permission = r.RequiredPermission, Enabled = enabled, AppliedAt = DateTimeOffset.UtcNow,
                });
            }
            else
            {
                existing.DownstreamTemplate = r.Downstream;
                existing.Methods = System.Text.Json.JsonSerializer.Serialize(r.Methods);
                existing.Permission = r.RequiredPermission;
                existing.Enabled = enabled;
                existing.AppliedAt = DateTimeOffset.UtcNow;
            }
        }
        await db.SaveChangesAsync(ct);
        log.LogInformation("Gateway routes applied module={module} enabled={enabled}", manifest.Module.Name, enabled);
    }

    public async Task RemoveModuleRoutesAsync(string tenantId, ManifestFile manifest, CancellationToken ct)
    {
        using var res = await SendGatewayAsync(HttpMethod.Delete, manifest.Module.Name, tenantId, null, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Gateway remove failed ({(int)res.StatusCode}): {body}");
        }
        // P3: the platform rule audit stays while any other tenant still has the module.
        var othersInstalled = await db.Installs.AnyAsync(
            x => x.ModuleId == manifest.Module.Name && x.TenantId != tenantId && x.State != InstallState.NotInstalled, ct);
        if (othersInstalled) return;
        db.GatewayRules.RemoveRange(db.GatewayRules.Where(x => x.ModuleId == manifest.Module.Name));
        await db.SaveChangesAsync(ct);
    }

    public static object[] GatewayUpstreams(ManifestFile manifest) =>
        manifest.Api.Routes.Select(r => (object)new
        {
            upstream = r.Upstream, downstream = r.Downstream,
            methods = r.Methods.ToArray(),
        }).ToArray();

    private async Task<HttpResponseMessage> SendGatewayAsync(HttpMethod method, string moduleId, string tenantId, HttpContent? content, CancellationToken ct)
    {
        var baseUrl = (cfg["Gateway:BaseUrl"] ?? "http://localhost:5100").TrimEnd('/');
        using var req = new HttpRequestMessage(method,
            $"{baseUrl}/api/v1/gateway/internal/modules/{Uri.EscapeDataString(moduleId)}/tenants/{Uri.EscapeDataString(tenantId)}")
        { Content = content };
        req.Headers.Add("X-Internal-Key", cfg["InternalApi:Key"]);
        var client = http.CreateClient("lifecycle");
        return await client.SendAsync(req, ct);
    }

    public async Task SeedTenantAsync(string tenantId, ManifestFile manifest, CancellationToken ct)
    {
        var yaml = await GetManifestYamlAsync(manifest.Module.Name, manifest.Module.Version, ct);
        var baseUrl = (cfg["Auth:BaseUrl"] ?? "http://localhost:5122").TrimEnd('/');
        using var client = http.CreateClient("lifecycle");
        using var res = await client.PostAsJsonAsync($"{baseUrl}/internal/seed:apply",
            new { moduleId = manifest.Module.Name, manifestYaml = yaml ?? "", tenantId }, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Tenant seed failed ({(int)res.StatusCode}): {body}");
        }
    }

    public async Task RevokeSeedAsync(string tenantId, ManifestFile manifest, CancellationToken ct)
    {
        var baseUrl = (cfg["Auth:BaseUrl"] ?? "http://localhost:5122").TrimEnd('/');
        using var client = http.CreateClient("lifecycle");
        using var res = await client.PostAsJsonAsync($"{baseUrl}/internal/seed:revoke",
            new { moduleId = manifest.Module.Name, tenantId }, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            if (res.StatusCode == System.Net.HttpStatusCode.Conflict && body.Contains(ErrorCodes.LastAdmin))
                throw new LifecycleRejectedException(ErrorCodes.LastAdmin,
                    $"Uninstalling '{manifest.Module.Name}' would remove the tenant's last active admin; assign another admin first.");
            throw new InvalidOperationException($"Tenant seed revoke failed ({(int)res.StatusCode}): {body}");
        }
    }

    public Task AuditAsync(string? tenantId, string actorId, string actorRole, string action, string resourceType, string resourceId, string? reason, CancellationToken ct)
    {
        db.Audits.Add(new TenantAudit
        {
            Id = Cuid.New("aud"), TenantId = tenantId, ActorId = actorId, ActorRole = actorRole,
            Action = action, ResourceType = resourceType, ResourceId = resourceId, Reason = reason,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return db.SaveChangesAsync(ct);
    }
}
