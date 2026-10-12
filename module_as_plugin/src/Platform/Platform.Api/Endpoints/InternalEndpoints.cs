using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Models;
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
    app.MapGet("/internal/tenants/{id}/status", async (CatalogDbContext db, string id) =>
    {
        var t = await db.Tenants.FindAsync(id);
        return t is null
            ? Results.NotFound()
            : Results.Json(new { data = new
            {
                id = t.Id, status = t.Status.ToString(), dbStatus = t.DbStatus.ToString(),
                deploymentMode = t.DeploymentMode, dbName = t.DbName, schema = t.Schema,
                bootstrapAdminEmail = t.BootstrapAdminEmail, bootstrapAdminName = t.BootstrapAdminName,
            } });
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
