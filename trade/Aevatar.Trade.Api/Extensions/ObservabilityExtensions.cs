using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Aevatar.Trade.Api.Extensions;

/// <summary>
/// 可观测性扩展
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// 添加 Aevatar 可观测性
    /// </summary>
    public static IHostApplicationBuilder AddAevatarObservability(this IHostApplicationBuilder builder)
    {
        var serviceName = "Aevatar.Trade";
        var serviceVersion = "1.0.0";

        // OpenTelemetry
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: serviceVersion))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource("Aevatar.Agents.*")
                .AddSource("Aevatar.Trade.*"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Aevatar.Agents")
                .AddMeter("Aevatar.Trade")
                .AddPrometheusExporter());

        return builder;
    }

    /// <summary>
    /// 使用 Prometheus Metrics 端点
    /// </summary>
    public static WebApplication UsePrometheusMetrics(this WebApplication app)
    {
        app.UseOpenTelemetryPrometheusScrapingEndpoint();
        return app;
    }
}
