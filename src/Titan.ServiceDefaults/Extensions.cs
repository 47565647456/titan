using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Sentry.Serilog;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    /// <summary>
    /// Configures Serilog with console output, optional file logging (development only),
    /// and optional Sentry integration (when DSN is configured).
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="hostName">Name of the host for log file naming (e.g., "api", "identity-host").</param>
    public static TBuilder AddTitanLogging<TBuilder>(this TBuilder builder, string hostName) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddSerilog(config =>
        {
            config.WriteTo.Console();

            // File logging for development only
            if (builder.Environment.IsDevelopment())
            {
                var logPath = builder.Configuration["Logging:FilePath"] ?? $"logs/titan-{hostName}-.txt";
                config.WriteTo.File(logPath, rollingInterval: Serilog.RollingInterval.Day);
            }

            // Sentry sink for error-level events (when configured)
            var sentryDsn = builder.Configuration["Sentry:Dsn"];
            if (!string.IsNullOrEmpty(sentryDsn))
            {
                config.WriteTo.Sentry(o =>
                {
                    o.Dsn = sentryDsn;
                    o.Environment = builder.Configuration["Sentry:Environment"] ?? builder.Environment.EnvironmentName;
                    
                    if (double.TryParse(builder.Configuration["Sentry:TracesSampleRate"], out var rate))
                    {
                        o.TracesSampleRate = rate;
                    }
                    
                    o.Debug = builder.Configuration.GetValue<bool>("Sentry:Debug");
                    o.MinimumBreadcrumbLevel = Serilog.Events.LogEventLevel.Information;
                    o.MinimumEventLevel = Serilog.Events.LogEventLevel.Error;
                });
            }
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
                    .AddRuntimeInstrumentation()
                    .AddMeter("Microsoft.Orleans")
                    .AddMeter("Titan.RateLimiting");
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddSource("Microsoft.Orleans.Runtime")
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
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

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var healthChecks = builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        // Add Health Checks for dependencies if configured
        var titanConn = builder.Configuration.GetConnectionString("titan");
        if (!string.IsNullOrEmpty(titanConn))
        {
            healthChecks.AddNpgSql(titanConn, name: "titan-db", tags: ["ready"]);
        }

        var redisConn = builder.Configuration.GetConnectionString("orleans-clustering");
        if (!string.IsNullOrEmpty(redisConn))
        {
            healthChecks.AddRedis(redisConn, name: "orleans-redis", tags: ["ready"]);
        }

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Adding health checks endpoints to applications in non-development environments has security implications.
        // See https://aka.ms/dotnet/aspire/healthchecks for details before enabling these endpoints in non-development environments.
        
        // All health checks must pass for app to be considered ready to accept traffic after starting
        // Returns detailed JSON with per-check status for dashboard consumption
        app.MapHealthChecks(HealthEndpointPath, new HealthCheckOptions
        {
            ResponseWriter = WriteDetailedHealthResponseAsync
        });

        // Only health checks tagged with the "live" tag must pass for app to be considered alive
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }

    /// <summary>
    /// Writes a detailed JSON response for health checks, reporting each check's status individually.
    /// </summary>
    private static async Task WriteDetailedHealthResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.ToString()
            })
        };

        await context.Response.WriteAsJsonAsync(response);
    }
}
