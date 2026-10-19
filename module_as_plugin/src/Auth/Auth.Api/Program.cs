// Auth API :5122 — tenant users/roles/permissions/menus + resolve endpoint (A-BR5).
// Permission model = role permissions UNION user additional permissions, ACTIVE only (A-BR3).
// Endpoint mappings live in Endpoints/ (per-area modules); contracts in Models/.
using System.Text;
using Auth.Api.Endpoints;
using Auth.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ModulePlatform.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwtSecret = builder.RequireSecret("Jwt:SecretKey", Extensions.DevJwtSecret);
builder.RequireSecret("InternalApi:Key", Extensions.DevInternalKey);
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
builder.Services.AddSingleton<PermissionVersionCache>();
builder.Services.AddHttpClient("platform", c => c.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddScoped<Auth.Api.Infrastructure.TenantDbProvider>();

var tenantConn = builder.Configuration.GetConnectionString("Tenant");
if (string.IsNullOrWhiteSpace(tenantConn))
    throw new InvalidOperationException(
        "Missing required connection string 'Tenant'. Set ConnectionStrings__Tenant to the physical PostgreSQL tenant database, e.g. " +
        "\"Host=localhost;Username=module_platform;Password=module_platform;Database=module_platform\". InMemory is not supported.");
builder.Services.AddDbContext<TenantDbContext>(o => o.UseNpgsql(tenantConn));
builder.Services.AddScoped<Auth.Api.Application.SeedApplier>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

// Physical PostgreSQL only (P9): apply EF migrations on startup.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Database.MigrateAsync();
}

HealthEndpoints.Map(app);
ResolveEndpoints.Map(app);
ProfileEndpoints.Map(app);
UserEndpoints.Map(app);
RoleEndpoints.Map(app);
PermissionEndpoints.Map(app);
SeedEndpoints.Map(app);
LoginEndpoints.Map(app);

app.Run();

public partial class Program;
