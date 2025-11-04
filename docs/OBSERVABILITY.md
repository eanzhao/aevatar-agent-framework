# 可观察性指南 (Observability Guide)

## 概览

Aevatar Agent Framework 现已内置完整的可观察性功能，包括指标收集、结构化日志和分布式追踪。所有功能都已自动集成，无需额外配置。

## 在 Aspire Dashboard 中查看指标

### 1. 安装 Aspire

```bash
# 安装 .NET Aspire workload
dotnet workload update
dotnet workload install aspire
```

### 2. 启动 Aspire Dashboard

```bash
# 进入项目目录
cd aevatar-agent-framework

# 启动 AppHost（包含 Dashboard）
dotnet run --project examples/Demo.AppHost
```

### 3. 访问 Dashboard

打开浏览器访问: **http://localhost:18888**

### 4. 查看指标

在 Dashboard 的 **Metrics** 标签页，你会看到：

#### 事件指标
- `aevatar.agents.events.published` - 发布的事件总数
- `aevatar.agents.events.handled` - 处理的事件总数  
- `aevatar.agents.events.dropped` - 丢弃的事件总数

#### 延迟指标
- `aevatar.agents.event.handling.duration` - 事件处理延迟（毫秒）
- `aevatar.agents.event.publish.duration` - 事件发布延迟（毫秒）

#### 系统指标
- `aevatar.agents.active.count` - 活跃 Actor 数量
- `aevatar.agents.exceptions` - 异常计数

### 5. 查看结构化日志

在 **Logs** 标签页，可以按以下字段过滤：
- `AgentId` - Agent ID
- `EventId` - 事件 ID
- `EventType` - 事件类型
- `Operation` - 操作名称
- `CorrelationId` - 关联 ID

### 6. 查看分布式追踪

在 **Traces** 标签页，可以看到：
- 完整的事件传播链
- 各个 Agent 的处理时间
- 跨服务调用关系

## Prometheus 指标

除了 Aspire Dashboard，你还可以通过 Prometheus 端点获取原始指标：

```bash
curl http://localhost:7001/metrics
```

### 指标格式示例

```prometheus
# HELP aevatar_agents_events_published Total number of events published
# TYPE aevatar_agents_events_published counter
aevatar_agents_events_published{event_type="WeatherUpdateEvent",agent_id="123"} 42

# HELP aevatar_agents_event_handling_duration Event handling duration in milliseconds
# TYPE aevatar_agents_event_handling_duration histogram
aevatar_agents_event_handling_duration_bucket{event_type="WeatherUpdateEvent",agent_id="123",le="10"} 35
aevatar_agents_event_handling_duration_bucket{event_type="WeatherUpdateEvent",agent_id="123",le="50"} 40
```

## 集成到生产环境

### 使用 Grafana

1. 添加 Prometheus 数据源：
```
URL: http://localhost:7001/metrics
```

2. 导入 Dashboard 模板（在 `/docs/grafana-dashboard.json`）

### 使用 Application Insights

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("Aevatar.Agents")
        .AddAzureMonitorMetricExporter(options =>
        {
            options.ConnectionString = "<your-connection-string>";
        }));
```

### 使用 Datadog

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("Aevatar.Agents")
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("https://ingest.datadoghq.com:4317");
            options.Headers = $"DD-API-KEY={apiKey}";
        }));
```

## 自定义指标

### 添加自定义计数器

```csharp
AgentMetrics.RecordEventPublished("CustomEvent", agentId);
```

### 添加自定义延迟

```csharp
var stopwatch = Stopwatch.StartNew();
// ... 你的操作 ...
stopwatch.Stop();
AgentMetrics.EventHandlingLatency.Record(
    stopwatch.ElapsedMilliseconds,
    new KeyValuePair<string, object?>("operation", "custom"));
```

### 记录异常

```csharp
try
{
    // ... 你的代码 ...
}
catch (Exception ex)
{
    AgentMetrics.RecordException(ex.GetType().Name, agentId, "CustomOperation");
    throw;
}
```

## 性能影响

- 指标收集的开销极小（< 1% CPU）
- 日志作用域使用 `IDisposable` 模式，自动清理
- 所有指标都是异步收集，不会阻塞业务逻辑

## 故障排查

### Dashboard 无法访问

1. 检查端口 18888 是否被占用
2. 确认 Docker 已安装并运行
3. 查看 AppHost 日志是否有错误

### 看不到指标

1. 确认 API 已启动并运行
2. 触发一些 Agent 事件来生成指标
3. 检查 OTLP 端点配置是否正确

### 日志没有结构化信息

1. 确认使用了 `LoggingScope.CreateEventHandlingScope`
2. 检查日志级别是否设置正确（至少 Information）

## 示例代码

查看 `examples/Demo.Agents/ObservableAgent.cs` 了解完整的可观察性功能示例。

## 相关文档

- [OpenTelemetry .NET](https://github.com/open-telemetry/opentelemetry-dotnet)
- [Aspire Dashboard](https://learn.microsoft.com/aspire/fundamentals/dashboard)
- [Prometheus](https://prometheus.io/)
- [Grafana](https://grafana.com/)
