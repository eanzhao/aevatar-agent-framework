using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;
using Demo.Agents;

namespace Demo.Agents;

/// <summary>
/// 演示可观察性功能的 Agent
/// 会自动记录指标和使用结构化日志
/// </summary>
public class ObservableAgent : GAgentBase<SimpleAgentState>
{
    public ObservableAgent(Guid id)
        : base(id)
    {
        // Logger 将被自动注入
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Observable Agent with built-in metrics and logging");
    }

    /// <summary>
    /// 处理天气更新事件
    /// </summary>
    [EventHandler]
    public async Task HandleWeatherUpdate(WeatherUpdateEvent evt)
    {
        // 日志已经包含了结构化的上下文信息（AgentId, EventId, EventType等）
        Logger.LogInformation("Processing weather update for {Location}", evt.Location);
        
        State.Name = $"Weather in {evt.Location}";
        State.Counter++;
        State.IsActive = true;
        
        // 模拟一些处理时间
        await Task.Delay(Random.Shared.Next(10, 50));
        
        // 发布响应事件（会自动记录发布指标）
        var response = new BroadcastMessage
        {
            Id = Guid.NewGuid().ToString(),
            Topic = "weather.processed",
            Content = $"Temperature in {evt.Location}: {evt.Temperature}°C",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        
        await PublishAsync(response, EventDirection.Up);
        
        Logger.LogInformation("Weather update processed successfully");
    }

    /// <summary>
    /// 处理广播消息
    /// </summary>
    [EventHandler]
    public Task HandleBroadcast(BroadcastMessage msg)
    {
        Logger.LogDebug("Received broadcast on topic {Topic}: {Content}", 
            msg.Topic, msg.Content);
        
        State.Items.Add($"{msg.Topic}: {msg.Content}");
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// 处理所有事件（演示 AllEventHandler）
    /// </summary>
    [AllEventHandler]
    public Task HandleAllEvents(EventEnvelope envelope)
    {
        // 这个处理器会记录所有经过的事件
        var eventType = envelope.Payload?.TypeUrl?.Split('/').LastOrDefault() ?? "Unknown";
        
        Logger.LogTrace("Event flow: {EventId} of type {EventType} from {PublisherId}", 
            envelope.Id, eventType, envelope.PublisherId);
        
        State.Attributes[$"last_event_{eventType}"] = envelope.Id;
        
        return Task.CompletedTask;
    }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        Logger.LogInformation("ObservableAgent {Id} activated", Id);
        
        // 初始化状态
        State.Name = "Observable Agent";
        State.IsActive = true;
        
        await base.OnActivateAsync(ct);
    }

    public override async Task OnDeactivateAsync(CancellationToken ct)
    {
        Logger.LogInformation("ObservableAgent {Id} deactivated with {Count} events processed", 
            Id, State.Counter);
        
        State.IsActive = false;
        
        await base.OnDeactivateAsync(ct);
    }
}

/// <summary>
/// 可观察性功能说明
/// </summary>
public static class ObservabilityFeatures
{
    public static void Describe()
    {
        Console.WriteLine(@"
=== 可观察性功能 (Observability Features) ===

框架现已内置以下可观察性功能：

1. **自动指标收集 (Automatic Metrics Collection)**
   - 事件发布计数和延迟
   - 事件处理计数和延迟
   - 异常计数（按类型和操作分类）
   - 活跃 Actor 数量
   - 事件丢弃计数

2. **结构化日志 (Structured Logging)**
   - 自动添加上下文信息（AgentId, EventId, EventType等）
   - 支持日志作用域，方便追踪操作链
   - 事件处理和发布自动包含相关元数据

3. **性能监控 (Performance Monitoring)**
   - 使用 System.Diagnostics.Metrics API
   - 兼容 OpenTelemetry
   - 支持 Prometheus, Application Insights 等监控系统

4. **使用方式**
   所有功能都是自动的，无需额外代码：
   - 发布事件时自动记录指标
   - 处理事件时自动创建日志作用域
   - Actor 生命周期自动更新计数器
   - 异常自动记录并分类

5. **集成点**
   - GAgentBase: 事件发布和处理
   - GAgentActorBase: Actor 级别的事件路由
   - LocalGAgentActor/ProtoActorGAgentActor: 活跃 Actor 计数
   
6. **导出指标**
   可以使用 OpenTelemetry 导出指标：
   ```csharp
   services.AddOpenTelemetry()
       .WithMetrics(builder => builder
           .AddMeter(""Aevatar.Agents"")
           .AddPrometheusExporter());
   ```

这些功能让开发者能够：
- 实时监控系统性能
- 快速定位问题
- 分析事件流模式
- 优化系统性能
");
    }
}
