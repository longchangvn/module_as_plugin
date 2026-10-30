using System.Security.Cryptography;
using System.Text;
using PluginHost.Api.Runtime;
using PluginHost.Api.Shells;
using SharedKernel;

namespace PluginHost.Api.Endpoints;

public sealed record ActivateRequest(string? Version, bool? Enabled, string? PackageSha256);

public sealed record MigrateRequest(string? FromVersion, string? ToVersion, string? PackageSha256);

public sealed record StageAssetsRequest(string? PackageSha256);

// Platform → Module Host protocol (contracts/plugin-host-api.md, research R9). X-Internal-Key on
// every call; not routed by the gateway.
public static class InternalEndpoints
{
    public const string MigrateFailed = "PLUGIN_MIGRATE_FAILED";

    public static void Map(IEndpointRouteBuilder app)
    {
        var cfg = app.ServiceProvider.GetRequiredService<IConfiguration>();
        var api = app.MapGroup("/internal/plugins")
            .AddEndpointFilter(async (fc, next) => IsInternalCaller(fc.HttpContext, cfg)
                ? await next(fc)
                : ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid internal key.", 401));

        api.MapPut("/{moduleId}/tenants/{tenantId}", async (
            TenantShellRegistry registry, string moduleId, string tenantId, ActivateRequest? req, CancellationToken ct) =>
        {
            if (ValidateIds(moduleId, tenantId) is { } invalid) return invalid;
            if (!PluginIds.IsValidVersion(req?.Version))
                return Invalid("version", "Required: a non-empty, path-safe version (e.g. 1.0.0).");
            if (!PluginIds.IsValidSha256(req!.PackageSha256))
                return Invalid("packageSha256", "Required: 64-character hex SHA-256 of the package.");
            var enabled = req.Enabled ?? true;
            return await Guarded(async () =>
            {
                await registry.ApplyAsync(moduleId, tenantId, req.Version!, enabled, req.PackageSha256!, ct);
                return Results.Json(new { data = new { moduleId, tenantId, version = req.Version, enabled, state = "ACTIVE" } });
            });
        });

        api.MapDelete("/{moduleId}/tenants/{tenantId}", async (
            TenantShellRegistry registry, string moduleId, string tenantId, CancellationToken ct) =>
        {
            if (ValidateIds(moduleId, tenantId) is { } invalid) return invalid;
            var removed = await registry.RemoveAsync(moduleId, tenantId, ct);
            return Results.Json(new { data = new { moduleId, tenantId, removed } });
        });

        api.MapPost("/{moduleId}/versions/{version}/migrate", async (
            TenantShellRegistry registry, ILoggerFactory loggers, string moduleId, string version, MigrateRequest? req, CancellationToken ct) =>
        {
            if (ValidateIds(moduleId, null) is { } invalid) return invalid;
            if (!PluginIds.IsValidVersion(version)) return Invalid("version", "Invalid version.");
            if (string.IsNullOrWhiteSpace(req?.FromVersion)) return Invalid("fromVersion", "Required.");
            if (!string.IsNullOrWhiteSpace(req.ToVersion) && !string.Equals(req.ToVersion, version, StringComparison.OrdinalIgnoreCase))
                return Invalid("toVersion", "Must equal the version in the path.");
            if (!PluginIds.IsValidSha256(req.PackageSha256))
                return Invalid("packageSha256", "Required: 64-character hex SHA-256 of the package.");
            return await Guarded(async () =>
            {
                var runtime = await registry.EnsureRuntimeAsync(moduleId, version, req.PackageSha256!, ct);
                if (!runtime.TryEnter())
                    return ApiErrors.Problem(ErrorCodes.ModuleNotReady, "Runtime is being unloaded; retry.", 503, retryable: true);
                try
                {
                    await using var scope = runtime.Services.CreateAsyncScope();
                    await runtime.Plugin.MigrateAsync(new(req.FromVersion!, version), scope.ServiceProvider, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    loggers.CreateLogger(typeof(InternalEndpoints)).LogError(ex,
                        "plugin.migrate module={module} from={from} to={to} result=error", moduleId, req.FromVersion, version);
                    return ApiErrors.Problem(MigrateFailed, $"Migrate failed: {ex.Message}", 500);
                }
                finally { runtime.Exit(); }
                return Results.Json(new { data = new { migrated = true } });
            });
        });

        // Hybrid modules: cache the package so /plugin-assets/{moduleId}/{version}/ serves its web/.
        // Idempotent (reuses a cache folder whose marker matches); never touches tenant shells.
        api.MapPut("/{moduleId}/versions/{version}/assets", async (
            IModuleAssetSource assets, string moduleId, string version, StageAssetsRequest? req, CancellationToken ct) =>
        {
            if (ValidateIds(moduleId, null) is { } invalid) return invalid;
            if (!PluginIds.IsValidVersion(version)) return Invalid("version", "Invalid version.");
            if (!PluginIds.IsValidSha256(req?.PackageSha256))
                return Invalid("packageSha256", "Required: 64-character hex SHA-256 of the package.");
            return await Guarded(async () =>
            {
                await assets.EnsureAssetsAsync(moduleId, version, req!.PackageSha256!, ct);
                return Results.Json(new { data = new { moduleId, version, staged = true } });
            });
        });

        api.MapGet("", (TenantShellRegistry registry) => Results.Json(new { data = registry.View() }));
    }

    // Same check as the Platform/Gateway internal APIs: fixed-time compare, fail closed.
    public static bool IsInternalCaller(HttpContext ctx, IConfiguration cfg)
    {
        var expected = cfg["InternalApi:Key"];
        var presented = ctx.Request.Headers["X-Internal-Key"].FirstOrDefault() ?? "";
        return !string.IsNullOrWhiteSpace(expected) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(presented));
    }

    private static async Task<IResult> Guarded(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (PluginLoadException ex) { return ApiErrors.Problem(ErrorCodes.PluginLoadFailed, ex.Message, 422); }
        catch (PluginPackageUnavailableException ex) { return ApiErrors.Problem(ErrorCodes.PluginHostUnavailable, ex.Message, 502, retryable: true); }
    }

    private static IResult? ValidateIds(string moduleId, string? tenantId)
    {
        if (!PluginIds.IsValidId(moduleId)) return Invalid("moduleId", "Letters, digits, '-' and '_' only (max 128).");
        if (tenantId is not null && !PluginIds.IsValidId(tenantId)) return Invalid("tenantId", "Letters, digits, '-' and '_' only (max 128).");
        return null;
    }

    private static IResult Invalid(string field, string message) =>
        ApiErrors.Problem(ErrorCodes.ValidationError, $"{field}: {message}", 400, new[] { new { field, message } });
}
