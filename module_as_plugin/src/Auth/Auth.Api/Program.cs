// Auth API :5122 — tenant users/roles/permissions/menus + resolve endpoint (A-BR5).
// Permission model = role permissions UNION user additional permissions, ACTIVE only (A-BR3).
using System.Text;
using Auth.Api.Domain;
using Auth.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using SharedKernel;

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
builder.Services.AddScoped<Auth.Api.Infrastructure.TenantDbProvider>();

var tenantConn = builder.Configuration.GetConnectionString("Tenant");
if (string.IsNullOrWhiteSpace(tenantConn))
    builder.Services.AddDbContext<TenantDbContext>(o => o.UseInMemoryDatabase("tenant"));
else
    builder.Services.AddDbContext<TenantDbContext>(o => o.UseNpgsql(tenantConn));
builder.Services.AddScoped<Auth.Api.Application.SeedApplier>();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

// Postgres: apply EF migrations on startup (P9); InMemory needs nothing.
if (!string.IsNullOrWhiteSpace(tenantConn))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Database.MigrateAsync();
}

app.MapGet("/healthz", () => Results.Json(new { status = "ok", service = "auth" }));
app.MapGet("/readyz", async (TenantDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Json(new { ready = true })
        : Results.Json(new { ready = false }, statusCode: 503));

// Resolve endpoint for gateway/module APIs — cached 60s by callers, fail closed (A-BR5).
// v1 scaffold: no in-process cache; callers add the short-TTL cache.
app.MapGet("/api/v1/auth/permissions/resolve", async (TenantDbContext legacy, TenantDbProvider provider, IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg, string tenantId, string userId) =>
{
    await using var db = await provider.ForTenantAsync(tenantId, legacy);
    // Disabled/unknown tenants resolve nothing (fail closed on explicit state).
    var usability = await TenantUsabilityAsync(http, cache, cfg, tenantId);
    if (usability is not null) return usability;
    var user = await db.Users.FindAsync(userId);
    if (user is null || !user.IsActive) return Results.Json(new { data = new { permissions = Array.Empty<string>(), version = 1 } });
    var roleIds = await db.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync();
    var activeRoleIds = await db.Roles.Where(r => roleIds.Contains(r.Id) && r.IsActive).Select(r => r.Id).ToListAsync();
    var rolePermIds = await db.RolePermissions.Where(rp => activeRoleIds.Contains(rp.RoleId)).Select(rp => rp.PermissionId).ToListAsync();
    var extraPermIds = await db.UserAdditionalPermissions.Where(u => u.UserId == userId).Select(u => u.PermissionId).ToListAsync();
    var codes = await db.Permissions.Where(p => rolePermIds.Concat(extraPermIds).Contains(p.Id)).Select(p => p.Code).ToListAsync();
    var union = codes.Distinct(StringComparer.Ordinal).OrderBy(c => c).ToList();
    return Results.Json(new { data = new { tenantId, userId, permissions = union, version = 1 } });
});

// Users (A-BR1). Tenant-scoped via JWT claim or X-Tenant-Id header
// (bootstrap path); platform tokens without a header hit the legacy store.
app.MapGet("/api/v1/admin/users", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx) =>
{
    await using var db = await provider.ForRequestAsync(ctx, legacy);
    return Results.Json(new { data = await db.Users.AsNoTracking().ToListAsync() });
}).RequireAuthorization();
app.MapPost("/api/v1/admin/users", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx, CreateUser req) =>
{
    await using var db = await provider.ForRequestAsync(ctx, legacy);
    if (await db.Users.AnyAsync(u => u.Email.ToLower() == req.Email.ToLower()))
        return ApiErrors.Problem(ErrorCodes.ValidationError, "Email already exists.", 400);
    var user = new TenantUser
    {
        Id = Cuid.New("u"), Email = req.Email, Name = req.Name,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Json(new { data = user }, statusCode: 201);
}).RequireAuthorization();

// Roles (A-BR2)
app.MapGet("/api/v1/admin/roles", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx) =>
{
    await using var db = await provider.ForRequestAsync(ctx, legacy);
    return Results.Json(new { data = await db.Roles.AsNoTracking().ToListAsync() });
}).RequireAuthorization();
app.MapPost("/api/v1/admin/roles", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx, CreateRole req) =>
{
    await using var db = await provider.ForRequestAsync(ctx, legacy);
    if (await db.Roles.AnyAsync(r => r.Name.ToLower() == req.Name.ToLower()))
        return ApiErrors.Problem(ErrorCodes.ValidationError, "Role name must be unique (case-insensitive).", 400);
    var role = new Role
    {
        Id = Cuid.New("r"), Name = req.Name, Description = req.Description,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };
    db.Roles.Add(role);
    await db.SaveChangesAsync();
    return Results.Json(new { data = role }, statusCode: 201);
}).RequireAuthorization();

