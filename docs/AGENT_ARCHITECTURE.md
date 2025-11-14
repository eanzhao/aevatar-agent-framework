# Aevatar Agent 架构文档

## 概述

Agent 层是 Aevatar 框架的核心，提供统一的 Agent 开发模型。所有 Agent 都基于 `GAgentBase<TState>` 或 `GAgentBase<TState, TConfig>` 基类构建。

## 基类体系

### 1. GAgentBase<TState> - 基础版本

**适用场景**：简单的 Agent，只需要状态管理，不需要配置。

**特性**：
- ✅ 状态自动持久化（当使用 IStateStore 时）
- ✅ 事件处理器自动发现和调用
- ✅ 事件发布和订阅
- ✅ 指标和遥测
- ✅ 资源管理
- ✅ 层级关系管理

```csharp
public class SimpleAgent : GAgentBase<SimpleState>
{
    [EventHandler]
    public async Task HandleEvent(MyEvent evt)
    {
        State.Count++;  // 自动加载/保存
        await PublishAsync(new ResultEvent { Value = State.Count });
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult("Simple agent");
}
```

### 2. GAgentBase<TState, TConfig> - 带配置版本

**适用场景**：需要实例级别配置的 Agent，每个实例有不同配置。

**特性**：
- ✅ 所有 `GAgentBase<TState>` 特性
- ✅ 配置自动持久化（独立存储）
- ✅ 运行时配置修改
- ✅ 配置与状态分离

```csharp
public class CounterAgent : GAgentBase<CounterState, CounterConfig>
{
    [EventHandler]
    public async Task HandleIncrement(IncrementEvent evt)
    {
        if (!Config.IsEnabled) return;

        State.Count += Config.IncrementStep;

        if (State.Count >= Config.MaxValue)
        {
            Config.IsEnabled = false;  // 修改配置，自动保存
        }
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult($"Counter agent. Current: {State.Count}");
}

public class CounterConfig
{
    public int IncrementStep { get; set; } = 1;
    public int MaxValue { get; set; } = 1000;
    public bool IsEnabled { get; set; } = true;
}
```

## 存储抽象

### IStateStore<TState> - 状态存储接口

```csharp
public interface IStateStore<TState>
    where TState : class
{
    Task<TState?> LoadAsync(Guid agentId, CancellationToken ct = default);
    Task SaveAsync(Guid agentId, TState state, CancellationToken ct = default);
    Task DeleteAsync(Guid agentId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid agentId, CancellationToken ct = default);
}

public interface IVersionedStateStore<TState> : IStateStore<TState>
    where TState : class
{
    Task SaveAsync(Guid agentId, TState state, long expectedVersion, CancellationToken ct = default);
    Task<long> GetCurrentVersionAsync(Guid agentId, CancellationToken ct = default);
}
```

**实现**：
- `InMemoryStateStore<TState>` - 内存实现（测试、Local 运行）
- `MongoDBStateStore<TState>` - MongoDB 持久化（生产）
- `EventSourcingStateStore<TState>` - 事件溯源（可选）

### IConfigurationStore<TConfig> - 配置存储接口

```csharp
public interface IConfigurationStore<TConfig>
    where TConfig : class, new()
{
    Task<TConfig?> LoadAsync(Guid agentId, CancellationToken ct = default);
    Task SaveAsync(Guid agentId, TConfig config, CancellationToken ct = default);
    Task DeleteAsync(Guid agentId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid agentId, CancellationToken ct = default);
}
```

**实现**：
- `InMemoryConfigurationStore<TConfig>` - 内存实现
- `MongoDBConfigurationStore<TConfig>` - MongoDB 持久化

## 配置系统

### Agent 配置

```csharp
// 1. 配置 StateStore
services.ConfigGAgentStateStore(options =>
{
    options.StateStore = sp => new InMemoryStateStore<SimpleState>();
});

// 2. 注册 Agent
services.ConfigGAgent<SimpleAgent, SimpleState>();
```

### Agent 配置（带配置）

```csharp
services.ConfigGAgentStateStore(options =>
{
    options.StateStore = sp => new MongoDBStateStore<CounterState>(database);
    options.ConfigStore = sp => new MongoDBConfigurationStore<CounterConfig>(database);
});

services.ConfigGAgent<CounterAgent, CounterState, CounterConfig>();
```

## 事件处理

