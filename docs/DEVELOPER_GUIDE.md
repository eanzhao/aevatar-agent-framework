# Aevatar Agent Framework - 开发者指南

## 🛠️ 概述

本文档涵盖框架的实现细节、扩展点和高级特性，适合需要深入理解框架内部机制的开发者。

---

## 🏭 IGAgentActorManager - 核心管理器

### 职责

`IGAgentActorManager` 是框架的核心组件，负责：

1. **全局Actor注册表**: 跟踪所有活跃的Actor
2. **生命周期管理**: 创建、激活、停用Actor
3. **批量操作**: 支持批量创建和停用
4. **类型查询**: 按类型查找Actor
5. **监控统计**: 健康状态和统计信息

### 接口定义

```csharp
public interface IGAgentActorManager
{
    // 生命周期
    Task<IGAgentActor> CreateAndRegisterAsync<TAgent>(Guid id, CancellationToken ct = default)
        where TAgent : IGAgent;
    Task<IReadOnlyList<IGAgentActor>> CreateBatchAsync<TAgent>(IEnumerable<Guid> ids, CancellationToken ct = default)
        where TAgent : IGAgent;
    Task DeactivateAndUnregisterAsync(Guid id, CancellationToken ct = default);
    Task DeactivateBatchAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task DeactivateAllAsync(CancellationToken ct = default);

    // 查询
    Task<IGAgentActor?> GetActorAsync(Guid id);
    Task<IReadOnlyList<IGAgentActor>> GetAllActorsAsync();
    Task<IReadOnlyList<IGAgentActor>> GetActorsByTypeAsync<TAgent>() where TAgent : IGAgent;
    Task<bool> ExistsAsync(Guid id);
    Task<int> GetCountAsync();

    // 监控
    Task<ActorHealthStatus> GetHealthStatusAsync(Guid id);
    Task<ActorManagerStatistics> GetStatisticsAsync();
}
```

### 三种实现

| Manager | 存储机制 | 特点 |
|---------|---------|------|
| `LocalGAgentActorManager` | ConcurrentDictionary | 进程内，最快 |
| `OrleansGAgentActorManager` | GrainFactory | 分布式，位置透明 |
| `ProtoActorGAgentActorManager` | ActorSystem.Root | 轻量级，高性能 |

---

## 🏭 IGAgentActorFactory - Actor工厂

### 职责

负责创建特定Runtime的Actor实例。

```csharp
public interface IGAgentActorFactory
{
    Task<IGAgentActor> CreateGAgentActorAsync<TAgent>(Guid id, CancellationToken ct = default)
        where TAgent : IGAgent;
    string GetRuntimeName();
}
```

### 运行时特定的工厂

#### LocalGAgentActorFactory
```csharp
public class LocalGAgentActorFactory : IGAgentActorFactory
{
    private readonly IServiceProvider _serviceProvider;

    public async Task<IGAgentActor> CreateGAgentActorAsync<TAgent>(Guid id, CancellationToken ct = default)
        where TAgent : IGAgent
    {
        // 创建 Local Actor (使用 Channel)
        var agent = ActivatorUtilities.CreateInstance<TAgent>(_serviceProvider, id);
        var actor = new LocalGAgentActor(agent, _serviceProvider);
        await actor.ActivateAsync(ct);
        return actor;
    }
}
```

#### OrleansGAgentActorFactory
```csharp
public class OrleansGAgentActorFactory : IGAgentActorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<OrleansGAgentActorFactory> _logger;

    public async Task<IGAgentActor> CreateActorForAgentAsync(IGAgent agent, Guid id, CancellationToken ct = default)
    {
        // 注入依赖
        AgentLoggerInjector.InjectLogger(agent, _serviceProvider);
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentConfigurationInjector.InjectConfigurationStore(agent, _serviceProvider);
        AgentEventStoreInjector.InjectEventStore(agent, _serviceProvider); // 事件溯源

        // 创建 Grain 和 Actor
        var grain = _grainFactory.GetGrain<IStandardGAgentGrain>(id.ToString());
        var actor = new OrleansGAgentActor(agent, _grainFactory, _streamProvider, _logger);

        // 激活（触发事件回放）
        await actor.ActivateAsync(ct);

        return actor;
    }
}
```

