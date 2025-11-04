namespace Demo.Api.Extensions;

/// <summary>
/// Aspire ServiceDefaults 简化版
/// </summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// 添加 Aspire 默认服务配置
    /// </summary>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        // 配置 OTLP 端点（如果有 Aspire Dashboard 运行）
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] 
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        
        if (!string.IsNullOrEmpty(otlpEndpoint))
        {
            builder.Services.Configure<OpenTelemetry.Exporter.OtlpExporterOptions>(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
            });
        }

        // 添加健康检查
        builder.Services.AddHealthChecks();

        return builder;
    }

    /// <summary>
    /// 映射默认端点
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // 健康检查端点
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive");
        
        return app;
    }
}
