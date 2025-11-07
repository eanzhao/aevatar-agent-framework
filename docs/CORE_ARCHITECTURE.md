# Aevatar Agent Framework - 核心架构设计

## 🏛️ 架构总览

核心架构基于事件驱动的Actor模型，提供统一的抽象层来隔离不同运行时实现的复杂性。架构设计遵循以下关键原则：

- **统一抽象**: 通过接口抽象隐藏运行时差异
- **事件驱动**: 所有组件间通信通过事件进行
- **类型安全**: 泛型和强类型确保编译时安全
- **可观测性**: 内置度量和跟踪支持
- **可扩展性**: 插件式架构支持功能扩展

## 🔧 核心抽象层

### 1. 基础接口层次结构

```csharp
// 最基础的身份标识
public interface IGAgent
{
    string Id { get; }
}

// 有状态代理
public interface IStateGAgent<TState> : IGAgent where TState : class, new()
{
    TState State { get; }
}

// 运行时Actor抽象
public interface IGAgentActor : IGAgent
{
    Task ActivateAsync();
    Task DeactivateAsync();
    Task HandleEventAsync(EventEnvelope @event);
}

// 事件发布接口
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent @event, EventDirection direction = EventDirection.Bidirectional)
        where TEvent : IEvent;
}
```

### 2. 事件系统核心设计

#### 事件信封模式
```csharp
public class EventEnvelope
{
    public string Id { get; init; }                    // 唯一标识
    public DateTime Timestamp { get; init; }           // 时间戳
    public string EventType { get; init; }             // 事件类型
    public string CorrelationId { get; init; }         // 关联ID
    public string SourceAgentId { get; init; }         // 源代理ID
    public EventDirection Direction { get; init; }     // 传播方向
    public Dictionary<string, string> Metadata { get; init; } // 元数据
    public IEvent Event { get; init; }                 // 实际事件
}
```

#### 事件传播方向
```csharp
public enum EventDirection
{
    Up,           // 向父级传播
    Down,         // 向子级传播
    Bidirectional // 双向传播
}
```

### 3. 核心基类实现

#### GAgentBase<TState> 设计
```csharp
public abstract class GAgentBase<TState> : IStateGAgent<TState>, IEventPublisher
    where TState : class, new()
{
    // 状态管理
    protected TState State { get; private set; }

    // 事件发布
    protected Task PublishAsync<TEvent>(TEvent @event, EventDirection direction = EventDirection.Bidirectional)
        where TEvent : IEvent;

    // 事件处理发现
    private readonly Dictionary<Type, MethodInfo> _eventHandlers;

    // 可观测性
    private readonly ILogger _logger;
    private readonly IMetrics _metrics;

    // 构造函数初始化
    protected GAgentBase()
    {
        State = new TState();
        _eventHandlers = DiscoverEventHandlers();
        SetupLoggingScope();
    }

    // 事件处理器自动发现
    private Dictionary<Type, MethodInfo> DiscoverEventHandlers()
    {
        var handlers = new Dictionary<Type, MethodInfo>();
        var methods = GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        foreach (var method in methods)
        {
            // 查找 [EventHandler] 标记的方法
            var handlerAttr = method.GetCustomAttribute<EventHandlerAttribute>();
            if (handlerAttr != null)
            {
                var eventParam = method.GetParameters().FirstOrDefault();
                if (eventParam != null && typeof(IEvent).IsAssignableFrom(eventParam.ParameterType))
                {
                    handlers[eventParam.ParameterType] = method;
                }
            }

            // 查找 [AllEventHandler] 标记的方法
            var allHandlerAttr = method.GetCustomAttribute<AllEventHandlerAttribute>();
            if (allHandlerAttr != null)
            {
                // 注册为通用处理器
                _allEventHandlers.Add(method);
            }
        }

        return handlers;
    }

    // 事件处理逻辑
    protected async Task HandleEventAsync(IEvent @event)
    {
        using var activity = StartActivity($"Handle {@event.GetType().Name}");

        try
        {
            _logger.LogDebug("Handling event {EventType}", @event.GetType().Name);

            // 查找特定事件处理器
            if (_eventHandlers.TryGetValue(@event.GetType(), out var handler))
            {
                await InvokeHandler(handler, @event);
            }

            // 调用通用处理器
            foreach (var allHandler in _allEventHandlers)
            {
                await InvokeHandler(allHandler, @event);
            }

            _metrics.IncrementCounter("events.handled", tags: new() { ["agent_type"] = GetType().Name });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling event {EventType}", @event.GetType().Name);

            // 发布异常事件
            await PublishAsync(new EventHandlingException(@event, ex));

            _metrics.IncrementCounter("events.handler_errors", tags: new() { ["agent_type"] = GetType().Name });

            throw;
        }
    }
}
```