** 关键设计 **:
- 统一使用 `IStandardGAgentGrain` (所有 Agent 使用相同的 Grain)
- 事件溯源通过依赖注入自动启用 (不需要配置选项)
- 事件回放在 Actor 激活时触发 (Actor 层,不是 Agent 层)

---

## 🏭 IGAgentActorFactory - Actor工厂

### 职责

负责创建特定Runtime的Actor实例。

```csharp
public interface IGAgentActorFactory
{
    Task<IGAgentActor> CreateGAgentActorAsync<TAgent>(Guid id, CancellationToken ct = default) 
        where TAgent : IGAgent;
    string GetRuntimeName();
}
```

### AutoDiscovery机制

框架支持自动发现Agent类型：

```csharp
public interface IGAgentActorFactoryProvider
{
    IGAgentActorFactory GetFactory(Type agentType);
    IGAgentActorFactory GetFactory<TAgent>() where TAgent : IGAgent;
    void RegisterFactory(Type agentType, IGAgentActorFactory factory);
}

// 使用AutoDiscoveryGAgentActorFactoryProvider
// 可以根据Agent类型自动选择合适的Factory
```

---

## 🔄 订阅管理器详解

### ISubscriptionManager

每个Runtime都实现了订阅管理器：

```csharp
public interface ISubscriptionManager
{
    Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Guid subscriberId,
        IMessageStream targetStream,
        Func<T, Task> handler,
        Func<T, bool>? filter = null,
        CancellationToken ct = default) 
        where T : IMessage;
    
    Task UnsubscribeAsync(Guid subscriptionId);
    IMessageStreamSubscription? GetSubscription(Guid subscriptionId);
    IReadOnlyList<IMessageStreamSubscription> GetActiveSubscriptions();
}
```

### 订阅恢复机制

当网络中断或Actor重启时，可以恢复订阅：

```csharp
// 1. 记录订阅信息
var subscriptionInfo = new SubscriptionInfo
{
    SubscriberId = actor.Id,
    StreamId = parentStream.Id,
    EventType = typeof(MyEvent)
};

// 2. 网络恢复后，重新订阅
var newSubscription = await manager.SubscribeAsync<MyEvent>(
    subscriptionInfo.SubscriberId,
    parentStream,
    handler
);

// 3. 使用Resume恢复
await newSubscription.ResumeAsync();
```

**实现差异**:
- **Local**: 简单重新订阅（无网络）
- **Orleans**: 利用Stream的Resume token
- **ProtoActor**: 重新建立EventStream连接

---

## 📊 可观测性（Observability）

### 内置指标

框架自动收集以下指标：

```csharp
public class AgentMetrics
{
    public long EventsProcessed { get; set; }        // 已处理事件数
    public long EventsPublished { get; set; }        // 已发布事件数
    public double AvgProcessingTimeMs { get; set; }  // 平均处理时间
    public DateTime LastActivityTime { get; set; }   // 最后活动时间
    public int ActiveSubscriptions { get; set; }     // 活跃订阅数
}
```

### 日志记录

框架使用结构化日志：

```csharp
Logger.LogInformation("Agent {AgentId} processed event {EventId} in {Duration}ms",
    Id, envelope.Id, duration);

// 自动包含的上下文：
// - AgentId
// - EventId  
// - CorrelationId
// - Runtime Type
```

### OpenTelemetry集成