### 事件处理器方法

事件处理器通过反射自动发现：

1. **EventHandlerAttribute** - 标记方法为事件处理器
2. **AllEventHandlerAttribute** - 接收所有事件
3. **默认命名** - `HandleAsync`, `HandleEventAsync` 方法名

```csharp
public class MyAgent : GAgentBase<MyState>
{
    // 使用特性标记
    [EventHandler(Priority = 1)]
    public async Task ProcessOrder(OrderEvent evt)
    {
        State.OrderCount++;
    }

    // 接收所有事件（Debug、Audit 等）
    [AllEventHandler(Priority = int.MaxValue)]
    public async Task Audit(EventEnvelope envelope)
    {
        State.LastEventId = envelope.Id;
    }

    // 默认命名
    public async Task HandleAsync(PaymentEvent evt)
    {
        State.TotalAmount += evt.Amount;
    }
}
```

### 事件处理器优先级

```csharp
[EventHandler(Priority = 10)]  // 先执行
public async Task HighPriorityHandler(Event evt) { }

[EventHandler(Priority = 100)] // 后执行
public async Task LowPriorityHandler(Event evt) { }
```

### 禁止自处理

```csharp
[EventHandler(AllowSelfHandling = true)]  // 允许处理自己发出的事件
public async Task Handle(MyEvent evt) { }
```

## 依赖注入

### 自动注入的依赖项

在 Agent 创建时，以下依赖会自动从 DI 注入：

1. **Logger** (ILogger) - 日志
2. **StateStore** (IStateStore<TState>) - 状态存储（如果注册）
3. **ConfigStore** (IConfigurationStore<TConfig>) - 配置存储（如果注册）
4. **EventPublisher** (IEventPublisher) - 事件发布器（由 Actor 提供）

### 手动注入其他依赖

```csharp
public class MyAgent : GAgentBase<MyState>
{
    [Inject]  // 自定义注入特性
    public IMyService MyService { get; set; }

    public MyAgent(IMyService myService) : base()
    {
        MyService = myService;
    }
}
```

## 生命周期管理

### 回调方法

```csharp
public class MyAgent : GAgentBase<MyState>
{
    /// <summary>
    /// Agent 激活时调用
    /// </summary>
    public override Task OnActivateAsync(CancellationToken ct = default)
    {
        Logger.LogInformation("Agent {Id} activated", Id);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Agent 停用时调用
    /// </summary>
    public override Task OnDeactivateAsync(CancellationToken ct = default)
    {
        Logger.LogInformation("Agent {Id} deactivated", Id);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 准备资源上下文
    /// </summary>
    public override Task PrepareResourceContextAsync(ResourceContext context, CancellationToken ct = default)
    {
        // 处理资源分配
        return Task.CompletedTask;
    }
}
```

## 资源管理

### ResourceContext

```csharp
public class MyAgent : GAgentBase<MyState>
{
    [EventHandler]
    public async Task Handle(ProcessEvent evt)
    {
        var context = new ResourceContext();
        await PrepareResourceContextAsync(context);

        using var resource = context.GetResource<IComputeResource>();
        var result = await resource.ProcessAsync(evt.Data);

        State.Results.Add(result);
    }
}
```

## 层级关系

### 父子关系

```csharp
public class ParentAgent : GAgentBase<ParentState>
{
    [EventHandler]
    public async Task Handle(CreateChildEvent evt)
    {
        // 创建子 Agent
        var childId = Guid.NewGuid();
        var child = await Manager.CreateAgentAsync<ChildAgent>(childId);

        // 设置父子关系
        await child.SetParentAsync(Id);

        State.Children.Add(childId);
    }
}

public class ChildAgent : GAgentBase<ChildState>
{
    [EventHandler]
    public async Task Handle(WorkEvent evt)
    {
        // 事件自动向上传播给父 Agent
        await PublishAsync(new ProgressEvent { Progress = 50 });
    }
}
```

## 遥测和指标

### Metrics

```csharp
// 自动收集的指标
AgentMetrics.EventsHandled.Add(1,
    new KeyValuePair<string, object?>("agent.id", Id),
    new KeyValuePair<string, object?>("event.type", "OrderEvent"));

AgentMetrics.EventHandlingLatency.Record(durationMs,
    new KeyValuePair<string, object?>("event.type", "OrderEvent"));

AgentMetrics.RecordException("DatabaseException", Id.ToString(), "SaveState");
```

