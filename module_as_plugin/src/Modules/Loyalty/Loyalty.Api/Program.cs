// Sample module API :5201 — built from the module template (§10.5, M-BR1).
// Every module implements: GET /healthz, GET /readyz, POST /internal/seed:preview,
// POST /internal/migrate. No GET /manifest (R10).
// Endpoint mappings live in Endpoints/ (ModuleEndpoints, LoyaltyHelpers);
// contracts in Models/.
using System.Text;
using Loyalty.Api.Endpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using ModulePlatform.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
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
app.MapDefaultEndpoints();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

ModuleEndpoints.Map(app);

app.Run();

public partial class Program;
