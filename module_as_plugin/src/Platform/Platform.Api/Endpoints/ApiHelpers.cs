using Microsoft.EntityFrameworkCore;
using Platform.Domain;
using Platform.Infrastructure;
using SharedKernel;
using System.Text.Json;

namespace Platform.Api.Endpoints;

public static class ApiHelpers
{
    public static string RequireRole(HttpContext ctx) =>
        ctx.User.FindFirst("platform_role")?.Value ?? "";
    public static string ActorId(HttpContext ctx) =>
        ctx.User.FindFirst("sub")?.Value
        ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? "unknown";

    public static bool CanWrite(HttpContext ctx, string tid)
    {
        if ((ctx.User.FindFirst("platform_role")?.Value) == "Admin") return true;
        return ctx.User.FindFirst("tenant_admin")?.Value == "true"
            && ctx.User.FindFirst("tenantId")?.Value == tid;
    }
    public static string ActorRole(HttpContext ctx) =>
        string.IsNullOrEmpty(RequireRole(ctx)) ? "TENANT_ADMIN" : RequireRole(ctx);

    public static string TenantId(HttpContext ctx) => ctx.User.FindFirst("tenantId")?.Value ?? "";

    public static IResult MapLifecycleError(ErrorEnvelope e) => e.Code switch
    {
        ErrorCodes.UnknownTenant => ApiErrors.Problem(e.Code, e.Message, 404, e.Details),
        ErrorCodes.TenantDisabled => ApiErrors.Problem(e.Code, e.Message, 403, e.Details),
        ErrorCodes.UnknownModule or ErrorCodes.UnknownVersion => ApiErrors.Problem(e.Code, e.Message, 404, e.Details),
        ErrorCodes.DependencyMissing or ErrorCodes.VersionConflict => ApiErrors.Problem(e.Code, e.Message, 422, e.Details),
        _ => ApiErrors.Problem(e.Code, e.Message, 502, e.Details, retryable: true),
    };

    public static async Task Audit(CatalogDbContext db, string? tenantId, string actorId, string actorRole,
        string action, string resourceType, string resourceId, string? reason, HttpContext ctx)
    {
        db.Audits.Add(new TenantAudit
        {
            Id = Cuid.New("aud"), TenantId = tenantId, ActorId = actorId, ActorRole = actorRole,
            Action = action, ResourceType = resourceType, ResourceId = resourceId, Reason = reason,
            CreatedAt = DateTimeOffset.UtcNow, RequestId = ctx.TraceIdentifier,
        });
        await db.SaveChangesAsync();
    }

    // BR-T1 uniqueness (case-insensitive for name/domain). Returns a 409 result
    // naming the conflicting field, or null when clear. excludeId skips one row (PATCH).