```csharp
// 配置OpenTelemetry
services.AddOpenTelemetry()
    .WithTracing(builder =>
    {
        builder
            .AddSource("Aevatar.Agents.*")
            .AddAspNetCoreInstrumentation()
            .AddGrpcClientInstrumentation();
    })
    .WithMetrics(builder =>
    {
        builder
            .AddMeter("Aevatar.Agents.*")
            .AddAspNetCoreInstrumentation();
    });
```

**自动追踪**:
- Event发布和处理
- Stream订阅和取消
- Actor激活和停用
- Parent-Child关系建立

---

## 🔧 扩展点

### 1. 自定义EventStore

实现 `IEventStore` 接口支持其他存储：

```csharp
public class RedisEventStore : IEventStore
{
    public async Task AppendEventAsync(Guid agentId, IMessage @event)
    {
        var key = $"events:{agentId}";
        var data = @event.ToByteArray();
        await _redis.ListRightPushAsync(key, data);
    }

    public async Task<IReadOnlyList<IMessage>> GetEventsAsync(Guid agentId)
    {
        var key = $"events:{agentId}";
        var values = await _redis.ListRangeAsync(key);
        return values.Select(v => ParseEvent(v)).ToList();
    }
}
```

### 2. 自定义EventDeduplicator

实现 `IEventDeduplicator` 防止重复事件：

```csharp
public class RedisEventDeduplicator : IEventDeduplicator
{
    public async Task<bool> IsProcessedAsync(string eventId)
    {
        return await _redis.ExistsAsync($"processed:{eventId}");
    }

    public async Task MarkAsProcessedAsync(string eventId, TimeSpan expiration)
    {
        await _redis.SetAsync($"processed:{eventId}", "1", expiration);
    }
}
```

### 3. 自定义Stream实现

实现 `IMessageStream` 支持其他消息系统：

```csharp
public class KafkaMessageStream : IMessageStream
{
    private readonly IProducer<string, byte[]> _producer;
    private readonly string _topic;

    public async Task PublishAsync<T>(T message, CancellationToken ct = default) 
        where T : IMessage
    {
        await _producer.ProduceAsync(_topic, new Message<string, byte[]>
        {
            Key = _streamId.ToString(),
            Value = message.ToByteArray()
        }, ct);
    }

    public Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler, 
        CancellationToken ct = default) 
        where T : IMessage
    {
        // 创建Kafka Consumer...
    }
}
```

---

## 🎯 Agent生命周期钩子

### OnActivateAsync

```csharp
public override async Task OnActivateAsync(CancellationToken ct = default)
{
    await base.OnActivateAsync(ct);  // 务必先调用base
    
    // 初始化State属性
    State.AgentId = Id.ToString();
    State.CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
    
    // 加载配置
    await LoadConfigurationAsync();
    
    // 建立连接
    await ConnectToExternalSystemAsync();
}
```

### OnDeactivateAsync

```csharp
public override async Task OnDeactivateAsync(CancellationToken ct = default)
{
    // 清理资源
    await DisconnectFromExternalSystemAsync();
    
    // 保存状态
    await SaveStateAsync();
    
    await base.OnDeactivateAsync(ct);  // 务必最后调用base
}
```

---

## 🔍 高级模式

### Supervisor Pattern

```csharp
public class SupervisorAgent : GAgentBase<SupervisorState>
{
    [EventHandler]
    public async Task HandleWorkerError(WorkerErrorEvent evt)
    {
        Logger.LogWarning("Worker {WorkerId} failed: {Error}",
            evt.WorkerId, evt.ErrorMessage);

        // 重启Worker
        var manager = GetManager();  // 从DI获取
        await manager.DeactivateAndUnregisterAsync(evt.WorkerId);
        var newWorker = await manager.CreateAndRegisterAsync<WorkerAgent>(evt.WorkerId);
        await newWorker.SetParentAsync(Id);
    }
}
```

### Aggregator Pattern

