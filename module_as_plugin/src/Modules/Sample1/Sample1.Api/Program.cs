// Sample Module 1 API :5202 — test module built from the module template (§10.5, M-BR1).
// Template endpoints: GET /healthz, GET /readyz, POST /internal/seed:preview,
// POST /internal/migrate. Business: GET /api/v1/data (any tenant user),
// GET /api/v1/protected (SAMPLE_ONE_DATA_PROTECTED, enforced via Auth resolve).
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModulePlatform.ServiceDefaults;
using Sample1.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwtSecret = builder.RequireSecret("Jwt:SecretKey", Extensions.DevJwtSecret);
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
builder.Services.AddHttpClient("auth", c => c.Timeout = TimeSpan.FromSeconds(5));

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

ModuleEndpoints.Map(app);

app.Run();

public partial class Program;