// Permission catalog (read-only, A-BR3/A-BR5)
app.MapGet("/api/v1/auth/permissions", async (TenantDbContext legacy, TenantDbProvider provider, HttpContext ctx) =>
{
    await using var db = await provider.ForRequestAsync(ctx, legacy);
    return Results.Json(new { data = await db.Permissions.AsNoTracking().ToListAsync() });
}).RequireAuthorization();

// Internal seed apply/revoke — called by Platform lifecycle (SR-5), never by browsers.
// tenantId is required: seeds land in that tenant's store.
app.MapPost("/internal/seed:apply", async (TenantDbContext legacy, TenantDbProvider provider, SeedApplyRequest req, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.TenantId))
        return ApiErrors.Problem(ErrorCodes.ValidationError, "tenantId is required.", 400, new { field = "tenantId" });
    await using var db = await provider.ForTenantAsync(req.TenantId, legacy);
    var applier = new Auth.Api.Application.SeedApplier(db);
    SharedKernel.ManifestFile manifest;
    try { manifest = SharedKernel.ManifestParser.Parse(req.ManifestYaml); }
    catch (Exception ex) { return ApiErrors.Problem(ErrorCodes.ValidationError, $"Invalid manifest: {ex.Message}", 400); }
    var diff = await applier.ApplyAsync(req.ModuleId, manifest, ct);
    return Results.Json(new { data = new { moduleId = req.ModuleId, applied = diff.Applied } });
});

app.MapPost("/internal/seed:revoke", async (TenantDbContext legacy, TenantDbProvider provider, SeedRevokeRequest req, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.TenantId))
        return ApiErrors.Problem(ErrorCodes.ValidationError, "tenantId is required.", 400, new { field = "tenantId" });
    await using var db = await provider.ForTenantAsync(req.TenantId, legacy);
    var applier = new Auth.Api.Application.SeedApplier(db);
    await applier.RevokeAsync(req.ModuleId, ct);
    return Results.Json(new { data = new { moduleId = req.ModuleId, revoked = true } });
});

// Tenant login — mints tenant JWT (tenantId + userId; tenant_admin when the user holds an *Admin* role).
app.MapPost("/api/v1/auth/login", async (TenantDbContext legacy, TenantDbProvider provider, IConfiguration cfg, IHttpClientFactory http, IMemoryCache cache, LoginRequest req, CancellationToken ct) =>
{
    // Per-tenant store first: first login migrates + reports Ready (lazy
    // provisioning), so the usability check below sees the flipped state.
    await using var db = await provider.ForTenantAsync(req.TenantId, legacy);
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == req.Email.ToLower(), ct);
    if (user is null || !user.IsActive)
        return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid credentials.", 401);
    // BR-R4: disabled/unknown tenants cannot log in (fail closed).
    var usability = await TenantUsabilityAsync(http, cache, cfg, req.TenantId);
    if (usability is not null) return usability;
    // NOTE: password verification is stubbed in v1 (seed users have no hash); production wires Argon2 here.
    var roleIds = await db.UserRoles.Where(ur => ur.UserId == user.Id).Select(ur => ur.RoleId).ToListAsync(ct);
    var roleNames = await db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Name).ToListAsync(ct);
    var isAdmin = roleNames.Any(n => n.Contains("Admin", StringComparison.OrdinalIgnoreCase));
    var token = JwtMint.Mint(JwtMint.Secret(cfg), new Dictionary<string, string>
    {
        ["sub"] = user.Id, ["email"] = user.Email,
        ["tenantId"] = req.TenantId, ["userId"] = user.Id,
        ["tenant_admin"] = isAdmin ? "true" : "false",
    });
    return Results.Json(new { data = new { accessToken = token, userId = user.Id, tenantAdmin = isAdmin } });
});

app.Run();

// Tenant usability via Platform's cluster-internal status endpoint.
// Cached 30s. Fail closed: unknown → 404, disabled → 403, platform
// unreachable → 503 (retryable). No fail-open.
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
        return ApiErrors.Problem(ErrorCodes.AuthzUnavailable, $"Tenant status unavailable: {ex.Message}", 503, retryable: true);
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
        ? ApiErrors.Problem(ErrorCodes.UnknownTenant, "Unknown tenant.", 404)
        : ApiErrors.Problem(ErrorCodes.TenantDisabled, "Tenant is not active.", 403);
}

public sealed record SeedApplyRequest(string ModuleId, string ManifestYaml, string TenantId = "");
public sealed record SeedRevokeRequest(string ModuleId, string TenantId = "");
public sealed record LoginRequest(string TenantId, string Email, string Password);

public sealed record CreateUser(string Email, string Name);
public sealed record CreateRole(string Name, string? Description);

public partial class Program;