#### GAgentActorBase 设计
```csharp
public abstract class GAgentActorBase : IGAgentActor
{
    protected readonly IGAgent _agent;
    protected readonly IEventPublisher _eventPublisher;
    private readonly IEventDeduplicator _deduplicator;
    private readonly List<IGAgentActor> _children;
    private IGAgentActor _parent;

    // 事件处理核心逻辑
    public async Task HandleEventAsync(EventEnvelope envelope)
    {
        // 1. 事件去重检查
        if (await _deduplicator.IsDuplicateAsync(envelope.Id))
        {
            _logger.LogDebug("Duplicate event {EventId} ignored", envelope.Id);
            return;
        }

        // 2. 处理事件
        try
        {
            using var activity = StartActivity($"Actor {_agent.Id} handle {envelope.EventType}");

            // 直接处理事件
            await HandleEventCoreAsync(envelope);

            // 3. 根据方向传播事件
            await PropagateEventAsync(envelope);

            // 4. 记录已处理事件
            await _deduplicator.RecordProcessedAsync(envelope.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing event {EventId}", envelope.Id);
            throw;
        }
    }

    private async Task PropagateEventAsync(EventEnvelope envelope)
    {
        switch (envelope.Direction)
        {
            case EventDirection.Up:
                await PropagateToParentAsync(envelope);
                break;

            case EventDirection.Down:
                await PropagateToChildrenAsync(envelope);
                break;

            case EventDirection.Bidirectional:
                await Task.WhenAll(
                    PropagateToParentAsync(envelope),
                    PropagateToChildrenAsync(envelope)
                );
                break;
        }
    }

    private async Task PropagateToChildrenAsync(EventEnvelope envelope)
    {
        var tasks = _children.Select(child =>
            child.HandleEventAsync(envelope with { Direction = EventDirection.Down })
        );

        await Task.WhenAll(tasks);
    }

    private async Task PropagateToParentAsync(EventEnvelope envelope)
    {
        if (_parent != null)
        {
            await _parent.HandleEventAsync(envelope with { Direction = EventDirection.Up });
        }
    }
}
```

## 🔄 状态管理设计

### 状态生命周期
```csharp
public interface IStateManager<TState> where TState : class, new()
{
    Task<TState> LoadStateAsync(string agentId);
    Task SaveStateAsync(string agentId, TState state);
    Task ClearStateAsync(string agentId);
    Task<bool> StateExistsAsync(string agentId);
}
```

### 状态快照策略
```csharp
public interface ISnapshotStrategy
{
    bool ShouldCreateSnapshot(int eventCount, TimeSpan timeSinceLastSnapshot);
    int GetSnapshotInterval();
}

public class DefaultSnapshotStrategy : ISnapshotStrategy
{
    public bool ShouldCreateSnapshot(int eventCount, TimeSpan timeSinceLastSnapshot)
    {
        return eventCount >= 100 || timeSinceLastSnapshot >= TimeSpan.FromMinutes(5);
    }

    public int GetSnapshotInterval() => 100;
}
```

## 📡 事件路由系统

### 事件路由器接口
```csharp
public interface IEventRouter
{
    Task RouteAsync(EventEnvelope envelope, RoutingContext context);
    Task RegisterRouteAsync(string pattern, IEventHandler handler);
}

public class RoutingContext
{
    public string SourceAgentId { get; init; }
    public EventDirection Direction { get; init; }
    public Dictionary<string, object> Metadata { get; init; }
    public CancellationToken CancellationToken { get; init; }
}
```

### 智能路由逻辑
```csharp
public class SmartEventRouter : IEventRouter
{
    private readonly Dictionary<string, List<IEventHandler>> _routes;
    private readonly IAgentRegistry _agentRegistry;

    public async Task RouteAsync(EventEnvelope envelope, RoutingContext context)
    {
        // 1. 基于模式匹配查找路由
        var matchingRoutes = FindMatchingRoutes(envelope);

        // 2. 基于上下文过滤路由
        var applicableRoutes = FilterRoutesByContext(matchingRoutes, context);

        // 3. 执行路由
        var tasks = applicableRoutes.Select(route =>
            ExecuteRouteAsync(route, envelope, context)
        );

        await Task.WhenAll(tasks);
    }

    private List<IEventHandler> FindMatchingRoutes(EventEnvelope envelope)
    {
        var handlers = new List<IEventHandler>();

        // 精确匹配
        if (_routes.TryGetValue(envelope.EventType, out var exactMatches))
        {
            handlers.AddRange(exactMatches);
        }

        // 通配符匹配
        foreach (var (pattern, routeHandlers) in _routes)
        {
            if (pattern.EndsWith("*") && envelope.EventType.StartsWith(pattern.TrimEnd('*')))
            {
                handlers.AddRange(routeHandlers);
            }
        }

        return handlers.Distinct().ToList();
    }
}
```

