// Auth API :5122 — tenant users/roles/permissions/menus + resolve endpoint (A-BR5).
// Permission model = role permissions UNION user additional permissions, ACTIVE only (A-BR3).
using System.Text;
using Auth.Api.Domain;
using Auth.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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
app.MapGet("/api/v1/auth/permissions/resolve", async (TenantDbContext db, string tenantId, string userId) =>
{
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

// Users (A-BR1)
app.MapGet("/api/v1/admin/users", async (TenantDbContext db) =>
    Results.Json(new { data = await db.Users.AsNoTracking().ToListAsync() })).RequireAuthorization();
app.MapPost("/api/v1/admin/users", async (TenantDbContext db, CreateUser req) =>
{
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
app.MapGet("/api/v1/admin/roles", async (TenantDbContext db) =>
    Results.Json(new { data = await db.Roles.AsNoTracking().ToListAsync() })).RequireAuthorization();
app.MapPost("/api/v1/admin/roles", async (TenantDbContext db, CreateRole req) =>
{
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
app.MapGet("/api/v1/auth/permissions", async (TenantDbContext db) =>
    Results.Json(new { data = await db.Permissions.AsNoTracking().ToListAsync() })).RequireAuthorization();

// Internal seed apply/revoke — called by Platform lifecycle (SR-5), never by browsers.
app.MapPost("/internal/seed:apply", async (TenantDbContext db, Auth.Api.Application.SeedApplier applier, SeedApplyRequest req, CancellationToken ct) =>
{
    SharedKernel.ManifestFile manifest;
    try { manifest = SharedKernel.ManifestParser.Parse(req.ManifestYaml); }
    catch (Exception ex) { return ApiErrors.Problem(ErrorCodes.ValidationError, $"Invalid manifest: {ex.Message}", 400); }
    var diff = await applier.ApplyAsync(req.ModuleId, manifest, ct);
    return Results.Json(new { data = new { moduleId = req.ModuleId, applied = diff.Applied } });
});

app.MapPost("/internal/seed:revoke", async (Auth.Api.Application.SeedApplier applier, SeedRevokeRequest req, CancellationToken ct) =>
{
    await applier.RevokeAsync(req.ModuleId, ct);
    return Results.Json(new { data = new { moduleId = req.ModuleId, revoked = true } });
});

// Tenant login — mints tenant JWT (tenantId + userId; tenant_admin when the user holds an *Admin* role).
app.MapPost("/api/v1/auth/login", async (TenantDbContext db, IConfiguration cfg, LoginRequest req, CancellationToken ct) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == req.Email.ToLower(), ct);
    if (user is null || !user.IsActive)
        return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid credentials.", 401);
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

public sealed record SeedApplyRequest(string ModuleId, string ManifestYaml);
public sealed record SeedRevokeRequest(string ModuleId);
public sealed record LoginRequest(string TenantId, string Email, string Password);

public sealed record CreateUser(string Email, string Name);
public sealed record CreateRole(string Name, string? Description);

public partial class Program;
