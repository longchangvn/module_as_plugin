using Loyalty.Api.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Loyalty.Api.Endpoints;

public static class ModuleEndpoints
{
    public const string ModuleId = "loyalty";
    public const string Version = "1.0.0";

    public static void Map(WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Json(new { status = "ok", module = ModuleId, version = Version }));
        app.MapGet("/readyz", () => Results.Json(new { ready = true, checks = new { db = "ok", dependencies = "ok" } }));

        // Dry-run seed diff (must equal the actual seed applied on install).
        app.MapPost("/internal/seed:preview", () => Results.Json(new
        {
            data = new
            {
                roles = new[] { "Loyalty Operator" },
                permissions = new[] { "LOYALTY_MEMBER_VIEW", "LOYALTY_MEMBER_UPDATE" },
                menus = new[] { "loyalty", "loyalty.members" },
            },
        }));

        // Additive migrations only in v1.
        app.MapPost("/internal/migrate", (MigrateRequest req) =>
            req.FromVersion == req.ToVersion
                ? Results.Json(new { data = new { migrated = false } })
                : Results.Json(new { data = new { migrated = true, from = req.FromVersion, to = req.ToVersion } }));

        // Business endpoint example: permission enforced via Auth resolve (cached by caller, fail closed).
        // R9: the JWT tenant must be present, match any explicit header, and be usable.
        app.MapGet("/api/v1/members", async (HttpContext ctx, IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg) =>
        {
            var tenantId = ctx.User.FindFirst("tenantId")?.Value;
            if (string.IsNullOrEmpty(tenantId)) return Results.Json(new { error = new { code = "FORBIDDEN_TENANT", message = "Tenant context required." } }, statusCode: 403);
            var header = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(header) && !string.Equals(header.Trim(), tenantId, StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { error = new { code = "FORBIDDEN_TENANT", message = "Tenant header does not match request tenant." } }, statusCode: 403);
            var usability = await LoyaltyHelpers.TenantUsabilityAsync(http, cache, cfg, tenantId);
            if (usability is not null) return usability;
            return Results.Json(new { data = new[] { new { id = "m_1", name = "Ada" } } });
        }).RequireAuthorization();
    }
}
