// Platform API :5121 — registry + lifecycle + tenants + platform users + frontend bootstrap.
// Clean-architecture wiring: Api (this file + Endpoints/) -> Application -> Domain; persistence via Infrastructure.
// Auth: platform JWT (role=Admin|Maintainer). R1 enforced per endpoint.
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ModulePlatform.ServiceDefaults;
using Platform.Api.Endpoints;
using Platform.Api.Services;
using Platform.Application;
using Platform.Domain;
using Platform.Infrastructure;
using SharedKernel;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwtSecret = builder.RequireSecret("Jwt:SecretKey", Extensions.DevJwtSecret);
builder.RequireSecret("InternalApi:Key", Extensions.DevInternalKey);
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
    throw new InvalidOperationException(
        "Missing required connection string 'Catalog'. Set ConnectionStrings__Catalog to the physical PostgreSQL catalog database, e.g. " +
        "\"Host=localhost;Username=module_platform;Password=module_platform;Database=module_platform\". InMemory is not supported.");
builder.Services.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(catalogConn));

builder.Services.AddScoped<IModuleLifecyclePorts, EfLifecyclePorts>();
builder.Services.AddScoped<ModuleLifecycleService>();
builder.Services.AddHttpClient("lifecycle", c => c.Timeout = TimeSpan.FromSeconds(30));

// Module types (FR-003): one IModuleHostingStrategy per type; adding a type = one registration.
builder.Services.AddSingleton(builder.Configuration.GetSection("PluginHost").Get<PluginHostOptions>() ?? new PluginHostOptions());
builder.Services.AddHttpClient("plugin-host", c => c.Timeout = TimeSpan.FromSeconds(60)); // includes package download + load
builder.Services.AddScoped<IPluginHostClient, HttpPluginHostClient>();
builder.Services.AddScoped<IRemoteModuleClient, HttpRemoteModuleClient>();
builder.Services.AddScoped<IModuleHostingStrategy, RemoteHostingStrategy>();
builder.Services.AddScoped<IModuleHostingStrategy, PluginHostingStrategy>();
builder.Services.AddScoped<ModuleHostingRegistry>();
builder.Services.AddScoped<ModuleHostingCoordinator>();
// Package registration (FR-004..FR-007): limits + immutable file store.
var packageOptions = builder.Configuration.GetSection("ModulePackages").Get<ModulePackageOptions>() ?? new ModulePackageOptions();
builder.Services.AddSingleton(packageOptions);
builder.Services.AddSingleton<ModulePackageStore>();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = packageOptions.MaxPackageBytes + 1024 * 1024);

// Data Protection key ring for tenant connection secrets. Persisted to disk
// (DataProtection:KeyDirectory, default ./keys) — without persistence every
// stored secret is orphaned on restart/redeploy.
var keyDir = Path.GetFullPath(
    builder.Configuration["DataProtection:KeyDirectory"] ?? "./keys",
    builder.Environment.ContentRootPath);
Directory.CreateDirectory(keyDir);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDir));
builder.Services.AddSingleton<TenantConnectionProtector>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

// Per-(tenant,module) install lock — R8.
var installLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

HealthEndpoints.Map(app);
PlatformUserEndpoints.Map(app);
TenantEndpoints.Map(app);
InternalEndpoints.Map(app);
ModuleRegistryEndpoints.Map(app);
LifecycleEndpoints.Map(app, installLocks);
LoginEndpoints.Map(app);
TenantSelfServiceEndpoints.Map(app, installLocks);
BootstrapEndpoints.Map(app);

// Dev auto-seed: first Development run creates an admin + demo tenant so the
// lifecycle can be smoke-tested end to end (login → publish → install).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var seedDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await seedDb.Database.MigrateAsync(); // physical PostgreSQL only (P9); InMemory is not supported
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

public partial class Program;