## 🛠️ 依赖注入设计

### 服务注册模式
```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAevatarAgents(this IServiceCollection services, Action<AgentOptions> configure = null)
    {
        var options = new AgentOptions();
        configure?.Invoke(options);

        // 核心服务
        services.AddSingleton<IEventRouter, SmartEventRouter>();
        services.AddSingleton<IEventDeduplicator, MemoryCacheEventDeduplicator>();
        services.AddSingleton<IStateManager, InMemoryStateManager>();
        services.AddSingleton<ISnapshotStrategy, DefaultSnapshotStrategy>();

        // 运行时特定服务
        if (options.UseLocalRuntime)
        {
            services.AddLocalAgentRuntime();
        }

        if (options.UseOrleansRuntime)
        {
            services.AddOrleansAgentRuntime();
        }

        if (options.UseProtoActorRuntime)
        {
            services.AddProtoActorAgentRuntime();
        }

        // 可观测性
        services.AddSingleton<IMetrics, DefaultMetrics>();
        services.AddSingleton<ITracer, DefaultTracer>();

        return services;
    }
}
```

## 📊 性能优化策略

### 1. 事件批处理
```csharp
public class BatchingEventProcessor
{
    private readonly Channel<EventEnvelope> _eventChannel;
    private readonly int _batchSize;
    private readonly TimeSpan _batchTimeout;

    public async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        var batch = new List<EventEnvelope>();
        var batchTimer = Stopwatch.StartNew();

        await foreach (var envelope in _eventChannel.Reader.ReadAllAsync(cancellationToken))
        {
            batch.Add(envelope);

            if (batch.Count >= _batchSize || batchTimer.Elapsed >= _batchTimeout)
            {
                await ProcessBatchAsync(batch);
                batch.Clear();
                batchTimer.Restart();
            }
        }

        // 处理剩余事件
        if (batch.Count > 0)
        {
            await ProcessBatchAsync(batch);
        }
    }
}
```

### 2. 内存池化
```csharp
public class EventEnvelopePool
{
    private readonly ObjectPool<EventEnvelope> _pool;

    public EventEnvelope Rent()
    {
        return _pool.Get();
    }

    public void Return(EventEnvelope envelope)
    {
        // 重置状态
        envelope.Metadata.Clear();
        envelope.Direction = EventDirection.Bidirectional;

        _pool.Return(envelope);
    }
}
```

### 3. 异步处理优化
```csharp
public class OptimizedEventProcessor
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConcurrency;

    public async Task ProcessEventsAsync(IEnumerable<EventEnvelope> events)
    {
        var tasks = events.Select(async envelope =>
        {
            await _semaphore.WaitAsync();

            try
            {
                await ProcessEventAsync(envelope);
            }
            finally
            {
                _semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }
}
```

## 🔐 错误处理与恢复

### 异常处理策略
```csharp
public class ResilientEventHandler
{
    private readonly IRetryPolicy _retryPolicy;
    private readonly ICircuitBreaker _circuitBreaker;

    public async Task HandleEventAsync(EventEnvelope envelope)
    {
        try
        {
            await _circuitBreaker.ExecuteAsync(async () =>
            {
                await _retryPolicy.ExecuteAsync(async () =
                {
                    await ProcessEventAsync(envelope);
                });
            });
        }
        catch (Exception ex)
        {
            // 降级处理
            await HandleFallbackAsync(envelope, ex);

            // 发布异常事件
            await PublishExceptionEventAsync(envelope, ex);
        }
    }
}
```

## 📋 配置模式

### 配置类设计
```csharp
public class AgentOptions
{
    public bool UseLocalRuntime { get; set; } = true;
    public bool UseOrleansRuntime { get; set; } = false;
    public bool UseProtoActorRuntime { get; set; } = false;

    public EventProcessingOptions EventProcessing { get; set; } = new();
    public SnapshotOptions Snapshotting { get; set; } = new();
    public ObservabilityOptions Observability { get; set; } = new();
}

public class EventProcessingOptions
{
    public int MaxConcurrency { get; set; } = 10;
    public int BatchSize { get; set; } = 100;
    public TimeSpan BatchTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
    public bool EnableDeduplication { get; set; } = true;
}

public class SnapshotOptions
{
    public bool EnableSnapshots { get; set; } = true;
    public int EventsPerSnapshot { get; set; } = 100;
    public TimeSpan SnapshotInterval { get; set; } = TimeSpan.FromMinutes(5);
}
```

---

*本文档详细描述了核心架构的设计原则和实现细节，为开发和优化提供指导。*