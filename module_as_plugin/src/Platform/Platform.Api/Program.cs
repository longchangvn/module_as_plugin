// Platform API :5121 — registry + lifecycle + tenants + platform users + frontend bootstrap.
// Clean-architecture wiring: Api (this file + Endpoints/) -> Application -> Domain; persistence via Infrastructure.
// Auth: platform JWT (role=Admin|Maintainer). R1 enforced per endpoint.
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Platform.Api.Endpoints;
using Platform.Api.Services;
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
    throw new InvalidOperationException(
        "Missing required connection string 'Catalog'. Set ConnectionStrings__Catalog to the physical PostgreSQL catalog database, e.g. " +
        "\"Host=localhost;Username=module_platform;Password=module_platform;Database=module_platform\". InMemory is not supported.");
builder.Services.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(catalogConn));

builder.Services.AddScoped<IModuleLifecyclePorts, EfLifecyclePorts>();
builder.Services.AddScoped<ModuleLifecycleService>();
builder.Services.AddHttpClient("lifecycle", c => c.Timeout = TimeSpan.FromSeconds(30));

var app = builder.Build();
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
