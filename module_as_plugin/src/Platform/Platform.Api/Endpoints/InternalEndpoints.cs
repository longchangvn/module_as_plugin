using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Models;
using Platform.Api.Services;
using Platform.Application;
using Platform.Domain;
using Platform.Infrastructure;
using SharedKernel;

using System.Text.Json;
namespace Platform.Api.Endpoints;

public static class InternalEndpoints
{
    public static void Map(WebApplication app)
    {
    // Cluster-internal tenant resolution by human identifier (slug or name,
    // case-insensitive) for login UX — callers that only know the tenant name
    // resolve it to the opaque id first, then use the id everywhere else.
    // Distinct `:resolve` path (never collides with {id} routes).
    app.MapGet("/internal/tenants:resolve", async (CatalogDbContext db, string? slug, string? name) =>
    {
        Tenant? t = null;
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var s = slug.Trim().ToLowerInvariant();
            t = await db.Tenants.FirstOrDefaultAsync(x => x.Slug.ToLower() == s);
        }
        t ??= !string.IsNullOrWhiteSpace(name)
            ? await db.Tenants.FirstOrDefaultAsync(x => x.Name.ToLower() == name.Trim().ToLower())
            : null;
        return t is null
            ? Results.NotFound()
            : Results.Json(new { data = new
            {
                id = t.Id, status = t.Status.ToString(), dbStatus = t.DbStatus.ToString(),
                deploymentMode = t.DeploymentMode, dbName = t.DbName, schema = t.Schema,
                hasCustomConnection = t.HasCustomConnection,
                bootstrapAdminEmail = t.BootstrapAdminEmail, bootstrapAdminName = t.BootstrapAdminName,
            } });
    });

    app.MapGet("/internal/tenants/{id}/status", async (CatalogDbContext db, string id) =>
    {
        var t = await db.Tenants.FindAsync(id);
        return t is null
            ? Results.NotFound()
            : Results.Json(new { data = new
            {
                id = t.Id, status = t.Status.ToString(), dbStatus = t.DbStatus.ToString(),
                deploymentMode = t.DeploymentMode, dbName = t.DbName, schema = t.Schema,
                hasCustomConnection = t.HasCustomConnection,
                bootstrapAdminEmail = t.BootstrapAdminEmail, bootstrapAdminName = t.BootstrapAdminName,
            } });
    });

    // Cluster-internal custom connection hand-off to Auth. Decrypts server-side
    // and returns the plaintext connection string. Requires the shared
    // X-Internal-Key (InternalApi:Key from env — never the DB, never browsers).
    // NOT routed by the gateway.
    app.MapGet("/internal/tenants/{id}/connection", async (
        CatalogDbContext db, HttpContext ctx, IConfiguration cfg, TenantConnectionProtector protector, string id) =>
    {
        var expected = cfg["InternalApi:Key"];
        var presented = ctx.Request.Headers["X-Internal-Key"].FirstOrDefault() ?? "";
        var ok = !string.IsNullOrWhiteSpace(expected) &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(presented));
        if (!ok)
            return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid internal key.", 401);
        var t = await db.Tenants.FindAsync(id);
        if (t is null)
            return ApiErrors.Problem(ErrorCodes.UnknownTenant, $"Unknown tenant '{id}'.", 404);
        if (!t.HasCustomConnection)
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Tenant has no custom connection.", 404);
        var plain = protector.TryUnprotect(t.DbConnectionEncrypted);
        if (plain is null)
            return ApiErrors.Problem(ErrorCodes.ProvisionFailed, "Stored connection cannot be decrypted.", 502, retryable: false);
        return Results.Json(new { data = new { tenantId = t.Id, connectionString = plain } });
    });
    app.MapPost("/internal/tenants/{id}/provisioned", async (CatalogDbContext db, string id, JsonElement body) =>
    {
        var t = await db.Tenants.FindAsync(id);
        if (t is null) return Results.NotFound();
        t.DbStatus = TenantDbStatus.Ready;
        t.SchemaVersion = ApiHelpers.JsonString(body, "schemaVersion");
        if (t.Status is TenantStatus.Provisioning or TenantStatus.Failed) t.Status = TenantStatus.Active;
        t.DbError = null;
        t.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.Json(new { data = new { id = t.Id, status = t.Status.ToString(), dbStatus = t.DbStatus.ToString() } });
    });

    app.MapPost("/internal/tenants/{id}/provision-failed", async (CatalogDbContext db, string id, JsonElement body) =>
    {
        var t = await db.Tenants.FindAsync(id);
        if (t is null) return Results.NotFound();
        t.DbStatus = TenantDbStatus.Failed;
        t.Status = TenantStatus.Failed;
        t.DbError = ApiHelpers.JsonString(body, "error") ?? "Unknown provisioning error.";
        t.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.Json(new { data = new { id = t.Id, status = t.Status.ToString(), dbStatus = t.DbStatus.ToString() } });
    });
    }
}