    public static async Task<IResult?> DuplicateTenantFieldAsync(
        CatalogDbContext db, string? excludeId, string name, string slug, string? domain, string? pathPrefix,
        string? deploymentMode = null, string? dbName = null, string? schema = null, string? pathBase = null,
        bool hasCustomConnection = false)
    {
        var lowerName = name.Trim().ToLower();
        if (await db.Tenants.AnyAsync(t => t.Id != excludeId && t.Name.ToLower() == lowerName))
            return ApiErrors.Problem(ErrorCodes.DuplicateTenant, "Tenant name already exists.", 409, new { field = "name" });
        var lowerSlug = slug.Trim().ToLower();
        if (await db.Tenants.AnyAsync(t => t.Id != excludeId && t.Slug.ToLower() == lowerSlug))
            return ApiErrors.Problem(ErrorCodes.DuplicateTenant, "Tenant slug already exists.", 409, new { field = "slug" });
        if (!string.IsNullOrWhiteSpace(domain))
        {
            var lowerDomain = domain.Trim().ToLower();
            if (await db.Tenants.AnyAsync(t => t.Id != excludeId && t.Domain != null && t.Domain.ToLower() == lowerDomain))
                return ApiErrors.Problem(ErrorCodes.DuplicateTenant, "Domain is already mapped to another tenant.", 409, new { field = "domain" });
        }
        var prefix = string.IsNullOrWhiteSpace(pathPrefix) ? TenantValidator.DefaultPathPrefix(slug, pathBase) : pathPrefix.Trim();
        if (await db.Tenants.AnyAsync(t => t.Id != excludeId && t.PathPrefix == prefix))
            return ApiErrors.Problem(ErrorCodes.DuplicateTenant, "Path prefix is already used by another tenant.", 409, new { field = "pathPrefix" });
        // BR-T1: dbName unique among DEDICATED_DB tenants (skipped for custom
        // connections — scoped per server, checked by CheckCustomConnectionAsync);
        // schema unique when set (likewise scoped for custom tenants).
        if (!hasCustomConnection && (deploymentMode ?? "dedicated") == "dedicated" && !string.IsNullOrWhiteSpace(dbName))
        {
            var lowerDb = dbName.Trim().ToLower();
            if (await db.Tenants.AnyAsync(t => t.Id != excludeId && t.DeploymentMode == "dedicated" && t.DbName.ToLower() == lowerDb))
                return ApiErrors.Problem(ErrorCodes.DuplicateTenant, "Database name is already used by another tenant.", 409, new { field = "dbName" });
        }
        if (!hasCustomConnection && !string.IsNullOrWhiteSpace(schema) && schema.Trim() != "public")
        {
            var s = schema.Trim();
            if (await db.Tenants.AnyAsync(t => t.Id != excludeId && t.Schema == s))
                return ApiErrors.Problem(ErrorCodes.DuplicateTenant, "Schema is already used by another tenant.", 409, new { field = "schema" });
        }
        return null;
    }

    // BR-T1 for custom-connection tenants: the (server, database, schema)
    // tuple must be unique. Cross-server duplicates are rejected too (safe
    // direction — per-server tracking would need stored host/port).
    public static async Task<IResult?> CheckCustomConnectionAsync(
        CatalogDbContext db, IConfiguration cfg, string? excludeId,
        string dbName, string schema, string customHost, int customPort)
    {
        var lowerDb = dbName.Trim().ToLower();
        var s = schema.Trim();
        if (await db.Tenants.AnyAsync(t => t.Id != excludeId && t.DbConnectionEncrypted != null
            && t.DbName.ToLower() == lowerDb && t.Schema == s))
            return ApiErrors.Problem(ErrorCodes.DuplicateTenant, "Database and schema are already used by another custom-connection tenant.", 409, new { field = "dbName" });
        // Same-server ownership: a default dedicated tenant owns its whole DB.
        var (defHost, defPort) = DefaultServer(cfg);
        if (defHost is not null && defPort.HasValue
            && string.Equals(defHost, customHost, StringComparison.OrdinalIgnoreCase) && defPort == customPort
            && await db.Tenants.AnyAsync(t => t.Id != excludeId && t.DbConnectionEncrypted == null
                && t.DeploymentMode == "dedicated" && t.DbName.ToLower() == lowerDb))
            return ApiErrors.Problem(ErrorCodes.DuplicateTenant, "Database name is already used by another tenant on this server.", 409, new { field = "dbName" });
        return null;
    }

    private static (string? Host, int? Port) DefaultServer(IConfiguration cfg)
    {
        var host = cfg["TenantStores:Host"];
        if (!string.IsNullOrWhiteSpace(host))
            return (host.Trim(), int.TryParse(cfg["TenantStores:Port"], out var p) ? p : 5432);
        try
        {
            var csb = new Npgsql.NpgsqlConnectionStringBuilder(cfg.GetConnectionString("Catalog"));
            return (csb.Host, csb.Port);
        }
        catch
        {
            return (null, null);
        }
    }

    // Physical store provisioning: CREATE DATABASE (dedicated, idempotent when
    // the DB already exists → reuse) or CREATE SCHEMA (shared). Runs with the
    // privileged provisioning connection (module_migrator, CREATEDB).

