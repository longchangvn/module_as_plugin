// Sample module API :5201 — built from the module template (§10.5, M-BR1).
// Every module implements: GET /healthz, GET /readyz, POST /internal/seed:preview,
// POST /internal/migrate. No GET /manifest (R10).
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
app.MapGet("/api/v1/members", () => Results.Json(new { data = new[] { new { id = "m_1", name = "Ada" } } }))
    .RequireAuthorization();

app.Run();

public sealed record MigrateRequest(string FromVersion, string ToVersion);
public partial class Program;
