# Aspire 集成指南

## 🎯 好消息

**框架已经完全兼容 Aspire！**

我们实现的 `AgentMetrics` 使用的是标准的 `System.Diagnostics.Metrics`，这正是 Aspire 使用的指标系统。无需任何修改，Metrics 会自动被 Aspire Dashboard 收集和显示。

## ✅ 自动兼容的 Metrics

当前框架已提供的 Metrics（在 `AgentMetrics` 中）：

### 计数器（Counter）
- `aevatar.agents.events.published` - 发布的事件总数
- `aevatar.agents.events.handled` - 处理的事件总数
- `aevatar.agents.events.dropped` - 丢弃的事件总数
- `aevatar.agents.exceptions` - 异常总数

### 直方图（Histogram）
- `aevatar.agents.event.handling.duration` - 事件处理延迟（ms）
- `aevatar.agents.event.publish.duration` - 事件发布延迟（ms）

### 可观测量（Gauge）
- `aevatar.agents.active.count` - 活跃 Actor 数量
- `aevatar.agents.queue.length` - 当前队列长度

**这些 Metrics 会自动出现在 Aspire Dashboard 中！** 🎊

## 🚀 Aspire 集成步骤

### 1. 在 AppHost 中添加 Aspire

```csharp
// Demo.AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

// 添加 Agent API 项目
var apiService = builder.AddProject<Projects.Demo_Api>("agent-api")
    .WithReplicas(1);

builder.Build().Run();
```

### 2. 配置 Demo.Api 支持 Aspire

在 `Demo.Api.csproj` 中添加：

```xml
<ItemGroup>
  <PackageReference Include="Aspire.Hosting.AppHost" Version="9.0.0" />
</ItemGroup>
```

### 3. 启动 Aspire Dashboard

```bash
cd examples/Demo.AppHost
dotnet run
```

访问 Aspire Dashboard（通常是 http://localhost:15888）

## 📊 Aspire Dashboard 中的 Agent Metrics

### Metrics 视图

在 Aspire Dashboard 中，你会看到：

**Counters（计数器）**：
```
📊 aevatar.agents.events.published
   ├── Total: 1,234
   ├── Rate: 12/s
   └── Tags: event.type, agent.id

📊 aevatar.agents.events.handled
   ├── Total: 1,230
   ├── Rate: 12/s
   └── Tags: event.type, agent.id

📊 aevatar.agents.exceptions
   ├── Total: 5
   └── Tags: exception.type, agent.id, operation
```

**Histograms（延迟分布）**：
```
📈 aevatar.agents.event.handling.duration
   ├── P50: 2.5ms
   ├── P95: 8.2ms
   ├── P99: 15.1ms
   └── Tags: event.type, agent.id
```

**Gauges（当前值）**：
```
📉 aevatar.agents.active.count
   └── Current: 42 actors

📉 aevatar.agents.queue.length
   └── Current: 128 events
```

### Traces 视图（分布式追踪）

虽然我们还没实现 ActivitySource，但 Aspire 会自动收集：
- HTTP 请求追踪
- 数据库调用追踪
- 服务间调用追踪

**将来实现 ActivitySource 后，Agent 事件传播也会出现在追踪中！**

### Logs 视图

Aspire 自动收集所有日志：
- Agent 激活/停用日志
- 事件处理日志
- 异常日志

使用我们的 `LoggingScope`，日志会带有结构化数据：
```
[Agent: fa3fd391-4eb7-470d-8ed8-6a595ebf2589]
[Operation: HandleEvent]
[EventId: e64bc130-aae5-442c-af63-156891cf3ef0]
[CorrelationId: 123e4567-e89b-12d3-a456-426614174000]
Agent handling event from stream
```

## 💡 在代码中使用 Metrics

### 记录事件发布

```csharp
using Aevatar.Agents.Core.Observability;

// 在 LocalGAgentActor 的 PublishEventAsync 中
AgentMetrics.RecordEventPublished(evt.GetType().Name, Id.ToString());
```

### 记录事件处理

```csharp
var startTime = DateTime.UtcNow;

// 处理事件
await _agent.HandleEventAsync(envelope);

// 记录延迟
var latency = (DateTime.UtcNow - startTime).TotalMilliseconds;
AgentMetrics.RecordEventHandled(eventType, Id.ToString(), latency);
```

### 更新活跃 Actor 数量

```csharp
// 在 ActorManager 中
public async Task<IGAgentActor> CreateAndRegisterAsync<TAgent, TState>(Guid id)
{
    var actor = await _factory.CreateAgentAsync<TAgent, TState>(id);
    _actors[id] = actor;
    
    // 更新 Metrics
    AgentMetrics.UpdateActiveActorCount(_actors.Count);
    
    return actor;
}
```

## 🔧 集成建议

### 短期（当前）
- ✅ **无需修改** - 当前 Metrics 已兼容
- ✅ 添加 Aspire AppHost 配置
- ✅ 启动即可在 Dashboard 看到 Metrics

### 中期（优化）
- 在关键路径添加 Metrics 记录
  - LocalGAgentActor.PublishEventAsync
  - HandleEventFromStreamAsync
  - ActorManager 的创建/销毁
  
### 长期（可选）
- ActivitySource 集成（分布式追踪）
  - 事件传播的完整链路追踪
  - 跨 Agent 的调用追踪

## 📝 示例：Aspire AppHost 配置

```csharp
// Demo.AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

// 添加 API 服务
var api = builder.AddProject<Projects.Demo_Api>("agent-api")
    .WithEnvironment("AgentRuntime__RuntimeType", "Local")
    .WithReplicas(1);

// 如果使用 Orleans
var orleans = builder.AddOrleans("agent-cluster")
    .WithClustering();

api.WithReference(orleans);

// 运行
builder.Build().Run();
```

启动后访问：
- Dashboard: http://localhost:15888
- Metrics: http://localhost:15888/metrics
- Traces: http://localhost:15888/traces
- Logs: http://localhost:15888/logs

## 🎯 Aspire 的价值

使用 Aspire 后，你可以：

1. **实时监控**
   - 查看当前有多少个 Agent Actor
   - 查看事件处理的延迟分布
   - 查看异常发生率

2. **调试**
   - 追踪事件在 Agent 之间的传播路径
   - 查看每个 Agent 的日志
   - 分析性能瓶颈

3. **告警**
   - 事件队列过长告警
   - 异常率过高告警
   - 响应时间过慢告警

## ✅ 结论

**不需要在底层添加特殊的 Aspire Metrics！**

当前的 `AgentMetrics`（基于标准 `System.Diagnostics.Metrics`）已经完全兼容 Aspire。

**只需要**：
1. 在 AppHost 中配置 Aspire
2. 在关键路径调用 `AgentMetrics.RecordXxx()`
3. 启动后在 Dashboard 查看

**框架设计天然支持 Aspire，无需额外工作！** 🎉

---

*标准化的 Metrics 让框架自然融入 .NET 生态，Aspire 的集成水到渠成。*

