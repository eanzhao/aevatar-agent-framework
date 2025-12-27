namespace Aevatar.Trade.Api.Extensions;

/// <summary>
/// Aspire ServiceDefaults 扩展
/// </summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// 添加 Aspire 默认服务配置
    /// </summary>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        // OTLP 端点配置
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        if (!string.IsNullOrEmpty(otlpEndpoint))
        {
            builder.Services.Configure<OpenTelemetry.Exporter.OtlpExporterOptions>(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
            });
        }

        // 健康检查
        builder.Services.AddHealthChecks();

        return builder;
    }

    /// <summary>
    /// 映射默认端点
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive");

        return app;
    }
}
