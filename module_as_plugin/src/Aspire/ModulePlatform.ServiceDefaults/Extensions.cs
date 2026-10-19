// Shared Aspire defaults: resilience + service discovery for HttpClients,
// OpenTelemetry (OTLP exporter activates only when an endpoint is configured,
// e.g. under the Aspire dashboard), and /health + /alive endpoints.
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace ModulePlatform.ServiceDefaults;

public static class Extensions
{
    public const string DevJwtSecret = "dev-only-secret-key-min-32-chars!!";
    public const string DevInternalKey = "dev-internal-key";

    // Fail-closed secret resolution. Development falls back to the well-known
    // dev value (written back into configuration so later reads agree);
    // every other environment aborts startup when the secret is missing,
    // shorter than minLength, or still the dev default.
    public static string RequireSecret(this IHostApplicationBuilder builder, string key, string devDefault, int minLength = 32)
    {
        var value = builder.Configuration[key];
        if (builder.Environment.IsDevelopment())
        {
            if (string.IsNullOrWhiteSpace(value)) builder.Configuration[key] = value = devDefault;
            return value;
        }
        if (string.IsNullOrWhiteSpace(value) || value == devDefault || value.Length < minLength)
            throw new InvalidOperationException(
                $"Required secret '{key}' is missing, shorter than {minLength} characters, or the development default. " +
                $"Set it via environment variable '{key.Replace(":", "__")}' or a secret store.");
        return value;
    }

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
            });
        builder.AddOpenTelemetryExporters();
        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks("/health");
            app.MapHealthChecks("/alive", new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live"),
            });
        }
        return app;
    }
}
