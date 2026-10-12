// Sample module API :5201 — built from the module template (§10.5, M-BR1).
// Every module implements: GET /healthz, GET /readyz, POST /internal/seed:preview,
// POST /internal/migrate. No GET /manifest (R10).
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwtSecret = builder.Configuration["Jwt:SecretKey"] ?? "dev-only-secret-key-min-32-chars!!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew = TimeSpan.FromMinutes(1),
    };
});
builder.Services.AddAuthorization();

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("platform", c => c.Timeout = TimeSpan.FromSeconds(5));

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

const string ModuleId = "loyalty";
const string Version = "1.0.0";

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
    var usability = await TenantUsabilityAsync(http, cache, cfg, tenantId);
    if (usability is not null) return usability;
    return Results.Json(new { data = new[] { new { id = "m_1", name = "Ada" } } });
}).RequireAuthorization();

app.Run();

// Tenant usability via Platform's cluster-internal status endpoint (cached
// 30s). Fail closed: unknown → 404, disabled → 403, platform unreachable →
// 503 (retryable). No fail-open.
static async Task<IResult?> TenantUsabilityAsync(
    IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg, string tenantId)
{
    var key = $"tenant-usable:{tenantId}";
    if (cache.TryGetValue<string>(key, out var cached) && cached is not null)
        return cached == "usable" ? null : FailFor(cached);
    string state;
    try
    {
        var baseUrl = (cfg["Platform:BaseUrl"] ?? "http://localhost:5121").TrimEnd('/');
        using var client = http.CreateClient("platform");
        using var res = await client.GetAsync($"{baseUrl}/internal/tenants/{Uri.EscapeDataString(tenantId)}/status");
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            return FailFor(Store("unknown"));
        res.EnsureSuccessStatusCode();
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        state = doc.RootElement.GetProperty("data").GetProperty("status").GetString() ?? "";
    }
    catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
    {
        return Results.Json(new { error = new { code = "AUTHZ_UNAVAILABLE", message = $"Tenant status unavailable: {ex.Message}", retryable = true } }, statusCode: 503);
    }
    var verdict = string.Equals(state, "Active", StringComparison.OrdinalIgnoreCase) ? "usable" : "disabled";
    cache.Set(key, verdict, TimeSpan.FromSeconds(30));
    return verdict == "usable" ? null : FailFor(verdict);

    string Store(string v)
    {
        cache.Set(key, v, TimeSpan.FromSeconds(30));
        return v;
    }
    static IResult FailFor(string v) => v == "unknown"
        ? Results.Json(new { error = new { code = "UNKNOWN_TENANT", message = "Unknown tenant." } }, statusCode: 404)
        : Results.Json(new { error = new { code = "TENANT_DISABLED", message = "Tenant is not active." } }, statusCode: 403);
}

public sealed record MigrateRequest(string FromVersion, string ToVersion);
public partial class Program;