### Distributed Tracing

```csharp
using var activity = Observability.ActivitySource.StartActivity("ProcessOrder");
activity?.SetTag("event.id", envelope.Id);
activity?.SetTag("agent.id", Id);

// 业务逻辑
```

## 最佳实践

### 1. State 设计

```csharp
public class OrderState
{
    // 不可变属性 - 无需快照
    public string OrderId { get; init; } = string.Empty;

    // 可变属性 - 需要快照
    public OrderStatus Status { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    public decimal TotalAmount { get; set; }

    // 元数据
    public long Version { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
}
```

### 2. 事件设计

```csharp
// 好的实践：事件名清晰，包含足够信息
public class OrderApprovedEvent
{
    public string OrderId { get; set; } = string.Empty;
    public DateTime ApprovedAt { get; set; }
    public string ApprovedBy { get; set; } = string.Empty;
}

// 不好的实践：事件名模糊
public class UpdateEvent  // 太模糊
{
    public object Data { get; set; } = null!;  // 类型不安全
}
```

### 3. 配置设计

```csharp
public class ProcessorConfig
{
    // 有合理默认值
    public int BatchSize { get; set; } = 100;
    public int MaxConcurrency { get; set; } = 10;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    // 启用/禁用功能
    public bool EnableCaching { get; set; } = true;
    public bool EnableRetry { get; set; } = true;

    // 策略配置
    public RetryStrategy RetryStrategy { get; set; } = RetryStrategy.ExponentialBackoff;
    public int MaxRetries { get; set; } = 3;
}
```

### 4. 错误处理

```csharp
public class MyAgent : GAgentBase<MyState>
{
    [EventHandler]
    public async Task Handle(ProcessEvent evt)
    {
        try
        {
            await ProcessWithRetryAsync(evt);
        }
        catch (Exception ex) when (IsRetryable(ex))
        {
            // 发布到异常流
            await PublishExceptionEventAsync(
                envelope: EventRouter.CreateEventEnvelope(evt),
                handlerName: nameof(Handle),
                exception: ex);

            // 降级处理
            await HandleGracefulDegradation(evt);
        }
    }

    private bool IsRetryable(Exception ex)
    {
        return ex is NetworkException or TimeoutException;
    }
}
```

## 迁移指南

### 从旧版本迁移

#### 迁移 StatefulGAgentBase

```csharp
// 旧代码
public class MyAgent : StatefulGAgentBase<MyState>
{
    public override async Task<string> GetDescriptionAsync()
    {
        return "Old agent";
    }
}

// 新代码
public class MyAgent : GAgentBase<MyState>
{
    [EventHandler]
    public async Task Handle(MyEvent evt)
    {
        // State 自动加载/保存
        State.Count++;
    }

    public override async Task<string> GetDescriptionAsync()
    {
        return "Refactored agent";
    }
}
```

#### 迁移 EventSourcingAgent

```csharp
// 旧代码
public class MyAgent : EventSourcingGAgentBase<MyState>
{
    // 直接继承
}

// 新代码 - EventSourcing 是配置，不是基类
services.ConfigGAgentStateStore(options =>
{
    options.StateStore = sp => new EventSourcingStateStore<MyState>(
        eventStore: sp.GetRequiredService<IEventStore>(),
        eventApplier: sp.GetRequiredService<IEventApplier<MyState>>());

    options.EnableEventSourcing = true;
    options.EventStore = sp => sp.GetRequiredService<IEventStore>();
});

public class MyAgent : GAgentBase<MyState>
{
    [EventHandler]
    public async Task Handle(MyEvent evt)
    {
        // 自动使用 EventSourcingStateStore
        State.Count++;
    }
}
```

## 总结

Agent 层提供了完整的开发模型：

1. **统一基类** - `GAgentBase<TState>` 和 `GAgentBase<TState, TConfig>`
2. **存储抽象** - 状态、配置分离，多种实现（InMemory、MongoDB）
3. **事件驱动** - 自动发现和调用事件处理器
4. **依赖注入** - Logger、StateStore、ConfigStore 自动注入
5. **生命周期** - 激活、停用、资源管理
6. **遥测** - 指标和分布式追踪

Agent 开发者只需关注业务逻辑，框架自动处理持久化、事件路由、依赖注入等基础设施。
