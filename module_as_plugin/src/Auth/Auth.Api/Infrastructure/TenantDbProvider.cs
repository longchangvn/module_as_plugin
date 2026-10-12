using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Auth.Api.Infrastructure;

// Resolves the TenantDbContext for a request: per-tenant DB (dedicated) or
// schema (shared) when TenantStores is configured, else the legacy singly-
// wired context (no-Postgres dev / pre-provisioning rows with no mode).
// First use per tenant runs MigrateAsync (lazy provisioning) exactly once and
// reports Ready/Failed back to Platform. Secrets come only from server-side
// TenantStores config; the catalog supplies non-secret metadata (NFR4).
public sealed class TenantDbProvider(
    IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg,
    ILogger<TenantDbProvider> log)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private static readonly ConcurrentDictionary<string, bool> Migrated = new();
    private static readonly ConcurrentDictionary<string, DbContextOptions<TenantDbContext>> OptionsCache = new();

    public sealed record StoreMeta(string Status, string Mode, string DbName, string Schema, string? BootstrapAdminEmail, string? BootstrapAdminName);

    public Task<TenantDbContext> ForRequestAsync(HttpContext ctx, TenantDbContext legacy)
    {
        var tid = ctx.User.FindFirst("tenantId")?.Value
            ?? ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault()
            ?? "";
        return ForTenantAsync(tid, legacy);
    }

    public async Task<TenantDbContext> ForTenantAsync(string tenantId, TenantDbContext legacy)
    {
        if (!PerTenantEnabled() || string.IsNullOrWhiteSpace(tenantId))
            return legacy;
        var meta = await GetMetaAsync(tenantId);
        // Legacy rows (no deployment mode) and unknown tenants stay on the
        // shared store; unknown-tenant rejection happens in the caller guard.
        if (meta is null || (meta.Mode != "dedicated" && meta.Mode != "shared"))
            return legacy;
        var options = OptionsCache.GetOrAdd(tenantId, _ => BuildOptions(meta));
        var db = new TenantDbContext(options, meta.Mode == "shared" ? meta.Schema : null);
        if (Migrated.ContainsKey(tenantId))
            return db;
        var sem = Locks.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
        try
        {
            if (!Migrated.ContainsKey(tenantId))
            {
                try
                {
                    await db.Database.MigrateAsync();
                    // Bootstrap admin: first tenant user, seeded once when the
                    // creator supplied an email (passwords are stub-verified
                    // in v1, so any password logs in).
                    if (!string.IsNullOrWhiteSpace(meta.BootstrapAdminEmail) &&
                        !await db.Users.AnyAsync())
                    {
                        var email = meta.BootstrapAdminEmail.Trim();
                        db.Users.Add(new Auth.Api.Domain.TenantUser
                        {
                            Id = SharedKernel.Cuid.New("u"),
                            Email = email,
                            Name = string.IsNullOrWhiteSpace(meta.BootstrapAdminName)
                                ? email.Split('@')[0] : meta.BootstrapAdminName.Trim(),
                            CreatedAt = DateTimeOffset.UtcNow,
                            UpdatedAt = DateTimeOffset.UtcNow,
                        });
                        await db.SaveChangesAsync();
                    }
                    var applied = (await db.Database.GetAppliedMigrationsAsync()).LastOrDefault();
                    await ReportAsync(tenantId, "provisioned", new { schemaVersion = applied });
                    Migrated[tenantId] = true;
                    log.LogInformation("Tenant store ready tenant={tenant} schemaVersion={v}", tenantId, applied);
                }
                catch (Exception ex)
                {
                    await ReportAsync(tenantId, "provision-failed", new { error = ex.Message });
                    await db.DisposeAsync();
                    throw new InvalidOperationException($"Tenant store provisioning failed: {ex.Message}", ex);
                }
            }
        }
        finally
        {
            sem.Release();
        }
        return db;
    }

    private bool PerTenantEnabled() => !string.IsNullOrWhiteSpace(cfg["TenantStores:Host"]);

    private async Task<StoreMeta?> GetMetaAsync(string tenantId)
    {
        var key = $"tenant-meta:{tenantId}";
        if (cache.TryGetValue<StoreMeta>(key, out var cached) && cached is not null)
            return cached;
        try
        {
            var baseUrl = (cfg["Platform:BaseUrl"] ?? "http://localhost:5121").TrimEnd('/');
            using var client = http.CreateClient("platform");
            using var res = await client.GetAsync($"{baseUrl}/internal/tenants/{Uri.EscapeDataString(tenantId)}/status");
            if (!res.IsSuccessStatusCode)
                return null;
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
            var data = doc.RootElement.GetProperty("data");
            var meta = new StoreMeta(
                data.GetProperty("status").GetString() ?? "",
                (data.GetProperty("deploymentMode").GetString() ?? "").ToLowerInvariant(),
                data.GetProperty("dbName").GetString() ?? "",
                data.GetProperty("schema").GetString() ?? "public",
                data.TryGetProperty("bootstrapAdminEmail", out var be) ? be.GetString() : null,
                data.TryGetProperty("bootstrapAdminName", out var bn) ? bn.GetString() : null);
            cache.Set(key, meta, TimeSpan.FromMinutes(10)); // identity/shape is immutable
            return meta;
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
        {
            log.LogWarning("Tenant metadata unavailable for {tenant}: {error}", tenantId, ex.Message);
            return null;
        }
    }

    private DbContextOptions<TenantDbContext> BuildOptions(StoreMeta meta)
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = cfg["TenantStores:Host"],
            Port = int.TryParse(cfg["TenantStores:Port"], out var p) ? p : 5432,
            Username = cfg["TenantStores:Username"],
            Password = cfg["TenantStores:Password"],
            Database = meta.Mode == "shared" ? cfg["TenantStores:SharedDatabase"] : meta.DbName,
        };
        // Belt-and-braces with the stamping generator: unqualified DDL lands
        // in the tenant schema via search_path (indexes always follow their
        // table's schema, regardless of generator dispatch).
        if (meta.Mode == "shared")
            csb.SearchPath = meta.Schema;
        var builder = new DbContextOptionsBuilder<TenantDbContext>()
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, TenantModelCacheKeyFactory>();
        if (meta.Mode == "shared")
        {
            ((Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsBuilderInfrastructure)builder)
                .AddOrUpdateExtension(new TenantSchemaOptionsExtension(meta.Schema));
            builder
                .ReplaceService<IMigrationsSqlGenerator, TenantSchemaMigrationsSqlGenerator>()
                .UseNpgsql(csb.ToString(), o => o.MigrationsHistoryTable("__EFMigrationsHistory", meta.Schema));
        }
        else
        {
            builder.UseNpgsql(csb.ToString());
        }
        return builder.Options;
    }

    private async Task ReportAsync(string tenantId, string action, object body)
    {
        try
        {
            var baseUrl = (cfg["Platform:BaseUrl"] ?? "http://localhost:5121").TrimEnd('/');
            using var client = http.CreateClient("platform");
            using var res = await client.PostAsJsonAsync(
                $"{baseUrl}/internal/tenants/{Uri.EscapeDataString(tenantId)}/{action}", body);
            res.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            log.LogWarning("Provision callback {action} failed for {tenant}: {error}", action, tenantId, ex.Message);
        }
    }
}