```csharp
public class AggregatorAgent : GAgentBase<AggregatorState>
{
    [EventHandler]
    public async Task HandleDataPoint(DataPointEvent evt)
    {
        State.DataPoints.Add(evt);

        // 达到阈值时聚合
        if (State.DataPoints.Count >= 100)
        {
            var summary = AggregateData(State.DataPoints);
            await PublishAsync(new AggregatedDataEvent { Summary = summary });
            State.DataPoints.Clear();
        }
    }
}
```

### Saga Pattern

```csharp
public class SagaCoordinatorAgent : GAgentBase<SagaState>
{
    [EventHandler]
    public async Task HandleStepCompleted(StepCompletedEvent evt)
    {
        State.CompletedSteps.Add(evt.StepId);

        // 所有步骤完成
        if (State.CompletedSteps.Count == State.TotalSteps)
        {
            await PublishAsync(new SagaCompletedEvent { SagaId = State.SagaId });
        }
        else
        {
            // 启动下一步
            await StartNextStep();
        }
    }

    [EventHandler]
    public async Task HandleStepFailed(StepFailedEvent evt)
    {
        Logger.LogError("Step {StepId} failed, compensating...", evt.StepId);
        
        // 补偿已完成的步骤
        await CompensatePreviousSteps();
    }
}
```

---

## 📐 依赖注入高级配置

### 多Runtime共存

```csharp
// 同时注册多个Runtime
services.AddSingleton<LocalGAgentActorFactory>();
services.AddSingleton<OrleansGAgentActorFactory>();
services.AddSingleton<ProtoActorGAgentActorFactory>();

// 根据需求选择Factory
services.AddSingleton<IGAgentActorFactoryProvider>(sp =>
{
    var provider = new AutoDiscoveryGAgentActorFactoryProvider();
    
    // 某些Agent用Local
    provider.RegisterFactory(typeof(TestAgent), sp.GetRequiredService<LocalGAgentActorFactory>());
    
    // 某些Agent用Orleans
    provider.RegisterFactory(typeof(ProductionAgent), sp.GetRequiredService<OrleansGAgentActorFactory>());
    
    return provider;
});
```

### 事件去重配置

```csharp
services.AddSingleton<IEventDeduplicator>(sp =>
    new MemoryCacheEventDeduplicator(new DeduplicationOptions
    {
        EventExpiration = TimeSpan.FromMinutes(5),  // 事件ID保留5分钟
        MaxCachedEvents = 10_000,                   // 最多缓存10K个ID
        EnableAutoCleanup = true                    // 自动清理过期
    })
);
```

### 订阅管理配置

```csharp
// Local Runtime
services.AddSingleton<ISubscriptionManager>(sp =>
    new LocalSubscriptionManager(
        sp.GetRequiredService<LocalMessageStreamRegistry>(),
        sp.GetRequiredService<ILogger<LocalSubscriptionManager>>()
    )
);

// Orleans Runtime
services.AddSingleton<ISubscriptionManager>(sp =>
{
    var client = sp.GetRequiredService<IClusterClient>();
    var streamProvider = client.GetStreamProvider("DefaultStreamProvider");
    return new OrleansSubscriptionManager(
        streamProvider,
        "AevatarStreams",  // Namespace
        sp.GetRequiredService<ILogger<OrleansSubscriptionManager>>()
    );
});
```

---

## 🔧 事件处理器发现机制

### 发现规则

框架使用反射自动发现事件处理器：

1. **属性标记**: `[EventHandler]` 或 `[AllEventHandler]`
2. **命名约定**: 方法名为 `HandleAsync` 或 `HandleEventAsync`
3. **方法签名**: `public/protected Task MethodName(EventType evt)`

### 缓存机制

```csharp
// 处理器信息缓存在静态字典中
private static readonly ConcurrentDictionary<Type, MethodInfo[]> HandlerCache = new();

// 首次使用时扫描，后续直接使用缓存
// 大幅提升性能
```

### 优先级排序

