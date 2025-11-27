using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Aevatar.Agents.Core.Telemetry;

// ============================================================
//  OpenTelemetry Service Extensions (P3-6)
//  Easy configuration for distributed tracing and metrics
// ============================================================

public static class TelemetryServiceExtensions
{
    /// <summary>
    /// Add Aevatar OpenTelemetry instrumentation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceName">Name of the service for resource identification.</param>
    /// <param name="serviceVersion">Version of the service.</param>
    /// <param name="configureTracing">Optional tracing configuration.</param>
    /// <param name="configureMetrics">Optional metrics configuration.</param>
    public static IServiceCollection AddAevatarTelemetry(
        this IServiceCollection services,
        string serviceName = "aevatar-agents",
        string? serviceVersion = null,
        Action<TracerProviderBuilder>? configureTracing = null,
        Action<MeterProviderBuilder>? configureMetrics = null)
    {
        var version = serviceVersion ?? typeof(TelemetryServiceExtensions).Assembly.GetName().Version?.ToString() ?? "1.0.0";

        // Configure OpenTelemetry Tracing
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: version,
                    serviceInstanceId: Environment.MachineName))
            .WithTracing(builder =>
            {
                // Add Aevatar activity source
                builder.AddSource(AevatarActivitySource.SourceName);

                // Add HTTP client instrumentation
                builder.AddHttpClientInstrumentation();

                // Allow custom configuration
                configureTracing?.Invoke(builder);
            })
            .WithMetrics(builder =>
            {
                // Add Aevatar meter
                builder.AddMeter(AevatarMetrics.MeterName);

                // Add runtime metrics
                builder.AddRuntimeInstrumentation();

                // Add HTTP client metrics
                builder.AddHttpClientInstrumentation();

                // Allow custom configuration
                configureMetrics?.Invoke(builder);
            });

        return services;
    }

    /// <summary>
    /// Add console exporter for development.
    /// </summary>
    public static IServiceCollection AddAevatarTelemetryConsoleExporter(
        this IServiceCollection services,
        string serviceName = "aevatar-agents")
    {
        return services.AddAevatarTelemetry(
            serviceName,
            configureTracing: builder => builder.AddConsoleExporter(),
            configureMetrics: builder => builder.AddConsoleExporter());
    }

    /// <summary>
    /// Add OTLP exporter for production.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceName">Name of the service.</param>
    /// <param name="otlpEndpoint">OTLP collector endpoint (default: http://localhost:4317).</param>
    public static IServiceCollection AddAevatarTelemetryOtlpExporter(
        this IServiceCollection services,
        string serviceName = "aevatar-agents",
        string? otlpEndpoint = null)
    {
        var endpoint = otlpEndpoint ?? "http://localhost:4317";

        return services.AddAevatarTelemetry(
            serviceName,
            configureTracing: builder =>
            {
                builder.AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(endpoint);
                });
            },
            configureMetrics: builder =>
            {
                builder.AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(endpoint);
                });
            });
    }
}

