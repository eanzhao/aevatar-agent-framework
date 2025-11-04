using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace Demo.Api.Extensions;

/// <summary>
/// Aspire 可观察性配置扩展
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// 配置 Aevatar Agents 的可观察性
    /// </summary>
    public static IHostApplicationBuilder AddAevatarObservability(this IHostApplicationBuilder builder)
    {
        // 配置 OpenTelemetry
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: "aevatar-agents-api"))
            .WithMetrics(metrics =>
            {
                metrics
                    // 添加 ASP.NET Core 指标
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    
                    // 添加 Aevatar Agents 自定义指标
                    .AddMeter("Aevatar.Agents")
                    
                    // 配置 Prometheus 导出（可选）
                    .AddPrometheusExporter()
                    
                    // 添加 OTLP 导出器（Aspire Dashboard）
                    .AddOtlpExporter();
            })
            .WithTracing(tracing =>
            {
                tracing
                    // 添加 ASP.NET Core 跟踪
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    
                    // 添加自定义跟踪源
                    .AddSource("Aevatar.Agents")
                    
                    // 添加 OTLP 导出器（Aspire Dashboard）
                    .AddOtlpExporter();
            });

        // 配置日志
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.ParseStateValues = true;
            
            // 添加 OTLP 导出器（Aspire Dashboard）
            options.AddOtlpExporter();
        });

        return builder;
    }

    /// <summary>
    /// 使用 Prometheus 端点
    /// </summary>
    public static WebApplication UsePrometheusMetrics(this WebApplication app)
    {
        // 暴露 Prometheus metrics 端点
        app.MapPrometheusScrapingEndpoint();
        
        return app;
    }
}

/// <summary>
/// 自定义指标增强
/// </summary>
public class AevatarMetricsEnricher
{
    /// <summary>
    /// 添加自定义维度到所有指标
    /// </summary>
    public static void EnrichWithCommonTags(IDictionary<string, object?> tags)
    {
        tags["service.version"] = "1.0.0";
        tags["deployment.environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        tags["host.name"] = Environment.MachineName;
    }
}

/// <summary>
/// Aspire Dashboard 集成说明
/// </summary>
public static class AspireDashboardIntegration
{
    public static void ConfigureInfo()
    {
        Console.WriteLine(@"
=== Aspire Dashboard 集成 ===

1. **安装 Aspire Workload**
   ```bash
   dotnet workload update
   dotnet workload install aspire
   ```

2. **添加 Aspire 包**
   ```xml
   <PackageReference Include=""Aspire.Hosting.AppHost"" Version=""9.0.0"" />
   <PackageReference Include=""Aspire.Hosting"" Version=""9.0.0"" />
   ```

3. **配置 AppHost (Demo.AppHost)**
   ```csharp
   var builder = DistributedApplication.CreateBuilder(args);

   // 添加 Aspire Dashboard
   var dashboard = builder.AddDashboard(""dashboard"");

   // 添加 API 项目
   var api = builder.AddProject<Projects.Demo_Api>(""api"")
       .WithReference(dashboard);

   builder.Build().Run();
   ```

4. **启动 Aspire Dashboard**
   ```bash
   dotnet run --project examples/Demo.AppHost
   ```

5. **访问 Dashboard**
   - 打开浏览器访问: http://localhost:18888
   - 自动显示所有服务的 Metrics, Traces, Logs

6. **查看 Agent Metrics**
   在 Dashboard 的 Metrics 页面，你会看到：
   
   **事件指标**:
   - aevatar.agents.events.published (发布的事件数)
   - aevatar.agents.events.handled (处理的事件数)
   - aevatar.agents.events.dropped (丢弃的事件数)
   
   **延迟指标**:
   - aevatar.agents.event.handling.duration (事件处理延迟)
   - aevatar.agents.event.publish.duration (事件发布延迟)
   
   **系统指标**:
   - aevatar.agents.active.count (活跃 Actor 数量)
   - aevatar.agents.exceptions (异常计数)

7. **自定义 Dashboard**
   可以使用 Grafana 连接 Prometheus 端点：
   - Prometheus endpoint: http://localhost:9090/metrics
   - 导入自定义 Dashboard JSON

8. **结构化日志**
   在 Logs 页面，可以按以下字段过滤：
   - AgentId
   - EventId
   - EventType
   - Operation
   - CorrelationId

9. **分布式追踪**
   在 Traces 页面，可以看到：
   - 完整的事件传播链
   - 各个 Agent 的处理时间
   - 异常和错误详情

10. **实时监控**
    Dashboard 支持：
    - 实时指标更新
    - 历史数据查询
    - 告警配置
    - 导出报告
");
    }
}