```csharp
[EventHandler(Priority = 1)]  // 先执行
public async Task HandleImportant(CriticalEvent evt) { }

[EventHandler(Priority = 10)] // 后执行
public async Task HandleNormal(NormalEvent evt) { }
```

---

## 🌊 Stream注册表

### LocalMessageStreamRegistry

```csharp
public class LocalMessageStreamRegistry
{
    // Stream存储
    private readonly ConcurrentDictionary<Guid, LocalMessageStream> _streams = new();

    // 获取或创建Stream
    public LocalMessageStream GetOrCreateStream(Guid streamId)
    {
        return _streams.GetOrAdd(streamId, id => new LocalMessageStream(id));
    }

    // 移除Stream
    public bool RemoveStream(Guid streamId)
    {
        return _streams.TryRemove(streamId, out _);
    }
}
```

### OrleansMessageStreamProvider

```csharp
public class OrleansMessageStreamProvider
{
    private readonly IStreamProvider _streamProvider;
    private readonly string _namespace;

    // Orleans使用IStreamProvider来管理Stream
    public IMessageStream GetStream(Guid streamId)
    {
        var stream = _streamProvider.GetStream<byte[]>(_namespace, streamId);
        return new OrleansMessageStream(streamId, stream);
    }
}
```

---

## 📊 健康检查

### ActorHealthStatus

```csharp
public record ActorHealthStatus
{
    public Guid Id { get; init; }
    public bool IsHealthy { get; init; }
    public DateTimeOffset? LastActivityTime { get; init; }
    public string? ErrorMessage { get; init; }
}

// 使用
var health = await manager.GetHealthStatusAsync(agentId);
if (!health.IsHealthy)
{
    Logger.LogWarning("Agent {AgentId} unhealthy: {Error}",
        health.Id, health.ErrorMessage);
}
```

### 统计信息

```csharp
public record ActorManagerStatistics
{
    public int TotalActors { get; init; }
    public int ActiveActors { get; init; }
    public Dictionary<string, int> ActorsByType { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

// 使用
var stats = await manager.GetStatisticsAsync();
Console.WriteLine($"Total Actors: {stats.TotalActors}");
Console.WriteLine($"Active: {stats.ActiveActors}");
foreach (var (type, count) in stats.ActorsByType)
{
    Console.WriteLine($"  {type}: {count}");
}
```

---

## 🎭 Orleans特定功能

### Grain类型选择

```csharp
// 配置Orleans Factory选项
services.Configure<OrleansGAgentActorFactoryOptions>(options =>
{
    options.UseEventSourcing = false;  // 标准Grain
    options.DefaultGrainType = GrainType.Standard;
});

// 或为每个Agent指定
[GrainType(GrainType.EventSourced)]
public class MyAgent : GAgentBase<MyState> { }
```

### 持久化Provider

```csharp
// Orleans Silo配置
siloBuilder.AddMemoryGrainStorage("PubSubStore");
siloBuilder.AddMemoryGrainStorage("StateStore");

// 或使用MongoDB
siloBuilder.AddMongoDBGrainStorage("StateStore", options =>
{
    options.ConnectionString = "mongodb://localhost:27017";
    options.DatabaseName = "aevatar_orleans";
});
```

---

## 🚀 ProtoActor特定功能

### Actor Props配置

```csharp
// ProtoActorGAgentActorFactory内部使用
var props = Props.FromProducer(() => new AgentActor(agent, logger))
    .WithMailbox(() => UnboundedMailbox.Create())
    .WithSupervisor(new OneForOneStrategy(...));

var pid = context.Spawn(props);
```

### Cluster支持

```csharp
// 配置Proto.Cluster
var system = new ActorSystem()
    .WithRemote(GrpcNetRemoteConfig.BindToLocalhost())
    .WithCluster(ClusterConfig
        .Setup("aevatar-cluster", 
               new ConsulProvider(new ConsulProviderConfig()),
               new PartitionIdentityLookup())
    );

await system.Cluster().StartMemberAsync();
```

