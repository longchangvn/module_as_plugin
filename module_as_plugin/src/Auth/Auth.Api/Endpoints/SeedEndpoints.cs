using Auth.Api.Infrastructure;
using Auth.Api.Models;
using SharedKernel;

namespace Auth.Api.Endpoints;

public static class SeedEndpoints
{
    public static void Map(WebApplication app)
    {
    // Internal seed apply/revoke — called by Platform lifecycle (SR-5), never by browsers.
    // tenantId is required: seeds land in that tenant's store.
    app.MapPost("/internal/seed:apply", async (TenantDbContext legacy, TenantDbProvider provider, Auth.Api.Infrastructure.PermissionVersionCache versions, SeedApplyRequest req, CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(req.TenantId))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "tenantId is required.", 400, new { field = "tenantId" });
        await using var db = await provider.ForTenantAsync(req.TenantId, legacy);
        var applier = new Auth.Api.Application.SeedApplier(db);
        SharedKernel.ManifestFile manifest;
        try { manifest = SharedKernel.ManifestParser.Parse(req.ManifestYaml); }
        catch (Exception ex) { return ApiErrors.Problem(ErrorCodes.ValidationError, $"Invalid manifest: {ex.Message}", 400); }
        var diff = await applier.ApplyAsync(req.ModuleId, manifest, ct);
        versions.Bump(req.TenantId); // invalidate cached resolve results (A-BR5)
        return Results.Json(new { data = new { moduleId = req.ModuleId, applied = diff.Applied } });
    });

    app.MapPost("/internal/seed:revoke", async (TenantDbContext legacy, TenantDbProvider provider, Auth.Api.Infrastructure.PermissionVersionCache versions, SeedRevokeRequest req, CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(req.TenantId))
            return ApiErrors.Problem(ErrorCodes.ValidationError, "tenantId is required.", 400, new { field = "tenantId" });
        await using var db = await provider.ForTenantAsync(req.TenantId, legacy);
        var applier = new Auth.Api.Application.SeedApplier(db);
        if (!await applier.RevokeAsync(req.ModuleId, ct))
            return UserEndpoints.LastAdminProblem();
        versions.Bump(req.TenantId); // invalidate cached resolve results (A-BR5)
        return Results.Json(new { data = new { moduleId = req.ModuleId, revoked = true } });
    });
    }
}
