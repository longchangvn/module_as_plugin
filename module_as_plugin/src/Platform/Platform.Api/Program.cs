// Platform API :5121 — registry + lifecycle + tenants + platform users + frontend bootstrap.
// Clean-architecture wiring: Api (this file + Endpoints/) -> Application -> Domain; persistence via Infrastructure.
// Auth: platform JWT (role=Admin|Maintainer). R1 enforced per endpoint.
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Platform.Application;
using Platform.Domain;
using Platform.Infrastructure;
using SharedKernel;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwtSecret = builder.Configuration["Jwt:SecretKey"] ?? "dev-only-secret-key-min-32-chars!!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("PlatformAdmin", p => p.RequireClaim("platform_role", "Admin"))
    .AddPolicy("PlatformAny", p => p.RequireClaim("platform_role", "Admin", "Maintainer"));

var catalogConn = builder.Configuration.GetConnectionString("Catalog");
if (string.IsNullOrWhiteSpace(catalogConn))
    builder.Services.AddDbContext<CatalogDbContext>(o => o.UseInMemoryDatabase("catalog"));
else
    builder.Services.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(catalogConn));

builder.Services.AddScoped<IModuleLifecyclePorts, EfLifecyclePorts>();
builder.Services.AddScoped<ModuleLifecycleService>();
builder.Services.AddHttpClient("lifecycle", c => c.Timeout = TimeSpan.FromSeconds(30));

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

static string RequireRole(HttpContext ctx) =>
    ctx.User.FindFirst("platform_role")?.Value ?? "";
static string ActorId(HttpContext ctx) =>
    ctx.User.FindFirst("sub")?.Value ?? "unknown";

// Per-(tenant,module) install lock — R8.
var installLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
SemaphoreSlim InstallLock(string tenant, string module) =>
    installLocks.GetOrAdd($"{tenant}:{module.ToLowerInvariant()}", _ => new SemaphoreSlim(1, 1));