    public static async Task ProvisionTenantStoreAsync(string provisionConn, string sharedDatabase, Tenant tenant)
    {
        if (tenant.DeploymentMode == "shared")
        {
            var sb = new Npgsql.NpgsqlConnectionStringBuilder(provisionConn) { Database = sharedDatabase };
            await using var conn = new Npgsql.NpgsqlConnection(sb.ToString());
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS {QuoteIdent(tenant.Schema)}", conn);
            await cmd.ExecuteNonQueryAsync();
            return;
        }
        await using var admin = new Npgsql.NpgsqlConnection(provisionConn);
        await admin.OpenAsync();
        await using var exists = new Npgsql.NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @db", admin);
        exists.Parameters.AddWithValue("db", tenant.DbName);
        var found = await exists.ExecuteScalarAsync();
        if (found is null)
        {
            await using var create = new Npgsql.NpgsqlCommand($"CREATE DATABASE {QuoteIdent(tenant.DbName)}", admin);
            await create.ExecuteNonQueryAsync();
        }
    }

    // Custom-connection provisioning: tables live under the tenant schema in
    // the tenant's own database. Dial the custom string as-is (database
    // exists); on 3D000 create the database via the maintenance database
    // first, then the schema. Never logs the connection string.
    public static async Task ProvisionCustomTenantStoreAsync(string customConn, string dbName, string schema)
    {
        try
        {
            await using var conn = new Npgsql.NpgsqlConnection(customConn);
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS {QuoteIdent(schema)}", conn);
            await cmd.ExecuteNonQueryAsync();
            return;
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "3D000")
        {
            // Database does not exist yet — create it below.
        }
        var sb = new Npgsql.NpgsqlConnectionStringBuilder(customConn) { Database = "postgres" };
        await using var admin = new Npgsql.NpgsqlConnection(sb.ToString());
        await admin.OpenAsync();
        await using var create = new Npgsql.NpgsqlCommand($"CREATE DATABASE {QuoteIdent(dbName)}", admin);
        await create.ExecuteNonQueryAsync();
        await using var target = new Npgsql.NpgsqlConnection(customConn);
        await target.OpenAsync();
        await using var schemaCmd = new Npgsql.NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS {QuoteIdent(schema)}", target);
        await schemaCmd.ExecuteNonQueryAsync();
    }

    public static string QuoteIdent(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";

    public static bool JsonHas(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out _);

    public static string? JsonString(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    public static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // Tenant must exist and be usable (Active). Unknown → 404, provisioning →
    // 403 TENANT_NOT_READY, otherwise → 403 TENANT_DISABLED.

    public static async Task<(Tenant? tenant, IResult? error)> RequireUsableTenantAsync(CatalogDbContext db, string tid)
    {
        var tenant = await db.Tenants.FindAsync(tid);
        if (tenant is null)
            return (null, ApiErrors.Problem(ErrorCodes.UnknownTenant, $"Unknown tenant '{tid}'.", 404));
        if (tenant.Status == TenantStatus.Provisioning)
            return (null, ApiErrors.Problem(ErrorCodes.TenantNotReady, $"Tenant '{tid}' is still provisioning.", 403));
        if (tenant.Status != TenantStatus.Active)
            return (null, ApiErrors.Problem(ErrorCodes.TenantDisabled, $"Tenant '{tid}' is not active.", 403));
        return (tenant, null);
    }

    // BR-R2: an explicit X-Tenant-Id header must match the effective tenant —
    // no override header. Returns 403 on mismatch, null when consistent/absent.

    public static IResult? CheckTenantHeader(HttpContext ctx, string effectiveTid)
    {
        var header = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header) &&
            !string.Equals(header.Trim(), effectiveTid, StringComparison.OrdinalIgnoreCase))
            return ApiErrors.Problem(ErrorCodes.ForbiddenTenant, "Tenant header does not match request tenant.", 403);
        return null;
    }
}
