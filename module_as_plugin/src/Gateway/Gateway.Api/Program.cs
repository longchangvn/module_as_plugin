// Gateway API :5100 — Ocelot proxy + admin API (/api/v1/gateway/**).
// Pipeline: CORS -> TrafficMiddleware -> admin branch (auth) -> tenant/module guard -> Ocelot.
// Config: ocelot.json (persistent) + gateway-services.json registry; single writer (SemaphoreSlim).
// Endpoint mappings live in Endpoints/ (AdminEndpoints, HealthEndpoints);
// services in Services/, middleware in Middleware/, contracts in Models/.
using System.Text;
using System.Threading.RateLimiting;
using Gateway.Api.Endpoints;
using Gateway.Api.Middleware;
using Gateway.Api.Models;
using Gateway.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModulePlatform.ServiceDefaults;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddOcelot(builder.Configuration);
builder.Services.AddSingleton<GatewayStore>();
builder.Services.AddSingleton<TrafficRecorder>();

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
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("GatewayViewer", p => p.RequireAuthenticatedUser())
    .AddPolicy("GatewayAdmin", p => p.RequireClaim("platform_role", "Admin"));

// Credential throttling (FR-A5): fixed window per client IP on login /
// change-password only; everything else is unlimited. Done here because the
// gateway is the only hop that sees the real client address (G-BR3).
var credentialPermits = builder.Configuration.GetValue("RateLimiting:CredentialPermitPerMinute", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        GatewayHelpers.IsCredentialRequest(ctx.Request)
            ? RateLimitPartition.GetFixedWindowLimiter(
                $"cred:{ctx.Connection.RemoteIpAddress}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = credentialPermits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter("none"));
    o.OnRejected = async (c, ct) =>
    {
        c.HttpContext.Response.Headers.RetryAfter = "60";
        await c.HttpContext.Response.WriteAsJsonAsync(new { error = new
        {
            code = "RATE_LIMITED", message = "Too many attempts. Try again later.", details = new { retryAfterSeconds = 60 },
        } }, ct);
    };
});

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<TrafficMiddleware>();
app.UseRateLimiter();

AdminEndpoints.Map(app);

// Tenant + module-enabled guard before proxying (SR-4, G-BR5).
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api/v1/gateway") || ctx.Request.Path is { Value: "/healthz" or "/readyz" or "/swagger" })
    { await next(); return; }
    var store = ctx.RequestServices.GetRequiredService<GatewayStore>();
    var moduleId = GatewayHelpers.ModuleFromPath(ctx.Request.Path.Value ?? "");
    if (moduleId is not null)
    {
        var state = store.GetModuleState(moduleId);
        if (state == ModuleState.NotInstalled)
        {
            ctx.Response.StatusCode = 404;
            await ctx.Response.WriteAsJsonAsync(new { error = new { code = "UNKNOWN_MODULE_ROUTE", message = $"No route for module '{moduleId}'." } });
            return;
        }
        if (state == ModuleState.Disabled)
        {
            ctx.Response.StatusCode = 503;
            await ctx.Response.WriteAsJsonAsync(new { error = new { code = "MODULE_DISABLED", message = $"Module '{moduleId}' is disabled." } });
            return;
        }
    }
    await next();
});

HealthEndpoints.Map(app);

// Ocelot is terminal: run it on a branch for non-admin paths only, so the admin API
// (/api/v1/gateway/**), /healthz and /swagger are answered by endpoints above.
// (Fallback pattern from the gateway requirement: MapWhen branch with its own routing.)
app.MapWhen(
    ctx => !GatewayHelpers.IsLocalPath(ctx.Request.Path),
    branch => { branch.UseOcelot().GetAwaiter().GetResult(); });
app.Run();

public partial class Program;