// ---- health ----
app.MapGet("/healthz", () => Results.Json(new { status = "ok", service = "platform" }));
app.MapGet("/readyz", async (CatalogDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Json(new { ready = true })
        : Results.Json(new { ready = false }, statusCode: 503));

// ---- platform users (P-BR1; Admin only) ----
app.MapGet("/api/v1/platform/users", async (CatalogDbContext db, HttpContext ctx) =>
{
    if (RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Admin only.", 403);
    var users = await db.PlatformUsers.AsNoTracking().ToListAsync();
    return Results.Json(new { data = users });
}).RequireAuthorization("PlatformAny");

app.MapPost("/api/v1/platform/users", async (CatalogDbContext db, HttpContext ctx, CreateUserRequest req) =>
{
    if (RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Admin only.", 403);
    if (await db.PlatformUsers.AnyAsync(u => u.Email.ToLower() == req.Email.ToLower()))
        return ApiErrors.Problem(ErrorCodes.ValidationError, "Email already exists.", 400);
    var user = new PlatformUser
    {
        Id = Cuid.New("pu"), Email = req.Email, Name = req.Name,
        PasswordHash = "argon2:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), // Argon2 wiring is ops config; never store plain
        Role = Enum.Parse<PlatformRole>(req.Role, true),
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };
    db.PlatformUsers.Add(user);
    await db.SaveChangesAsync();
    await Audit(db, null, ActorId(ctx), RequireRole(ctx), "user.create", "platform_user", user.Id, null, ctx);
    return Results.Json(new { data = user }, statusCode: 201);
}).RequireAuthorization("PlatformAny");

// ---- tenants (P-BR3; Maintainer read-only per R1) ----
app.MapGet("/api/v1/platform/tenants", async (CatalogDbContext db) =>
{
    var tenants = await db.Tenants.AsNoTracking().ToListAsync();
    return Results.Json(new { data = tenants });
}).RequireAuthorization("PlatformAny");

app.MapPost("/api/v1/platform/tenants", async (CatalogDbContext db, HttpContext ctx, IConfiguration cfg, CreateTenantRequest req) =>
{
    if (RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Maintainer is read-only for tenants.", 403);
    var failures = TenantValidator.ValidateCreate(req.Name, req.Slug, req.ResolutionMode, req.Domain, req.PathPrefix, req.ContactEmail,
        req.DeploymentMode, req.DbName, req.Schema, req.BootstrapAdminEmail);
    if (failures.Count > 0)
        return ApiErrors.Problem(ErrorCodes.ValidationError, "Tenant validation failed.",
            400, failures.Select(f => new { field = f.Field, message = f.Message }));
    var name = req.Name.Trim();
    var slug = req.Slug.Trim().ToLowerInvariant();
    var mode = (req.DeploymentMode ?? "dedicated").Trim().ToLowerInvariant();
    var dbName = string.IsNullOrWhiteSpace(req.DbName) ? $"tenant_{slug.Replace("-", "_")}" : req.DbName.Trim();
    var schema = mode == "shared" ? req.Schema!.Trim() : "public";
    var dup = await DuplicateTenantFieldAsync(db, null, name, slug, req.Domain, req.PathPrefix, mode, dbName, schema);
    if (dup is not null) return dup;
    var tenant = new Tenant
    {
        Id = Cuid.New("tnt"), Name = name, Slug = slug,
        Domain = string.IsNullOrWhiteSpace(req.Domain) ? null : req.Domain.Trim().ToLowerInvariant(),
        PathPrefix = string.IsNullOrWhiteSpace(req.PathPrefix) ? TenantValidator.DefaultPathPrefix(slug) : req.PathPrefix.Trim(),
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
    // immediately (no-Postgres dev: InMemory catalog, legacy single store).
    var provisionConn = cfg["TenantStores:ProvisioningConnection"];
    if (string.IsNullOrWhiteSpace(provisionConn))
    {
        tenant.Status = TenantStatus.Active;
        tenant.DbStatus = TenantDbStatus.Ready;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await Audit(db, tenant.Id, ActorId(ctx), RequireRole(ctx), "tenant.create", "tenant", tenant.Id, null, ctx);
        return Results.Json(new { data = tenant }, statusCode: 201);
    }
    try
    {
        await ProvisionTenantStoreAsync(provisionConn, cfg["TenantStores:SharedDatabase"] ?? "module_platform", tenant);
    }
    catch (Exception ex)
    {
        tenant.Status = TenantStatus.Failed;
        tenant.DbStatus = TenantDbStatus.Failed;
        tenant.DbError = ex.Message;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await Audit(db, tenant.Id, ActorId(ctx), RequireRole(ctx), "tenant.create", "tenant", tenant.Id, ex.Message, ctx);
        return ApiErrors.Problem(ErrorCodes.ProvisionFailed, $"Tenant store provisioning failed: {ex.Message}", 502, retryable: true);
    }
    await db.SaveChangesAsync();
    await Audit(db, tenant.Id, ActorId(ctx), RequireRole(ctx), "tenant.create", "tenant", tenant.Id, null, ctx);
    // Store exists; Auth migrates + seeds lazily on first use, then reports
    // Ready via /internal/tenants/{id}/provisioned. 202 while Provisioning.
    return Results.Json(new { data = tenant }, statusCode: 202);
}).RequireAuthorization("PlatformAny");

// Update tenant (Admin only): name/contactEmail/domain/pathPrefix/resolutionMode.
// dbName/schema/slug/id are immutable → 422 IMMUTABLE_FIELD.
app.MapPatch("/api/v1/platform/tenants/{tid}", async (CatalogDbContext db, HttpContext ctx, string tid, JsonElement body) =>
{
    if (RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Maintainer is read-only for tenants.", 403);
    foreach (var immutable in new[] { "id", "slug", "dbName", "schema", "deploymentMode", "bootstrapAdminEmail", "bootstrapAdminName" })
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty(immutable, out _))
            return ApiErrors.Problem(ErrorCodes.ImmutableField, $"Field '{immutable}' is immutable.", 422, new { field = immutable });
    var tenant = await db.Tenants.FindAsync(tid);
    if (tenant is null) return ApiErrors.Problem(ErrorCodes.UnknownTenant, $"Unknown tenant '{tid}'.", 404);
    var name = JsonString(body, "name") ?? tenant.Name;
    var contactEmail = JsonString(body, "contactEmail") ?? tenant.ContactEmail;
    var resolutionMode = JsonString(body, "resolutionMode") ?? tenant.ResolutionMode;
    var domain = JsonHas(body, "domain") ? EmptyToNull(JsonString(body, "domain")) : tenant.Domain;
    var pathPrefix = JsonHas(body, "pathPrefix") ? EmptyToNull(JsonString(body, "pathPrefix")) : tenant.PathPrefix;
    var failures = TenantValidator.ValidateUpdate(name, resolutionMode, domain, pathPrefix, contactEmail);
    if (failures.Count > 0)
        return ApiErrors.Problem(ErrorCodes.ValidationError, "Tenant validation failed.",
            400, failures.Select(f => new { field = f.Field, message = f.Message }));
    var dup = await DuplicateTenantFieldAsync(db, tid, name.Trim(), tenant.Slug, domain, pathPrefix);
    if (dup is not null) return dup;
    tenant.Name = name.Trim();
    tenant.ContactEmail = contactEmail!.Trim();
    tenant.ResolutionMode = resolutionMode!;
    tenant.Domain = string.IsNullOrWhiteSpace(domain) ? null : domain.Trim().ToLowerInvariant();
    tenant.PathPrefix = string.IsNullOrWhiteSpace(pathPrefix) ? TenantValidator.DefaultPathPrefix(tenant.Slug) : pathPrefix.Trim();
    tenant.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    await Audit(db, tenant.Id, ActorId(ctx), RequireRole(ctx), "tenant.update", "tenant", tenant.Id, null, ctx);
    return Results.Json(new { data = tenant });
}).RequireAuthorization("PlatformAny");

app.MapPost("/api/v1/platform/tenants/{tid}/disable", async (CatalogDbContext db, HttpContext ctx, string tid, DisableRequest req) =>
{
    if (RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Maintainer is read-only for tenants.", 403);
    if (string.IsNullOrWhiteSpace(req?.Reason))
        return ApiErrors.Problem(ErrorCodes.ValidationError, "A reason is required to disable a tenant.", 400, new { field = "reason" });
    var tenant = await db.Tenants.FindAsync(tid);
    if (tenant is null) return ApiErrors.Problem(ErrorCodes.UnknownTenant, $"Unknown tenant '{tid}'.", 404);
    if (tenant.Status == TenantStatus.Disabled)
        return ApiErrors.Problem(ErrorCodes.ValidationError, "Tenant is already disabled.", 409);
    tenant.Status = TenantStatus.Disabled;
    tenant.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    await Audit(db, tenant.Id, ActorId(ctx), RequireRole(ctx), "tenant.disable", "tenant", tenant.Id, req.Reason, ctx);
    return Results.Json(new { data = tenant });
}).RequireAuthorization("PlatformAny");

app.MapPost("/api/v1/platform/tenants/{tid}/enable", async (CatalogDbContext db, HttpContext ctx, string tid) =>
{
    if (RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Maintainer is read-only for tenants.", 403);
    var tenant = await db.Tenants.FindAsync(tid);
    if (tenant is null) return ApiErrors.Problem(ErrorCodes.UnknownTenant, $"Unknown tenant '{tid}'.", 404);
    if (tenant.Status == TenantStatus.Active)
        return ApiErrors.Problem(ErrorCodes.ValidationError, "Tenant is already active.", 409);
    tenant.Status = TenantStatus.Active;
    tenant.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    await Audit(db, tenant.Id, ActorId(ctx), RequireRole(ctx), "tenant.enable", "tenant", tenant.Id, null, ctx);
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
    if (RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Maintainer is read-only for tenants.", 403);
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
        await ProvisionTenantStoreAsync(provisionConn, cfg["TenantStores:SharedDatabase"] ?? "module_platform", tenant);
    }
    catch (Exception ex)
    {
        tenant.Status = TenantStatus.Failed;
        tenant.DbStatus = TenantDbStatus.Failed;
        tenant.DbError = ex.Message;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await Audit(db, tenant.Id, ActorId(ctx), RequireRole(ctx), "tenant.provision-retry", "tenant", tenant.Id, ex.Message, ctx);
        return ApiErrors.Problem(ErrorCodes.ProvisionFailed, $"Tenant store provisioning failed: {ex.Message}", 502, retryable: true);
    }
    await db.SaveChangesAsync();
    await Audit(db, tenant.Id, ActorId(ctx), RequireRole(ctx), "tenant.provision-retry", "tenant", tenant.Id, null, ctx);
    return Results.Json(new { data = tenant }, statusCode: 202);
}).RequireAuthorization("PlatformAny");

// Cluster-internal tenant status for Auth/module guards. NOT routed by the
// gateway (outside /api/v1/*) — direct service-to-service only.
// Carries non-secret store metadata (dbName/schema/mode) so Auth can build
// per-tenant connections from its own server-side config (NFR4: secrets never
// transit APIs).
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
    t.SchemaVersion = JsonString(body, "schemaVersion");
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
    t.DbError = JsonString(body, "error") ?? "Unknown provisioning error.";
    t.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    return Results.Json(new { data = new { id = t.Id, status = t.Status.ToString(), dbStatus = t.DbStatus.ToString() } });
});

// ---- module registry (§9.1) ----
app.MapGet("/api/v1/platform/modules", async (CatalogDbContext db) =>
{
    var modules = await db.Modules.AsNoTracking().ToListAsync();
    var versions = await db.ModuleVersions.AsNoTracking().ToListAsync();
    return Results.Json(new
    {
        data = modules.Select(m => new
        {
            m.Id, m.DisplayName, m.Description, m.Owner, m.Status,
            latestVersion = versions.Where(v => v.ModuleId == m.Id).OrderByDescending(v => v.Version).FirstOrDefault()?.Version,
        }),
    });
}).RequireAuthorization("PlatformAny");

app.MapPost("/api/v1/platform/modules", async (CatalogDbContext db, HttpContext ctx, PublishModuleRequest req, IWebHostEnvironment env) =>
{
    if (RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Module publish requires Admin.", 403);
    ManifestFile manifest;
    try { manifest = ManifestParser.Parse(req.ManifestYaml); }
    catch (Exception ex) { return ApiErrors.Problem(ErrorCodes.ValidationError, $"Invalid YAML: {ex.Message}", 400); }

    var usedScopes = new HashSet<string>();
    // collect scopes from stored manifests (best-effort; full parse per version),
    // excluding this module's own versions (re-publish with same scope is allowed).
    foreach (var v in await db.ModuleVersions.AsNoTracking().Where(v => v.ModuleId != manifest.Module.Name).ToListAsync())
    {
        try { usedScopes.Add(ManifestParser.Parse(v.ManifestYaml).Frontend.Scope); } catch { /* ignore legacy rows */ }
    }
    var failures = ManifestValidator.Validate(manifest, usedScopes, env.IsDevelopment());
    if (failures.Count > 0)
        return ApiErrors.Problem(ErrorCodes.ValidationError, "Manifest validation failed.",
            400, failures.Select(f => new { field = f.Field, message = f.Message }));

    // circular dependency across registry: publish rejected (edge case 4)
    if (manifest.Dependencies.Any(d => string.Equals(d.Name, manifest.Module.Name, StringComparison.OrdinalIgnoreCase)))
        return ApiErrors.Problem(ErrorCodes.ValidationError, "Circular dependency: module depends on itself.", 400);

    var module = await db.Modules.FindAsync(manifest.Module.Name);
    if (module is null)
    {
        module = new PlatformModule
        {
            Id = manifest.Module.Name, DisplayName = manifest.Module.DisplayName,
            Description = manifest.Module.Description, Owner = manifest.Module.Owner,
            Icon = manifest.Module.Icon, Category = manifest.Module.Category,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Modules.Add(module);
    }
    if (await db.ModuleVersions.AnyAsync(v => v.ModuleId == module.Id && v.Version == manifest.Module.Version))
        return ApiErrors.Problem(ErrorCodes.ValidationError, "Version already published (immutable).", 400,
            new { field = "module.version" });

    using var sha = SHA256.Create();
    db.ModuleVersions.Add(new ModuleVersion
    {
        Id = Cuid.New("mv"), ModuleId = module.Id, Version = manifest.Module.Version,
        ManifestYaml = req.ManifestYaml,
        ManifestHash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(req.ManifestYaml))).ToLowerInvariant(),
        ArtifactApiUrl = req.ArtifactApiUrl, ArtifactRemoteUrl = req.ArtifactRemoteUrl,
        PublishedBy = ActorId(ctx), PublishedAt = DateTimeOffset.UtcNow,
    });
    await db.SaveChangesAsync();
    await Audit(db, null, ActorId(ctx), RequireRole(ctx), "module.publish", "module", $"{module.Id}@{manifest.Module.Version}", null, ctx);
    return Results.Json(new { data = new { id = module.Id, version = manifest.Module.Version } }, statusCode: 201);
}).RequireAuthorization("PlatformAny");

app.MapPost("/api/v1/platform/modules/{id}/deprecate", async (CatalogDbContext db, HttpContext ctx, string id) =>
{
    if (RequireRole(ctx) != "Admin") return ApiErrors.Problem(ErrorCodes.Forbidden, "Deprecate requires Admin.", 403);
    var module = await db.Modules.FindAsync(id);
    if (module is null) return ApiErrors.Problem(ErrorCodes.UnknownModule, $"Unknown module '{id}'.", 404);
    module.Status = ModuleStatus.Deprecated;
    module.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    await Audit(db, null, ActorId(ctx), RequireRole(ctx), "module.deprecate", "module", id, null, ctx);
    return Results.Json(new { data = module });
}).RequireAuthorization("PlatformAny");

// ---- per-tenant lifecycle (§9.1 platform view + §9.2 tenant self-service share the service) ----
async Task<IResult> RunLifecycle(HttpContext ctx, CatalogDbContext db, ModuleLifecycleService svc,
    string tenantId, string moduleId, string? version, string action)
{
    var (_, tenantError) = await RequireUsableTenantAsync(db, tenantId);
    if (tenantError is not null) return tenantError;
    if (CheckTenantHeader(ctx, tenantId) is { } headerError) return headerError;
    var gate = action is "install" or "upgrade";
    if (gate && RequireRole(ctx) is not ("Admin" or "TENANT_ADMIN"))
    {
        // platform Admin always allowed; tenant admin allowed via tenant JWT (claim tenant_admin=true + matching tenant)
        var isTenantAdmin = ctx.User.FindFirst("tenant_admin")?.Value == "true"
            && ctx.User.FindFirst("tenantId")?.Value == tenantId;
        if (!isTenantAdmin && RequireRole(ctx) != "Admin")
            return ApiErrors.Problem(ErrorCodes.Forbidden, "Not allowed.", 403);
    }
    var sem = InstallLock(tenantId, moduleId);
    await sem.WaitAsync();
    try
    {
        var result = await svc.InstallAsync(tenantId, moduleId, version, ActorId(ctx),
            string.IsNullOrEmpty(RequireRole(ctx)) ? "TENANT_ADMIN" : RequireRole(ctx), ctx.RequestAborted);
        return result.Ok
            ? Results.Json(new { data = new { moduleId, version = result.Value, state = "INSTALLED" } }, statusCode: 202)
            : MapLifecycleError(result.Error!);
    }
    finally { sem.Release(); }
}

app.MapGet("/api/v1/platform/tenants/{tid}/modules", async (CatalogDbContext db, string tid) =>
{
    if (!await db.Tenants.AnyAsync(t => t.Id == tid))
        return ApiErrors.Problem(ErrorCodes.UnknownTenant, $"Unknown tenant '{tid}'.", 404);
    var installs = await db.Installs.Where(i => i.TenantId == tid).AsNoTracking().ToListAsync();
    return Results.Json(new { data = installs });
}).RequireAuthorization("PlatformAny");

app.MapPost("/api/v1/platform/tenants/{tid}/modules/{id}/install",
    (HttpContext ctx, CatalogDbContext db, ModuleLifecycleService svc, string tid, string id, InstallRequest req) =>
        RunLifecycle(ctx, db, svc, tid, id, req.Version, "install")).RequireAuthorization();

app.MapPost("/api/v1/platform/tenants/{tid}/modules/{id}/upgrade",
    (HttpContext ctx, CatalogDbContext db, ModuleLifecycleService svc, string tid, string id, UpgradeRequest req) =>
        UpgradeModuleAsync(ctx, db, svc, tid, id, req.ToVersion)).RequireAuthorization();

// ---- platform login: mints platform JWT (role=Admin|Maintainer, no tenant) ----
app.MapPost("/api/v1/platform/auth/login", async (CatalogDbContext db, IConfiguration cfg, PlatformLoginRequest req) =>
{
    var user = await db.PlatformUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == req.Email.ToLower());
    if (user is null || !user.IsActive)
        return ApiErrors.Problem(ErrorCodes.Unauthorized, "Invalid credentials.", 401);
    // NOTE: password verification stubbed in v1 (see P5 note); production wires Argon2 here.
    user.LastLoginAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    var token = JwtMint.Mint(JwtMint.Secret(cfg), new Dictionary<string, string>
    {
        ["sub"] = user.Id, ["email"] = user.Email, ["platform_role"] = user.Role.ToString(),
    });
    return Results.Json(new { data = new { accessToken = token, role = user.Role.ToString() } });
});

// Shared lifecycle guards: platform Admin always allowed; tenant writers need
// tenant_admin=true + matching tenantId claim (§8.2 Tenant Admin).
static bool CanWrite(HttpContext ctx, string tid)
{
    if ((ctx.User.FindFirst("platform_role")?.Value) == "Admin") return true;
    return ctx.User.FindFirst("tenant_admin")?.Value == "true"
        && ctx.User.FindFirst("tenantId")?.Value == tid;
}
static string ActorRole(HttpContext ctx) =>
    string.IsNullOrEmpty(RequireRole(ctx)) ? "TENANT_ADMIN" : RequireRole(ctx);

async Task<IResult> EnableModuleAsync(CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string tid, string id)
{
    if (!CanWrite(ctx, tid)) return ApiErrors.Problem(ErrorCodes.Forbidden, "Not allowed.", 403);
    var (_, tenantError) = await RequireUsableTenantAsync(db, tid);
    if (tenantError is not null) return tenantError;
    if (CheckTenantHeader(ctx, tid) is { } headerError) return headerError;
    var install = await db.Installs.FindAsync(tid, id);
    if (install is null) return ApiErrors.Problem(ErrorCodes.ModuleNotInstalled, $"Module '{id}' not installed.", 404);
    // R7: dependencies enabled — gate on install states (health check is the module's /readyz).
    var states = await db.Installs.Where(i => i.TenantId == tid).AsNoTracking().ToListAsync();
    var manifest = await ports.GetManifestAsync(id, install.Version, ctx.RequestAborted);
    if (manifest is not null)
    {
        var missing = manifest.Dependencies
            .Where(d => !states.Any(s => s.ModuleId.Equals(d.Name, StringComparison.OrdinalIgnoreCase)
                && s.State == InstallState.Installed && s.Enabled))
            .Select(d => d.Name).ToList();
        if (missing.Count > 0)
            return ApiErrors.Problem(ErrorCodes.ModuleNotReady,
                $"Dependencies not enabled: {string.Join(", ", missing)}.", 422, new { missing });
        try { await ports.ApplyGatewayRoutesAsync(tid, manifest, enabled: true, ctx.RequestAborted); }
        catch (Exception ex) { return ApiErrors.Problem(ErrorCodes.GatewayApplyFailed, ex.Message, 502, retryable: true); }
    }
    install.State = InstallState.Installed; install.Enabled = true; install.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    await Audit(db, tid, ActorId(ctx), ActorRole(ctx), "module.enable", "module", id, null, ctx);
    return Results.Json(new { data = install });
}

async Task<IResult> DisableModuleAsync(CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string tid, string id, string? reason)
{
    if (!CanWrite(ctx, tid)) return ApiErrors.Problem(ErrorCodes.Forbidden, "Not allowed.", 403);
    var (_, tenantError) = await RequireUsableTenantAsync(db, tid);
    if (tenantError is not null) return tenantError;
    if (CheckTenantHeader(ctx, tid) is { } headerError) return headerError;
    var install = await db.Installs.FindAsync(tid, id);
    if (install is null) return ApiErrors.Problem(ErrorCodes.ModuleNotInstalled, $"Module '{id}' not installed.", 404);
    var manifest = await ports.GetManifestAsync(id, install.Version, ctx.RequestAborted);
    if (manifest is not null)
    {
        try { await ports.ApplyGatewayRoutesAsync(tid, manifest, enabled: false, ctx.RequestAborted); }
        catch (Exception ex) { return ApiErrors.Problem(ErrorCodes.GatewayApplyFailed, ex.Message, 502, retryable: true); }
    }
    install.State = InstallState.Disabled; install.Enabled = false; install.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    await Audit(db, tid, ActorId(ctx), ActorRole(ctx), "module.disable", "module", id, reason, ctx);
    return Results.Json(new { data = install });
}

async Task<IResult> UninstallModuleAsync(CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string tid, string id)
{
    if (!CanWrite(ctx, tid)) return ApiErrors.Problem(ErrorCodes.Forbidden, "Not allowed.", 403);
    var (_, tenantError) = await RequireUsableTenantAsync(db, tid);
    if (tenantError is not null) return tenantError;
    if (CheckTenantHeader(ctx, tid) is { } headerError) return headerError;
    var states = await db.Installs.Where(i => i.TenantId == tid).AsNoTracking().ToListAsync();
    // R2 guard: implemented against stored manifests' dependency lists
    var blockers = new List<string>();
    foreach (var s in states)
    {
        if (s.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
        if (s.State is not (InstallState.Installed or InstallState.Disabled)) continue;
        var ver = await db.ModuleVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.ModuleId == s.ModuleId && v.Version == s.Version);
        if (ver is null) continue;
        try
        {
            var deps = ManifestParser.Parse(ver.ManifestYaml).Dependencies;
            if (deps.Any(d => d.Name.Equals(id, StringComparison.OrdinalIgnoreCase)))
                blockers.Add(s.ModuleId);
        }
        catch { /* ignore */ }
    }
    if (blockers.Count > 0)
        return ApiErrors.Problem(ErrorCodes.DependencyInUse,
            $"Module '{id}' is required by installed module(s).", 409, new { dependents = blockers });

    var install = await db.Installs.FindAsync(tid, id);
    if (install is null) return ApiErrors.Problem(ErrorCodes.ModuleNotInstalled, $"Module '{id}' not installed.", 404);
    // R6: revoke routes + seed grants; data archived per retention (history stays in audit).
    var uninstallManifest = await ports.GetManifestAsync(id, install.Version, ctx.RequestAborted);
    if (uninstallManifest is not null)
    {
        try
        {
            await ports.RemoveModuleRoutesAsync(tid, uninstallManifest, ctx.RequestAborted);
            await ports.RevokeSeedAsync(tid, uninstallManifest, ctx.RequestAborted);
        }
        catch (Exception ex) { return ApiErrors.Problem(ErrorCodes.ProvisionFailed, ex.Message, 502, retryable: true); }
    }
    db.Installs.Remove(install);
    await db.SaveChangesAsync();
    await Audit(db, tid, ActorId(ctx), ActorRole(ctx), "module.uninstall", "module", id, null, ctx);
    return Results.Json(new { data = new { moduleId = id, state = "NOT_INSTALLED" } });
}

async Task<IResult> UpgradeModuleAsync(HttpContext ctx, CatalogDbContext db, ModuleLifecycleService svc, string tid, string id, string toVersion)
{
    if (!CanWrite(ctx, tid)) return ApiErrors.Problem(ErrorCodes.Forbidden, "Not allowed.", 403);
    var (_, tenantError) = await RequireUsableTenantAsync(db, tid);
    if (tenantError is not null) return tenantError;
    if (CheckTenantHeader(ctx, tid) is { } headerError) return headerError;
    var sem = InstallLock(tid, id);
    await sem.WaitAsync();
    try
    {
        var result = await svc.UpgradeAsync(tid, id, toVersion, ActorId(ctx), ActorRole(ctx), ctx.RequestAborted);
        return result.Ok
            ? Results.Json(new { data = new { moduleId = id, version = result.Value, state = "INSTALLED" } }, statusCode: 202)
            : MapLifecycleError(result.Error!);
    }
    finally { sem.Release(); }
}

app.MapPost("/api/v1/platform/tenants/{tid}/modules/{id}/enable",
    (CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string tid, string id) =>
        EnableModuleAsync(db, ctx, ports, tid, id)).RequireAuthorization();

app.MapPost("/api/v1/platform/tenants/{tid}/modules/{id}/disable",
    (CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string tid, string id, DisableRequest req) =>
        DisableModuleAsync(db, ctx, ports, tid, id, req?.Reason)).RequireAuthorization();

app.MapPost("/api/v1/platform/tenants/{tid}/modules/{id}/uninstall",
    (CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string tid, string id) =>
        UninstallModuleAsync(db, ctx, ports, tid, id)).RequireAuthorization();

// ---- tenant self-service catalog (§9.2: same lifecycle, tenant JWT) ----
app.MapGet("/api/v1/tenant/modules", async (CatalogDbContext db, HttpContext ctx) =>
{
    var tenantId = ctx.User.FindFirst("tenantId")?.Value;
    if (tenantId is null) return ApiErrors.Problem(ErrorCodes.ForbiddenTenant, "Tenant context required.", 403);
    if (CheckTenantHeader(ctx, tenantId) is { } headerError) return headerError;
    var (_, tenantError) = await RequireUsableTenantAsync(db, tenantId);
    if (tenantError is not null) return tenantError;
    var modules = await db.Modules.Where(m => m.Status == ModuleStatus.Available).AsNoTracking().ToListAsync();
    var installs = await db.Installs.Where(i => i.TenantId == tenantId).AsNoTracking().ToListAsync();
    return Results.Json(new
    {
        data = modules.Select(m => new
        {
            m.Id, m.DisplayName, m.Description, m.Owner,
            installState = installs.FirstOrDefault(i => i.ModuleId == m.Id)?.State.ToString() ?? "NOT_INSTALLED",
        }),
    });
}).RequireAuthorization();

app.MapPost("/api/v1/tenant/modules/{id}/install",
    (HttpContext ctx, CatalogDbContext db, ModuleLifecycleService svc, string id, InstallRequest req) =>
        RunLifecycle(ctx, db, svc, ctx.User.FindFirst("tenantId")?.Value ?? "", id, req.Version, "install"))
    .RequireAuthorization();

static string TenantId(HttpContext ctx) => ctx.User.FindFirst("tenantId")?.Value ?? "";

app.MapGet("/api/v1/tenant/modules/{id}", async (CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string id) =>
{
    var tid = TenantId(ctx);
    if (string.IsNullOrEmpty(tid)) return ApiErrors.Problem(ErrorCodes.ForbiddenTenant, "Tenant context required.", 403);
    if (CheckTenantHeader(ctx, tid) is { } detailHeaderError) return detailHeaderError;
    var (_, detailTenantError) = await RequireUsableTenantAsync(db, tid);
    if (detailTenantError is not null) return detailTenantError;
    var module = await db.Modules.FindAsync(id);
    if (module is null) return ApiErrors.Problem(ErrorCodes.UnknownModule, $"Unknown module '{id}'.", 404);
    var install = await db.Installs.FindAsync(tid, id);
    var versions = await db.ModuleVersions.Where(v => v.ModuleId == id).OrderBy(v => v.Version)
        .Select(v => v.Version).ToListAsync();
    // Install plan preview: resolve deps for the requested (or latest) version without applying.
    var preview = await ports.AvailableVersionsAsync(id, ctx.RequestAborted);
    var planTarget = preview.Count == 0 ? null : SemverOrdering.Latest(preview);
    IReadOnlyList<DependencyResolver.PlanStep> plan = Array.Empty<DependencyResolver.PlanStep>();
    if (planTarget is not null)
    {
        var planResult = DependencyResolver.Resolve(id, $"={planTarget}",
            n => ports.AvailableVersionsAsync(n, CancellationToken.None).GetAwaiter().GetResult(),
            (n, v) => ports.GetManifestAsync(n, v, CancellationToken.None).GetAwaiter().GetResult()?.Dependencies
                ?? new List<ManifestDependency>());
        if (planResult.Ok) plan = planResult.Value!;
    }
    return Results.Json(new
    {
        data = new
        {
            module, installState = install?.State.ToString() ?? "NOT_INSTALLED",
            installedVersion = install?.Version, versions,
            installPlan = plan.Select(s => new { module = s.Name, version = s.Version }),
        },
    });
}).RequireAuthorization();

app.MapPost("/api/v1/tenant/modules/{id}/enable",
    (CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string id) =>
        EnableModuleAsync(db, ctx, ports, TenantId(ctx), id)).RequireAuthorization();
app.MapPost("/api/v1/tenant/modules/{id}/disable",
    (CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string id, DisableRequest req) =>
        DisableModuleAsync(db, ctx, ports, TenantId(ctx), id, req?.Reason)).RequireAuthorization();
app.MapPost("/api/v1/tenant/modules/{id}/uninstall",
    (CatalogDbContext db, HttpContext ctx, IModuleLifecyclePorts ports, string id) =>
        UninstallModuleAsync(db, ctx, ports, TenantId(ctx), id)).RequireAuthorization();
app.MapPost("/api/v1/tenant/modules/{id}/upgrade",
    (HttpContext ctx, CatalogDbContext db, ModuleLifecycleService svc, string id, UpgradeRequest req) =>
        UpgradeModuleAsync(ctx, db, svc, TenantId(ctx), id, req.ToVersion)).RequireAuthorization();

// ---- frontend bootstrap (SR-6b): Vite-only payload, never the manifest (R10) ----
app.MapGet("/api/v1/tenant/frontend-bootstrap", async (CatalogDbContext db, HttpContext ctx) =>
{
    var tenantId = ctx.User.FindFirst("tenantId")?.Value;
    if (tenantId is null) return ApiErrors.Problem(ErrorCodes.ForbiddenTenant, "Tenant context required.", 403);
    if (CheckTenantHeader(ctx, tenantId) is { } bootstrapHeaderError) return bootstrapHeaderError;
    var (_, bootstrapTenantError) = await RequireUsableTenantAsync(db, tenantId);
    if (bootstrapTenantError is not null) return bootstrapTenantError;
    var installs = await db.Installs
        .Where(i => i.TenantId == tenantId && i.State == InstallState.Installed && i.Enabled)
        .AsNoTracking().ToListAsync();
    var modules = new List<object>();
    foreach (var ins in installs)
    {
        var ver = await db.ModuleVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.ModuleId == ins.ModuleId && v.Version == ins.Version);
        if (ver is null) continue;
        ManifestFile manifest;
        try { manifest = ManifestParser.Parse(ver.ManifestYaml); }
        catch { continue; }
        modules.Add(new
        {
            moduleId = ins.ModuleId, version = ins.Version,
            scope = manifest.Frontend.Scope, remoteUrl = manifest.Frontend.RemoteUrl,
            modulePrefix = manifest.Frontend.ModulePrefix,
            routes = manifest.Frontend.Routes.Select(r => new { r.Path, r.Exposes, r.RequiredPermission, r.MenuId }),
            menus = manifest.Frontend.Menus.Select(m => new { m.Id, m.ParentId, m.Title, m.Icon, m.Route, m.RequiredPermission, m.Order }),
            uiPermissions = manifest.Frontend.UiPermissions,
        });
    }
    ctx.Response.Headers.CacheControl = "private, max-age=30";
    return Results.Json(new { data = new { tenantId, modules } });
}).RequireAuthorization();

// Dev auto-seed: first Development run creates an admin + demo tenant so the
// lifecycle can be smoke-tested end to end (login → publish → install).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var seedDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    if (string.IsNullOrWhiteSpace(catalogConn))
        await seedDb.Database.EnsureCreatedAsync();
    else
        await seedDb.Database.MigrateAsync(); // Postgres: apply EF migrations (P9)
    if (!await seedDb.PlatformUsers.AnyAsync())
    {
        seedDb.PlatformUsers.Add(new PlatformUser
        {
            Id = Cuid.New("pu"), Email = "admin@local", Name = "Platform Admin",
            PasswordHash = "dev-stub", Role = PlatformRole.Admin,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
    }
    if (!await seedDb.Tenants.AnyAsync())
    {
        seedDb.Tenants.Add(new Tenant
        {
            Id = "tnt_demo", Name = "Demo Tenant", Slug = "demo",
            DbName = "tenant_demo", Schema = "public", ContactEmail = "demo@local",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
    }
    await seedDb.SaveChangesAsync();
}

app.Run();

static IResult MapLifecycleError(ErrorEnvelope e) => e.Code switch
{
    ErrorCodes.UnknownTenant => ApiErrors.Problem(e.Code, e.Message, 404, e.Details),
    ErrorCodes.TenantDisabled => ApiErrors.Problem(e.Code, e.Message, 403, e.Details),
    ErrorCodes.UnknownModule or ErrorCodes.UnknownVersion => ApiErrors.Problem(e.Code, e.Message, 404, e.Details),
    ErrorCodes.DependencyMissing or ErrorCodes.VersionConflict => ApiErrors.Problem(e.Code, e.Message, 422, e.Details),
    _ => ApiErrors.Problem(e.Code, e.Message, 502, e.Details, retryable: true),
};

static async Task Audit(CatalogDbContext db, string? tenantId, string actorId, string actorRole,
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
static async Task<IResult?> DuplicateTenantFieldAsync(
    CatalogDbContext db, string? excludeId, string name, string slug, string? domain, string? pathPrefix,
    string? deploymentMode = null, string? dbName = null, string? schema = null)
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
    var prefix = string.IsNullOrWhiteSpace(pathPrefix) ? TenantValidator.DefaultPathPrefix(slug) : pathPrefix.Trim();
    if (await db.Tenants.AnyAsync(t => t.Id != excludeId && t.PathPrefix == prefix))
        return ApiErrors.Problem(ErrorCodes.DuplicateTenant, "Path prefix is already used by another tenant.", 409, new { field = "pathPrefix" });
    // BR-T1: dbName unique among DEDICATED_DB tenants; schema unique when set.
    if ((deploymentMode ?? "dedicated") == "dedicated" && !string.IsNullOrWhiteSpace(dbName))
    {
        var lowerDb = dbName.Trim().ToLower();
        if (await db.Tenants.AnyAsync(t => t.Id != excludeId && t.DeploymentMode == "dedicated" && t.DbName.ToLower() == lowerDb))
            return ApiErrors.Problem(ErrorCodes.DuplicateTenant, "Database name is already used by another tenant.", 409, new { field = "dbName" });
    }
    if (!string.IsNullOrWhiteSpace(schema) && schema.Trim() != "public")
    {
        var s = schema.Trim();
        if (await db.Tenants.AnyAsync(t => t.Id != excludeId && t.Schema == s))
            return ApiErrors.Problem(ErrorCodes.DuplicateTenant, "Schema is already used by another tenant.", 409, new { field = "schema" });
    }
    return null;
}

// Physical store provisioning: CREATE DATABASE (dedicated, idempotent when
// the DB already exists → reuse) or CREATE SCHEMA (shared). Runs with the
// privileged provisioning connection (module_migrator, CREATEDB).
static async Task ProvisionTenantStoreAsync(string provisionConn, string sharedDatabase, Tenant tenant)
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

static string QuoteIdent(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";

static bool JsonHas(JsonElement body, string name) =>
    body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out _);

static string? JsonString(JsonElement body, string name) =>
    body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
        ? v.GetString() : null;

static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

// Tenant must exist and be usable (Active). Unknown → 404, provisioning →
// 403 TENANT_NOT_READY, otherwise → 403 TENANT_DISABLED.
static async Task<(Tenant? tenant, IResult? error)> RequireUsableTenantAsync(CatalogDbContext db, string tid)
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
static IResult? CheckTenantHeader(HttpContext ctx, string effectiveTid)
{
    var header = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(header) &&
        !string.Equals(header.Trim(), effectiveTid, StringComparison.OrdinalIgnoreCase))
        return ApiErrors.Problem(ErrorCodes.ForbiddenTenant, "Tenant header does not match request tenant.", 403);
    return null;
}

public sealed record CreateUserRequest(string Email, string Name, string Role);
public sealed record CreateTenantRequest(string Name, string Slug, string? Domain, string? ContactEmail, string? ResolutionMode = null, string? PathPrefix = null, string? DeploymentMode = null, string? DbName = null, string? Schema = null, string? BootstrapAdminEmail = null, string? BootstrapAdminName = null);
public sealed record PublishModuleRequest(string ManifestYaml, string? ArtifactApiUrl, string? ArtifactRemoteUrl);
public sealed record InstallRequest(string? Version);
public sealed record UpgradeRequest(string ToVersion);
public sealed record DisableRequest(string? Reason);
public sealed record PlatformLoginRequest(string Email, string Password);

// EF-backed lifecycle ports: physical side effects go over HTTP —
// gateway routes via Gateway.Api internal API (source=module-install),
// tenant seed via Auth.Api internal API (idempotent upserts). Failures throw
// so the lifecycle marks the install FAILED per SR-5 (rollback of that module).
public sealed class EfLifecyclePorts(CatalogDbContext db, IHttpClientFactory http, IConfiguration cfg, ILogger<EfLifecyclePorts> log) : IModuleLifecyclePorts
{
    public async Task<TenantView?> GetTenantAsync(string tenantId, CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        return t is null ? null : new TenantView(t.Id, t.Status.ToString());
    }

    public async Task<IReadOnlyList<string>> AvailableVersionsAsync(string moduleId, CancellationToken ct) =>
        await db.ModuleVersions.Where(v => v.ModuleId == moduleId).OrderBy(v => v.Version)
            .Select(v => v.Version).ToListAsync(ct);

    public async Task<ManifestFile?> GetManifestAsync(string moduleId, string version, CancellationToken ct)
    {
        var v = await db.ModuleVersions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ModuleId == moduleId && x.Version == version, ct);
        if (v is null) return null;
        try { return ManifestParser.Parse(v.ManifestYaml); } catch { return null; }
    }

    public async Task<IDictionary<string, InstallState>> GetInstallStatesAsync(string tenantId, CancellationToken ct) =>
        (await db.Installs.Where(i => i.TenantId == tenantId).AsNoTracking().ToListAsync(ct))
            .ToDictionary(i => i.ModuleId, i => i.State, StringComparer.OrdinalIgnoreCase);

    public async Task SaveInstallAsync(string tenantId, string moduleId, string version, InstallState state, bool enabled, string? error, CancellationToken ct)
    {
        var existing = await db.Installs.FindAsync([tenantId, moduleId], ct);
        if (existing is null)
            db.Installs.Add(new TenantModuleInstall
            {
                TenantId = tenantId, ModuleId = moduleId, Version = version,
                State = state, Enabled = enabled, InstalledAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow, Error = error,
            });
        else
        {
            existing.Version = version; existing.State = state; existing.Enabled = enabled;
            existing.Error = error; existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetManifestYamlAsync(string moduleId, string version, CancellationToken ct) =>
        (await db.ModuleVersions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ModuleId == moduleId && x.Version == version, ct))?.ManifestYaml;

    public async Task<string?> GetInstalledVersionAsync(string tenantId, string moduleId, CancellationToken ct) =>
        (await db.Installs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ModuleId == moduleId, ct))?.Version;

    public async Task RunMigrateHookAsync(string moduleId, string fromVersion, string toVersion, CancellationToken ct)
    {
        if (fromVersion == toVersion) return;
        var hookBase = (await db.ModuleVersions.AsNoTracking()
            .Where(x => x.ModuleId == moduleId && x.Version == toVersion)
            .Select(x => x.ArtifactApiUrl).FirstOrDefaultAsync(ct))?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(hookBase))
        {
            log.LogInformation("No artifactApiUrl for {module} — migrate hook skipped", moduleId);
            return;
        }
        using var client = http.CreateClient("lifecycle");
        using var res = await client.PostAsJsonAsync($"{hookBase}/internal/migrate",
            new { fromVersion, toVersion }, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Migrate hook rejected ({(int)res.StatusCode}): {body}");
        }
    }

    public async Task ApplyGatewayRoutesAsync(string tenantId, ManifestFile manifest, bool enabled, CancellationToken ct)
    {
        var baseUrl = (cfg["Gateway:BaseUrl"] ?? "http://localhost:5100").TrimEnd('/');
        using var client = http.CreateClient("lifecycle");
        using var res = await client.PostAsJsonAsync($"{baseUrl}/api/v1/gateway/internal/module-routes",
            new
            {
                moduleId = manifest.Module.Name,
                upstreams = manifest.Api.Routes.Select(r => new
                {
                    upstream = r.Upstream, downstream = r.Downstream,
                    methods = r.Methods.ToArray(),
                }).ToArray(),
                enabled,
            }, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Gateway apply failed ({(int)res.StatusCode}): {body}");
        }
        log.LogInformation("Gateway routes applied module={module} enabled={enabled}", manifest.Module.Name, enabled);
    }

    public async Task RemoveModuleRoutesAsync(string tenantId, ManifestFile manifest, CancellationToken ct)
    {
        var baseUrl = (cfg["Gateway:BaseUrl"] ?? "http://localhost:5100").TrimEnd('/');
        using var client = http.CreateClient("lifecycle");
        using var res = await client.PostAsJsonAsync($"{baseUrl}/api/v1/gateway/internal/module-routes",
            new
            {
                moduleId = manifest.Module.Name,
                upstreams = Array.Empty<object>(),
                enabled = false,
                remove = true,
            }, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Gateway remove failed ({(int)res.StatusCode}): {body}");
        }
    }

    public async Task SeedTenantAsync(string tenantId, ManifestFile manifest, CancellationToken ct)
    {
        var yaml = await GetManifestYamlAsync(manifest.Module.Name, manifest.Module.Version, ct);
        var baseUrl = (cfg["Auth:BaseUrl"] ?? "http://localhost:5122").TrimEnd('/');
        using var client = http.CreateClient("lifecycle");
        using var res = await client.PostAsJsonAsync($"{baseUrl}/internal/seed:apply",
            new { moduleId = manifest.Module.Name, manifestYaml = yaml ?? "", tenantId }, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Tenant seed failed ({(int)res.StatusCode}): {body}");
        }
    }

    public async Task RevokeSeedAsync(string tenantId, ManifestFile manifest, CancellationToken ct)
    {
        var baseUrl = (cfg["Auth:BaseUrl"] ?? "http://localhost:5122").TrimEnd('/');
        using var client = http.CreateClient("lifecycle");
        using var res = await client.PostAsJsonAsync($"{baseUrl}/internal/seed:revoke",
            new { moduleId = manifest.Module.Name, tenantId }, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Tenant seed revoke failed ({(int)res.StatusCode}): {body}");
        }
    }

    public Task AuditAsync(string? tenantId, string actorId, string actorRole, string action, string resourceType, string resourceId, string? reason, CancellationToken ct)
    {
        db.Audits.Add(new TenantAudit
        {
            Id = Cuid.New("aud"), TenantId = tenantId, ActorId = actorId, ActorRole = actorRole,
            Action = action, ResourceType = resourceType, ResourceId = resourceId, Reason = reason,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return db.SaveChangesAsync(ct);
    }
}

// Required so WebApplicationFactory / tests can reference the entry point.
public partial class Program;
