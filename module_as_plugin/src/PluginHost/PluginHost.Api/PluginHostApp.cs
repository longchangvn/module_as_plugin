using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.IdentityModel.Tokens;
using ModulePlatform.ServiceDefaults;
using OpenTelemetry.Metrics;
using PluginHost.Api.Endpoints;
using PluginHost.Api.Middleware;
using PluginHost.Api.Routing;
using PluginHost.Api.Runtime;
using PluginHost.Api.Services;
using PluginHost.Api.Shells;

namespace PluginHost.Api;

// Composition root, factored out of Program so tests can run the real pipeline on a random port
// with configuration / service overrides (configure runs after the default registrations).
public static class PluginHostApp
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(PluginHostApp).Assembly.GetName().Name,
        });
        builder.AddServiceDefaults();
        builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddMeter(PluginHostMetrics.MeterName));

        var jwtSecret = builder.RequireSecret("Jwt:SecretKey", Extensions.DevJwtSecret);
        // Guards /internal/plugins/** and authenticates calls to the Platform's internal API.
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

        // Shared with plugins (ModuleGuards uses "platform" / "auth").
        builder.Services.AddHttpClient("platform", c => c.Timeout = TimeSpan.FromSeconds(5));
        builder.Services.AddHttpClient("auth", c => c.Timeout = TimeSpan.FromSeconds(5));
        // Package download + reconcile: larger budget than the resilience defaults (10 s / attempt).
        builder.Services.AddHttpClient(PlatformPackageClient.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(2));
        builder.Services.Configure<HttpStandardResilienceOptions>($"{PlatformPackageClient.HttpClientName}-standard", o =>
        {
            o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(100);
        });

        builder.Services.AddSingleton(sp => PluginHostOptions.From(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHostEnvironment>()));
        builder.Services.AddSingleton<PluginEndpointDataSource>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<MatcherPolicy, PluginTenantMatcherPolicy>());
        builder.Services.AddSingleton<PluginRuntimeFactory>();
        builder.Services.AddSingleton<IPluginPackageSource, PlatformPackageClient>();
        builder.Services.AddSingleton<TenantShellRegistry>();
        builder.Services.AddSingleton<ShellReconciler>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ShellReconciler>());

        configure?.Invoke(builder);

        var app = builder.Build();
        app.MapDefaultEndpoints();
        app.MapGet("/healthz", () => Results.Json(new { status = "ok", service = "plugin-host" })).AllowAnonymous();

        // auth → tenant shell (pins runtime) → routing (matcher policy) → authorization → plugin DI.
        app.UseAuthentication();
        app.UseMiddleware<TenantShellMiddleware>();
        app.UseRouting();
        app.UseAuthorization();
        app.UseMiddleware<PluginServicesMiddleware>();

        InternalEndpoints.Map(app);
        AssetEndpoints.Map(app);
        ((IEndpointRouteBuilder)app).DataSources.Add(app.Services.GetRequiredService<PluginEndpointDataSource>());
        return app;
    }
}