---

## 🔍 调试技巧

### 1. 追踪事件流

```csharp
[AllEventHandler]
public async Task TraceAllEvents(EventEnvelope envelope)
{
    var eventType = envelope.EventType;
    Logger.LogDebug("[TRACE] {Sender} → {Receiver}: {EventType}",
        envelope.SenderId, envelope.ReceiverId, eventType);
    
    // 可以记录到分布式追踪系统
    Activity.Current?.AddTag("event.type", eventType);
    Activity.Current?.AddTag("event.id", envelope.Id);
}
```

### 2. Stream诊断

```csharp
// 检查订阅状态
var subscriptionManager = services.GetRequiredService<ISubscriptionManager>();
var subscriptions = subscriptionManager.GetActiveSubscriptions();

foreach (var sub in subscriptions)
{
    Logger.LogInformation("Subscription {SubId}: Stream={StreamId}, Active={Active}",
        sub.SubscriptionId, sub.StreamId, sub.IsActive);
}
```

### 3. Actor诊断

```csharp
// 获取所有Actor
var allActors = await manager.GetAllActorsAsync();
Logger.LogInformation("Total Actors: {Count}", allActors.Count);

// 按类型分组
var grouped = allActors.GroupBy(a => a.GetAgent().GetType().Name);
foreach (var group in grouped)
{
    Logger.LogInformation("  {Type}: {Count}", group.Key, group.Count());
}
```

---

## ⚠️ 常见问题

### 1. Actor激活失败

**问题**: Actor必须有无参构造函数

```csharp
// ❌ 错误
public class MyAgent : GAgentBase<MyState>
{
    public MyAgent(string name) : base() { }  // 有参数！
}

// ✅ 正确
public class MyAgent : GAgentBase<MyState>
{
    public MyAgent() : base() { }
    
    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        // 在这里初始化
        State.Name = $"Agent_{Id.ToString("N")[..8]}";
    }
}
```

### 2. State修改错误

**问题**: State是只读属性，不能赋值

```csharp
// ❌ 错误
public override async Task OnActivateAsync(CancellationToken ct = default)
{
    State = new MyState { Name = "Test" };  // State是只读的！
}

// ✅ 正确
public override async Task OnActivateAsync(CancellationToken ct = default)
{
    await base.OnActivateAsync(ct);
    State.Name = "Test";  // 修改State的属性
    State.Count = 0;
}
```

### 3. 订阅内存泄漏

**问题**: 创建订阅但never dispose

```csharp
// ❌ 错误
public async Task SubscribeToMany()
{
    for (int i = 0; i < 1000; i++)
    {
        await stream.SubscribeAsync<MyEvent>(handler);  // 泄漏！
    }
}

// ✅ 正确
public async Task SubscribeToMany()
{
    var subscriptions = new List<IMessageStreamSubscription>();
    for (int i = 0; i < 1000; i++)
    {
        var sub = await stream.SubscribeAsync<MyEvent>(handler);
        subscriptions.Add(sub);
    }
    
    // 记得清理
    _cleanup = async () =>
    {
        foreach (var sub in subscriptions)
        {
            await sub.DisposeAsync();
        }
    };
}
```

---

## 📚 参考

### 核心文档
- `CORE_CONCEPTS.md` - Stream、序列化、事件传播
- `EVENTSOURCING.md` - EventSourcing详细指南
- `AI_INTEGRATION.md` - AI能力集成  
- `RUNTIME_GUIDE.md` - Runtime选择指南

### 代码示例
- `examples/` - 各种示例项目
- `test/` - 完整的测试用例

### API文档
- `src/Aevatar.Agents.Abstractions/` - 核心接口
- `src/Aevatar.Agents.Core/` - 基础实现

---

**深入理解，才能掌控分布式智能的震动** 🌌

