using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Models;
using Platform.Application;
using Platform.Domain;
using Platform.Infrastructure;
using SharedKernel;

using System.Text.Json;
namespace Platform.Api.Endpoints;

public static class TenantEndpoints
{
    public static void Map(WebApplication app)
    {
    // ---- tenants (P-BR3; Maintainer read-only per R1) ----
    app.MapGet("/api/v1/platform/tenants", async (CatalogDbContext db) =>
    {
        var tenants = await db.Tenants.AsNoTracking().ToListAsync();
        return Results.Json(new { data = tenants });
    }).RequireAuthorization("PlatformAny");

    app.MapPost("/api/v1/platform/tenants", async (CatalogDbContext db, HttpContext ctx, IConfiguration cfg, CreateTenantRequest req) =>
    {
        if (ApiHelpers.RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Maintainer is read-only for tenants.", 403);
        var pathBase = TenantValidator.NormalizePathBase(cfg["TenantRouting:PathPrefix"]);
        var failures = TenantValidator.ValidateCreate(req.Name, req.Slug, req.ResolutionMode, req.Domain, req.PathPrefix, req.ContactEmail,
            req.DeploymentMode, req.DbName, req.Schema, req.BootstrapAdminEmail, pathBase);
        if (failures.Count > 0)
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Tenant validation failed.",
                400, failures.Select(f => new { field = f.Field, message = f.Message }));
        var name = req.Name.Trim();
        var slug = req.Slug.Trim().ToLowerInvariant();
        var mode = (req.DeploymentMode ?? "dedicated").Trim().ToLowerInvariant();
        var dbName = string.IsNullOrWhiteSpace(req.DbName) ? $"tenant_{slug.Replace("-", "_")}" : req.DbName.Trim();
        var schema = mode == "shared" ? req.Schema!.Trim() : "public";
        var dup = await ApiHelpers.DuplicateTenantFieldAsync(db, null, name, slug, req.Domain, req.PathPrefix, mode, dbName, schema, pathBase);
        if (dup is not null) return dup;
        var tenant = new Tenant
        {
            Id = Cuid.New("tnt"), Name = name, Slug = slug,
            Domain = string.IsNullOrWhiteSpace(req.Domain) ? null : req.Domain.Trim().ToLowerInvariant(),
            PathPrefix = string.IsNullOrWhiteSpace(req.PathPrefix) ? TenantValidator.DefaultPathPrefix(slug, pathBase) : req.PathPrefix.Trim(),
            ResolutionMode = req.ResolutionMode ?? "path",
            DeploymentMode = mode, DbName = dbName, Schema = schema,
            Status = TenantStatus.Provisioning, DbStatus = TenantDbStatus.Provisioning,
            BootstrapAdminEmail = string.IsNullOrWhiteSpace(req.BootstrapAdminEmail) ? null : req.BootstrapAdminEmail.Trim(),
            BootstrapAdminName = string.IsNullOrWhiteSpace(req.BootstrapAdminName) ? null : req.BootstrapAdminName.Trim(),
            ContactEmail = req.ContactEmail!.Trim(), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        // Physical provisioning (CREATE DATABASE / CREATE SCHEMA) when a
        // provisioning connection is configured; otherwise the tenant is usable
        // immediately on the legacy shared store.
        var provisionConn = cfg["TenantStores:ProvisioningConnection"];
        if (string.IsNullOrWhiteSpace(provisionConn))
        {
            tenant.Status = TenantStatus.Active;
            tenant.DbStatus = TenantDbStatus.Ready;
            tenant.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await ApiHelpers.Audit(db, tenant.Id, ApiHelpers.ActorId(ctx), ApiHelpers.RequireRole(ctx), "tenant.create", "tenant", tenant.Id, null, ctx);
            return Results.Json(new { data = tenant }, statusCode: 201);
        }
        try
        {
            await ApiHelpers.ProvisionTenantStoreAsync(provisionConn, cfg["TenantStores:SharedDatabase"] ?? "module_platform", tenant);
        }
        catch (Exception ex)
        {
            tenant.Status = TenantStatus.Failed;
            tenant.DbStatus = TenantDbStatus.Failed;
            tenant.DbError = ex.Message;
            tenant.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await ApiHelpers.Audit(db, tenant.Id, ApiHelpers.ActorId(ctx), ApiHelpers.RequireRole(ctx), "tenant.create", "tenant", tenant.Id, ex.Message, ctx);
            return ApiErrors.Problem(ErrorCodes.ProvisionFailed, $"Tenant store provisioning failed: {ex.Message}", 502, retryable: true);
        }
        await db.SaveChangesAsync();
        await ApiHelpers.Audit(db, tenant.Id, ApiHelpers.ActorId(ctx), ApiHelpers.RequireRole(ctx), "tenant.create", "tenant", tenant.Id, null, ctx);
        // Store exists; Auth migrates + seeds lazily on first use, then reports
        // Ready via /internal/tenants/{id}/provisioned. 202 while Provisioning.
        return Results.Json(new { data = tenant }, statusCode: 202);
    }).RequireAuthorization("PlatformAny");

    // Update tenant (Admin only): name/contactEmail/domain/pathPrefix/resolutionMode.
    // dbName/schema/slug/id are immutable → 422 IMMUTABLE_FIELD.
    app.MapPatch("/api/v1/platform/tenants/{tid}", async (CatalogDbContext db, HttpContext ctx, IConfiguration cfg, string tid, JsonElement body) =>
    {
        if (ApiHelpers.RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Maintainer is read-only for tenants.", 403);
        foreach (var immutable in new[] { "id", "slug", "dbName", "schema", "deploymentMode", "bootstrapAdminEmail", "bootstrapAdminName" })
            if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty(immutable, out _))
                return ApiErrors.Problem(ErrorCodes.ImmutableField, $"Field '{immutable}' is immutable.", 422, new { field = immutable });
        var tenant = await db.Tenants.FindAsync(tid);
        if (tenant is null) return ApiErrors.Problem(ErrorCodes.UnknownTenant, $"Unknown tenant '{tid}'.", 404);
        var name = ApiHelpers.JsonString(body, "name") ?? tenant.Name;
        var contactEmail = ApiHelpers.JsonString(body, "contactEmail") ?? tenant.ContactEmail;
        var resolutionMode = ApiHelpers.JsonString(body, "resolutionMode") ?? tenant.ResolutionMode;
        var domain = ApiHelpers.JsonHas(body, "domain") ? ApiHelpers.EmptyToNull(ApiHelpers.JsonString(body, "domain")) : tenant.Domain;
        var pathPrefix = ApiHelpers.JsonHas(body, "pathPrefix") ? ApiHelpers.EmptyToNull(ApiHelpers.JsonString(body, "pathPrefix")) : tenant.PathPrefix;
        var pathBase = TenantValidator.NormalizePathBase(cfg["TenantRouting:PathPrefix"]);
        var failures = TenantValidator.ValidateUpdate(name, resolutionMode, domain, pathPrefix, contactEmail, pathBase);
        if (failures.Count > 0)
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Tenant validation failed.",
                400, failures.Select(f => new { field = f.Field, message = f.Message }));
        var dup = await ApiHelpers.DuplicateTenantFieldAsync(db, tid, name.Trim(), tenant.Slug, domain, pathPrefix, pathBase: pathBase);
        if (dup is not null) return dup;
        tenant.Name = name.Trim();
        tenant.ContactEmail = contactEmail!.Trim();
        tenant.ResolutionMode = resolutionMode!;
        tenant.Domain = string.IsNullOrWhiteSpace(domain) ? null : domain.Trim().ToLowerInvariant();
        tenant.PathPrefix = string.IsNullOrWhiteSpace(pathPrefix) ? TenantValidator.DefaultPathPrefix(tenant.Slug, pathBase) : pathPrefix.Trim();
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await ApiHelpers.Audit(db, tenant.Id, ApiHelpers.ActorId(ctx), ApiHelpers.RequireRole(ctx), "tenant.update", "tenant", tenant.Id, null, ctx);
        return Results.Json(new { data = tenant });
    }).RequireAuthorization("PlatformAny");

    app.MapPost("/api/v1/platform/tenants/{tid}/disable", async (CatalogDbContext db, HttpContext ctx, string tid, DisableRequest req) =>
    {
        if (ApiHelpers.RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Maintainer is read-only for tenants.", 403);
        if (string.IsNullOrWhiteSpace(req?.Reason))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "A reason is required to disable a tenant.", 400, new { field = "reason" });
        var tenant = await db.Tenants.FindAsync(tid);
        if (tenant is null) return ApiErrors.Problem(ErrorCodes.UnknownTenant, $"Unknown tenant '{tid}'.", 404);
        if (tenant.Status == TenantStatus.Disabled)
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Tenant is already disabled.", 409);
        tenant.Status = TenantStatus.Disabled;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await ApiHelpers.Audit(db, tenant.Id, ApiHelpers.ActorId(ctx), ApiHelpers.RequireRole(ctx), "tenant.disable", "tenant", tenant.Id, req.Reason, ctx);
        return Results.Json(new { data = tenant });
    }).RequireAuthorization("PlatformAny");

    app.MapPost("/api/v1/platform/tenants/{tid}/enable", async (CatalogDbContext db, HttpContext ctx, string tid) =>
    {
        if (ApiHelpers.RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Maintainer is read-only for tenants.", 403);
        var tenant = await db.Tenants.FindAsync(tid);
        if (tenant is null) return ApiErrors.Problem(ErrorCodes.UnknownTenant, $"Unknown tenant '{tid}'.", 404);
        if (tenant.Status == TenantStatus.Active)
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Tenant is already active.", 409);
        tenant.Status = TenantStatus.Active;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await ApiHelpers.Audit(db, tenant.Id, ApiHelpers.ActorId(ctx), ApiHelpers.RequireRole(ctx), "tenant.enable", "tenant", tenant.Id, null, ctx);
        return Results.Json(new { data = tenant });
    }).RequireAuthorization("PlatformAny");

    // Audit trail per tenant (FR-SA8): most recent first.
    app.MapGet("/api/v1/platform/tenants/{tid}/audits", async (CatalogDbContext db, string tid) =>
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tid))
            return ApiErrors.Problem(ErrorCodes.UnknownTenant, $"Unknown tenant '{tid}'.", 404);
        var audits = await db.Audits.Where(a => a.TenantId == tid).OrderByDescending(a => a.CreatedAt).Take(100).AsNoTracking().ToListAsync();
        return Results.Json(new { data = audits });
    }).RequireAuthorization("PlatformAny");

    // Retry physical provisioning for a Failed tenant (Admin only).
    app.MapPost("/api/v1/platform/tenants/{tid}/provision:retry", async (CatalogDbContext db, HttpContext ctx, IConfiguration cfg, string tid) =>
    {
        if (ApiHelpers.RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Maintainer is read-only for tenants.", 403);
        var tenant = await db.Tenants.FindAsync(tid);
        if (tenant is null) return ApiErrors.Problem(ErrorCodes.UnknownTenant, $"Unknown tenant '{tid}'.", 404);
        if (tenant.DbStatus == TenantDbStatus.Ready)
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Tenant store is already ready.", 422);
        if (tenant.DbStatus == TenantDbStatus.Provisioning)
            return ApiErrors.Problem(ErrorCodes.ValidationError, "Provisioning is already in progress.", 409);
        var provisionConn = cfg["TenantStores:ProvisioningConnection"];
        if (string.IsNullOrWhiteSpace(provisionConn))
            return ApiErrors.Problem(ErrorCodes.ProvisionFailed, "No provisioning connection configured.", 502, retryable: false);
        tenant.DbStatus = TenantDbStatus.Provisioning;
        tenant.Status = TenantStatus.Provisioning;
        tenant.DbError = null;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await ApiHelpers.ProvisionTenantStoreAsync(provisionConn, cfg["TenantStores:SharedDatabase"] ?? "module_platform", tenant);
        }
        catch (Exception ex)
        {
            tenant.Status = TenantStatus.Failed;
            tenant.DbStatus = TenantDbStatus.Failed;
            tenant.DbError = ex.Message;
            tenant.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await ApiHelpers.Audit(db, tenant.Id, ApiHelpers.ActorId(ctx), ApiHelpers.RequireRole(ctx), "tenant.provision-retry", "tenant", tenant.Id, ex.Message, ctx);
            return ApiErrors.Problem(ErrorCodes.ProvisionFailed, $"Tenant store provisioning failed: {ex.Message}", 502, retryable: true);
        }
        await db.SaveChangesAsync();
        await ApiHelpers.Audit(db, tenant.Id, ApiHelpers.ActorId(ctx), ApiHelpers.RequireRole(ctx), "tenant.provision-retry", "tenant", tenant.Id, null, ctx);
        return Results.Json(new { data = tenant }, statusCode: 202);
    }).RequireAuthorization("PlatformAny");

    // Cluster-internal tenant status for Auth/module guards. NOT routed by the
    // gateway (outside /api/v1/*) — direct service-to-service only.
    // Carries non-secret store metadata (dbName/schema/mode) so Auth can build
    // per-tenant connections from its own server-side config (NFR4: secrets never
    // transit APIs).
    }
}
