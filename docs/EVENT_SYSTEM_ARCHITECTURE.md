# Aevatar Agent Framework - 事件系统架构设计

## 🎯 事件系统概述

Aevatar Agent Framework的事件系统是整个框架的核心，它实现了**统一的事件模型、智能的路由机制、可靠的传输保证**以及**完善的事件溯源**功能。事件系统采用事件信封模式，确保所有组件间的通信都是标准化的、可追踪的、可重放的。

## 🏗️ 事件系统架构

```
┌─────────────────────────────────────────────────────────┐
│                事件发布层                                │
│           Agent事件发布 → 事件路由器                     │
├─────────────────────────────────────────────────────────┤
│                事件路由层                                │
│  ┌───────────────────────────────────────────────────┐   │
│  │EventRouter    │RoutingTable    │RoutingPolicy    │   │
│  │EventDirection │SmartRouting    │LoadBalancing    │   │
│  └───────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                事件处理层                                │
│  ┌──────────────┬──────────────┬────────────────────┐   │
│  │EventHandler  │EventProcessor│EventExecutor       │   │
│  │HandlerDiscovery│AsyncProcessing│ParallelExecution │   │
│  └──────────────┴──────────────┴────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                事件存储层                                │
│  ┌──────────────┬──────────────┬────────────────────┐   │
│  │EventStore    │EventStream   │SnapshotStore       │   │
│  │Persistence   │Replay        │Compaction          │   │
│  └──────────────┴──────────────┴────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                事件传输层                                │
│  ┌──────────────┬──────────────┬────────────────────┐   │
│  │Transport     │Serialization │DeliveryGuarantee   │   │
│  │Channel       │Protobuf      │AtLeastOnce         │   │
│  └──────────────┴──────────────┴────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                可靠性保证                                │
│  ┌──────────────┬──────────────┬────────────────────┐   │
│  │Deduplication │RetryPolicy   │CircuitBreaker      │   │
│  │Idempotency   │DeadLetter    │PoisonMessage       │   │
│  └──────────────┴──────────────┴────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

## 🔧 核心事件模型

### 1. 事件基础接口

```csharp
// 基础事件标记接口
public interface IEvent
{
    string EventType { get; }
    DateTime Timestamp { get; }
    string Source { get; }
    int Version { get; }
}

// 带数据的事件接口
public interface IEvent<out TData> : IEvent
{
    TData Data { get; }
}

// 领域事件接口
public interface IDomainEvent : IEvent
{
    string AggregateId { get; }
    long AggregateVersion { get; }
}

// 集成事件接口
public interface IIntegrationEvent : IEvent
{
    string CorrelationId { get; }
    string TenantId { get; }
    Dictionary<string, string> Headers { get; }
}
```

### 2. 事件信封设计

```csharp
public class EventEnvelope
{
    // 事件标识
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string CorrelationId { get; init; }
    public string CausationId { get; init; }

    // 时间戳
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public DateTime? ScheduledTime { get; init; }

    // 事件元数据
    public string EventType { get; init; }
    public int EventVersion { get; init; } = 1;
    public string AggregateType { get; init; }
    public string AggregateId { get; init; }
    public long AggregateVersion { get; init; }

    // 路由信息
    public string SourceAgentId { get; init; }
    public string TargetAgentId { get; init; }
    public EventDirection Direction { get; init; } = EventDirection.Bidirectional;
    public int Priority { get; init; } = 0;

    // 传输信息
    public string Channel { get; init; } = "default";
    public DeliveryOptions DeliveryOptions { get; init; } = new();

    // 可靠性信息
    public int RetryCount { get; init; } = 0;
    public DateTime? FirstAttemptTime { get; init; }
    public DateTime? LastAttemptTime { get; init; }
    public string DeadLetterReason { get; init; }

    // 内容
    public IEvent Event { get; init; }
    public string SerializedEvent { get; init; }
    public string ContentType { get; init; } = "application/json";

    // 上下文
    public Dictionary<string, string> Headers { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();

    // 追踪信息
    public ActivityContext TraceContext { get; init; }
    public string TraceId { get; init; }
    public string SpanId { get; init; }

    // 序列化支持
    public TEvent GetEvent<TEvent>() where TEvent : class, IEvent
    {
        return Event as TEvent ?? DeserializeEvent<TEvent>();
    }

    private TEvent DeserializeEvent<TEvent>() where TEvent : class, IEvent
    {
        if (!string.IsNullOrEmpty(SerializedEvent))
        {
            return JsonSerializer.Deserialize<TEvent>(SerializedEvent);
        }
        return null;
    }
}

// 事件传播方向
public enum EventDirection
{
    Up,              // 向父级代理传播
    Down,            // 向子级代理传播
    Bidirectional,   // 双向传播
    Local,           // 仅本地处理
    Broadcast        // 广播到所有代理
}

// 传输选项
public class DeliveryOptions
{
    public bool Persistent { get; init; } = true;
    public bool Guaranteed { get; init; } = true;
    public int MaxRetries { get; init; } = 3;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan? TimeToLive { get; init; }
    public bool Deduplicate { get; init; } = true;
    public DeliveryPriority Priority { get; init; } = DeliveryPriority.Normal;
}

// 传输优先级
public enum DeliveryPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}
```

### 3. 事件发布接口

```csharp
public interface IEventPublisher
{
    // 基础发布方法
    Task PublishAsync<TEvent>(TEvent @event, EventDirection direction = EventDirection.Bidirectional,
        DeliveryOptions options = null, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    Task PublishAsync<TEvent>(TEvent @event, string targetAgentId,
        DeliveryOptions options = null, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    // 批量发布
    Task PublishBatchAsync<TEvent>(IEnumerable<TEvent> events, EventDirection direction = EventDirection.Bidirectional,
        DeliveryOptions options = null, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    // 延迟发布
    Task ScheduleAsync<TEvent>(TEvent @event, DateTime scheduledTime,
        EventDirection direction = EventDirection.Bidirectional, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    // 条件发布
    Task PublishIfAsync<TEvent>(TEvent @event, Func<EventEnvelope, Task<bool>> condition,
        EventDirection direction = EventDirection.Bidirectional, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    // 发布选项
    Task PublishWithOptionsAsync<TEvent>(TEvent @event, Action<EventOptionsBuilder> optionsBuilder)
        where TEvent : IEvent;
}

// 事件选项构建器
public class EventOptionsBuilder
{
    public EventDirection Direction { get; set; } = EventDirection.Bidirectional;
    public string TargetAgentId { get; set; }
    public int Priority { get; set; } = 0;
    public DeliveryOptions DeliveryOptions { get; set; } = new();
    public Dictionary<string, string> Headers { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public string CorrelationId { get; set; }
    public string CausationId { get; set; }
}
```

## 🔄 事件路由系统

### 1. 事件路由器接口

```csharp
public interface IEventRouter
{
    Task RouteAsync(EventEnvelope envelope, CancellationToken cancellationToken = default);
    Task RouteBatchAsync(IEnumerable<EventEnvelope> envelopes, CancellationToken cancellationToken = default);

    // 路由注册
    Task RegisterRouteAsync(string pattern, IEventHandler handler);
    Task UnregisterRouteAsync(string pattern);
    Task<List<RouteInfo>> GetRoutesAsync();

    // 路由策略
    Task SetRoutingPolicyAsync(string agentType, IRoutingPolicy policy);
    Task<IRoutingPolicy> GetRoutingPolicyAsync(string agentType);
}

// 路由信息
public class RouteInfo
{
    public string Pattern { get; init; }
    public string HandlerType { get; init; }
    public string Description { get; init; }
    public bool IsActive { get; init; }
    public Dictionary<string, object> Metadata { get; init; }
}

// 路由策略
public interface IRoutingPolicy
{
    Task<RoutingDecision> ShouldRouteAsync(EventEnvelope envelope, RoutingContext context);
    Task<List<string>> GetTargetAgentsAsync(EventEnvelope envelope, RoutingContext context);
}

public class RoutingDecision
{
    public bool ShouldRoute { get; init; }
    public string Reason { get; init; }
    public RoutingPriority Priority { get; init; } = RoutingPriority.Normal;
    public Dictionary<string, object> Options { get; init; } = new();
}

public enum RoutingPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}
```

### 2. 智能路由实现

```csharp
public class SmartEventRouter : IEventRouter
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly IEventHandlerRegistry _handlerRegistry;
    private readonly IRoutingPolicyProvider _policyProvider;
    private readonly IEventDeduplicator _deduplicator;
    private readonly ILogger<SmartEventRouter> _logger;

    private readonly ConcurrentDictionary<string, List<IEventHandler>> _handlerRoutes;
    private readonly ConcurrentDictionary<string, IRoutingPolicy> _routingPolicies;

    public async Task RouteAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        using var activity = StartActivity($"Route {envelope.EventType}");

        try
        {
            _logger.LogDebug("Routing event {EventId} of type {EventType}", envelope.Id, envelope.EventType);

            // 1. 事件去重检查
            if (await ShouldDeduplicateAsync(envelope))
            {
                if (await _deduplicator.IsDuplicateAsync(envelope.Id))
                {
                    _logger.LogDebug("Duplicate event {EventId} ignored", envelope.Id);
                    return;
                }
            }

            // 2. 确定目标代理
            var targetAgents = await GetTargetAgentsAsync(envelope);
            if (!targetAgents.Any())
            {
                _logger.LogWarning("No target agents found for event {EventId}", envelope.Id);
                return;
            }

            // 3. 应用路由策略
            var routingTasks = targetAgents.Select(async agentId =>
            {
                var context = new RoutingContext { SourceAgentId = envelope.SourceAgentId, TargetAgentId = agentId };
                var policy = await GetRoutingPolicyAsync(agentId);
                var decision = await policy.ShouldRouteAsync(envelope, context);

                return new { AgentId = agentId, Decision = decision };
            });

            var routingDecisions = await Task.WhenAll(routingTasks);
            var allowedRoutes = routingDecisions.Where(r => r.Decision.ShouldRoute).ToList();

            // 4. 执行路由
            var routeTasks = allowedRoutes.Select(async route =>
            {
                try
                {
                    await RouteToAgentAsync(envelope, route.AgentId, route.Decision);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to route event {EventId} to agent {AgentId}",
                        envelope.Id, route.AgentId);
                }
            });

            await Task.WhenAll(routeTasks);

            // 5. 记录已处理事件
            if (envelope.DeliveryOptions.Deduplicate)
            {
                await _deduplicator.RecordProcessedAsync(envelope.Id);
            }

            _logger.LogInformation("Event {EventId} routed to {AgentCount} agents successfully",
                envelope.Id, allowedRoutes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error routing event {EventId}", envelope.Id);
            throw;
        }
    }

    private async Task<List<string>> GetTargetAgentsAsync(EventEnvelope envelope)
    {
        var targetAgents = new List<string>();

        switch (envelope.Direction)
        {
            case EventDirection.Local:
                // 仅本地处理
                targetAgents.Add(envelope.SourceAgentId);
                break;

            case EventDirection.Up:
                // 向父级代理传播
                var parentAgent = await _agentRegistry.GetParentAgentAsync(envelope.SourceAgentId);
                if (parentAgent != null)
                {
                    targetAgents.Add(parentAgent.Id);
                }
                break;

            case EventDirection.Down:
                // 向子级代理传播
                var childAgents = await _agentRegistry.GetChildAgentsAsync(envelope.SourceAgentId);
                targetAgents.AddRange(childAgents.Select(a => a.Id));
                break;

            case EventDirection.Bidirectional:
                // 双向传播
                var parent = await _agentRegistry.GetParentAgentAsync(envelope.SourceAgentId);
                if (parent != null)
                {
                    targetAgents.Add(parent.Id);
                }

                var children = await _agentRegistry.GetChildAgentsAsync(envelope.SourceAgentId);
                targetAgents.AddRange(children.Select(a => a.Id));
                break;

            case EventDirection.Broadcast:
                // 广播到所有代理
                var allAgents = await _agentRegistry.GetAllAgentsAsync();
                targetAgents.AddRange(allAgents.Select(a => a.Id));
                break;
        }

        // 添加特定目标代理
        if (!string.IsNullOrEmpty(envelope.TargetAgentId))
        {
            if (!targetAgents.Contains(envelope.TargetAgentId))
            {
                targetAgents.Add(envelope.TargetAgentId);
            }
        }

        // 基于事件类型查找感兴趣的代理
        var interestedAgents = await FindInterestedAgentsAsync(envelope.EventType);
        targetAgents.AddRange(interestedAgents.Where(id => !targetAgents.Contains(id)));

        return targetAgents.Distinct().ToList();
    }

    private async Task RouteToAgentAsync(EventEnvelope envelope, string agentId, RoutingDecision decision)
    {
        using var activity = StartActivity($"Route to {agentId}");

        try
        {
            // 获取代理Actor
            var agentActor = await _agentRegistry.GetAgentActorAsync(agentId);
            if (agentActor == null)
            {
                _logger.LogWarning("Agent {AgentId} not found for event routing", agentId);
                return;
            }

            // 创建目标事件信封
            var targetEnvelope = envelope with
            {
                TargetAgentId = agentId,
                Direction = EventDirection.Local,
                Metadata = new Dictionary<string, object>(envelope.Metadata)
                {
                    ["routing_priority"] = decision.Priority,
                    ["routing_reason"] = decision.Reason
                }
            };

            // 发送到代理
            await agentActor.HandleEventAsync(targetEnvelope);

            _logger.LogDebug("Event {EventId} routed to agent {AgentId} successfully",
                envelope.Id, agentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to route event {EventId} to agent {AgentId}",
                envelope.Id, agentId);
            throw;
        }
    }

    private async Task<bool> ShouldDeduplicateAsync(EventEnvelope envelope)
    {
        // 根据事件类型和配置决定是否去重
        var eventTypeConfig = await GetEventTypeConfigurationAsync(envelope.EventType);
        return eventTypeConfig?.EnableDeduplication ?? envelope.DeliveryOptions.Deduplicate;
    }

    private async Task<List<string>> FindInterestedAgentsAsync(string eventType)
    {
        // 查找注册了此事件类型的处理器的代理
        var handlers = await _handlerRegistry.GetHandlersForEventAsync(eventType);
        return handlers.Select(h => h.AgentId).Distinct().ToList();
    }
}
```

## 🎯 事件处理器系统

### 1. 事件处理器接口

```csharp
public interface IEventHandler
{
    string HandlerId { get; }
    string AgentId { get; }
    string[] HandledEventTypes { get; }

    Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken = default);
    Task<bool> CanHandleAsync(EventEnvelope envelope);
}

// 泛型事件处理器
public interface IEventHandler<in TEvent> : IEventHandler where TEvent : IEvent
{
    Task HandleAsync(TEvent @event, EventEnvelope envelope, CancellationToken cancellationToken = default);
}

// 事件处理器基类
public abstract class EventHandlerBase<TEvent> : IEventHandler<TEvent> where TEvent : IEvent
{
    public string HandlerId { get; } = Guid.NewGuid().ToString();
    public string AgentId { get; protected set; }
    public string[] HandledEventTypes { get; protected set; }

    protected EventHandlerBase(string agentId)
    {
        AgentId = agentId;
        HandledEventTypes = new[] { typeof(TEvent).Name };
    }

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Event is TEvent typedEvent)
        {
            await HandleAsync(typedEvent, envelope, cancellationToken);
        }
        else
        {
            throw new InvalidOperationException($"Handler {GetType().Name} cannot handle event of type {envelope.EventType}");
        }
    }

    public Task<bool> CanHandleAsync(EventEnvelope envelope)
    {
        return Task.FromResult(envelope.Event is TEvent);
    }

    public abstract Task HandleAsync(TEvent @event, EventEnvelope envelope, CancellationToken cancellationToken = default);
}
```

### 2. 事件处理器发现

```csharp
public interface IEventHandlerDiscovery
{
    Task<List<IEventHandler>> DiscoverHandlersAsync(object target);
    Task<List<IEventHandler>> DiscoverHandlersAsync(Type targetType);
    Task<List<IEventHandler>> DiscoverHandlersAsync(Assembly assembly);
}

public class AttributeBasedEventHandlerDiscovery : IEventHandlerDiscovery
{
    public async Task<List<IEventHandler>> DiscoverHandlersAsync(object target)
    {
        var handlers = new List<IEventHandler>();
        var targetType = target.GetType();

        // 查找标记有 [EventHandler] 的方法
        var methods = targetType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var method in methods)
        {
            var handlerAttr = method.GetCustomAttribute<EventHandlerAttribute>();
            if (handlerAttr != null)
            {
                var handler = CreateMethodBasedHandler(target, method, handlerAttr);
                if (handler != null)
                {
                    handlers.Add(handler);
                }
            }

            var allEventsAttr = method.GetCustomAttribute<AllEventsHandlerAttribute>();
            if (allEventsAttr != null)
            {
                var handler = CreateAllEventsHandler(target, method);
                if (handler != null)
                {
                    handlers.Add(handler);
                }
            }
        }

        await Task.CompletedTask;
        return handlers;
    }

    private IEventHandler CreateMethodBasedHandler(object target, MethodInfo method, EventHandlerAttribute attribute)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != 1)
        {
            return null;
        }

        var eventType = parameters[0].ParameterType;
        if (!typeof(IEvent).IsAssignableFrom(eventType))
        {
            return null;
        }

        var handlerType = typeof(MethodBasedEventHandler<>).MakeGenericType(eventType);
        return Activator.CreateInstance(handlerType, target, method) as IEventHandler;
    }
}

// 方法基础事件处理器
public class MethodBasedEventHandler<TEvent> : EventHandlerBase<TEvent> where TEvent : IEvent
{
    private readonly object _target;
    private readonly MethodInfo _method;

    public MethodBasedEventHandler(object target, MethodInfo method, string agentId) : base(agentId)
    {
        _target = target;
        _method = method;
    }

    public override async Task HandleAsync(TEvent @event, EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = _method.Invoke(_target, new object[] { @event });

            if (result is Task task)
            {
                await task;
            }
            else if (result is ValueTask valueTask)
            {
                await valueTask;
            }
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException ?? ex;
        }
    }
}
```

## 📦 事件存储与溯源

### 1. 事件存储接口

```csharp
public interface IEventStore
{
    // 存储事件
    Task AppendAsync(EventEnvelope envelope, CancellationToken cancellationToken = default);
    Task AppendBatchAsync(IEnumerable<EventEnvelope> envelopes, CancellationToken cancellationToken = default);

    // 读取事件
    Task<EventEnvelope> GetEventAsync(string eventId, CancellationToken cancellationToken = default);
    Task<List<EventEnvelope>> GetEventsAsync(string aggregateId, long fromVersion = 0, long toVersion = long.MaxValue,
        CancellationToken cancellationToken = default);

    // 事件流
    IAsyncEnumerable<EventEnvelope> GetEventStreamAsync(string aggregateId, long fromVersion = 0,
        CancellationToken cancellationToken = default);

    // 事件查询
    Task<List<EventEnvelope>> QueryEventsAsync(EventQuery query, CancellationToken cancellationToken = default);
    Task<long> GetEventCountAsync(string aggregateId, CancellationToken cancellationToken = default);

    // 事件版本管理
    Task<long> GetCurrentVersionAsync(string aggregateId, CancellationToken cancellationToken = default);
    Task<bool> EventExistsAsync(string eventId, CancellationToken cancellationToken = default);

    // 快照支持
    Task SaveSnapshotAsync(Snapshot snapshot, CancellationToken cancellationToken = default);
    Task<Snapshot> GetSnapshotAsync(string aggregateId, CancellationToken cancellationToken = default);
    Task<List<Snapshot>> GetSnapshotsAsync(string aggregateId, CancellationToken cancellationToken = default);
}

// 事件查询
public class EventQuery
{
    public string AggregateId { get; set; }
    public string[] AggregateIds { get; set; }
    public string EventType { get; set; }
    public string[] EventTypes { get; set; }
    public DateTime? FromTimestamp { get; set; }
    public DateTime? ToTimestamp { get; set; }
    public string SourceAgentId { get; set; }
    public string CorrelationId { get; set; }
    public Dictionary<string, object> MetadataFilter { get; set; }
    public int? MaxResults { get; set; }
    public int? Skip { get; set; }
    public string SortBy { get; set; } = "Timestamp";
    public bool SortDescending { get; set; } = true;
}

// 快照
public class Snapshot
{
    public string AggregateId { get; init; }
    public long Version { get; init; }
    public DateTime Timestamp { get; init; }
    public object State { get; init; }
    public string StateType { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}
```

### 2. 事件溯源支持

```csharp
public interface IEventSourcingSupport
{
    Task ReplayEventsAsync(string aggregateId, long fromVersion = 0, long toVersion = long.MaxValue,
        CancellationToken cancellationToken = default);

    Task ReplayToSnapshotAsync(string aggregateId, CancellationToken cancellationToken = default);

    Task<EventSourcingStatistics> GetStatisticsAsync(string aggregateId,
        CancellationToken cancellationToken = default);

    Task CompactEventsAsync(string aggregateId, long upToVersion,
        CancellationToken cancellationToken = default);
}

public class EventSourcingSupport : IEventSourcingSupport
{
    private readonly IEventStore _eventStore;
    private readonly IEventHandlerResolver _handlerResolver;
    private readonly ILogger<EventSourcingSupport> _logger;

    public async Task ReplayEventsAsync(string aggregateId, long fromVersion = 0, long toVersion = long.MaxValue,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting event replay for aggregate {AggregateId} from version {FromVersion} to {ToVersion}",
            aggregateId, fromVersion, toVersion);

        var stopwatch = Stopwatch.StartNew();
        var eventCount = 0;

        try
        {
            // 获取快照作为起点
            var snapshot = await _eventStore.GetSnapshotAsync(aggregateId, cancellationToken);
            var startVersion = snapshot?.Version + 1 ?? 0;

            if (startVersion > toVersion)
            {
                _logger.LogInformation("No events to replay for aggregate {AggregateId}", aggregateId);
                return;
            }

            // 获取事件流
            await foreach (var envelope in _eventStore.GetEventStreamAsync(aggregateId, startVersion, cancellationToken))
            {
                if (envelope.AggregateVersion > toVersion)
                {
                    break;
                }

                try
                {
                    // 重放事件
                    await ReplayEventAsync(envelope, cancellationToken);
                    eventCount++;

                    if (eventCount % 1000 == 0)
                    {
                        _logger.LogInformation("Replayed {EventCount} events for aggregate {AggregateId}",
                            eventCount, aggregateId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error replaying event {EventId} for aggregate {AggregateId}",
                        envelope.Id, aggregateId);
                    throw;
                }
            }

            stopwatch.Stop();

            _logger.LogInformation("Event replay completed for aggregate {AggregateId}. Replayed {EventCount} events in {Duration}ms",
                aggregateId, eventCount, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Event replay failed for aggregate {AggregateId} after {EventCount} events and {Duration}ms",
                aggregateId, eventCount, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task ReplayEventAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        // 解析事件处理器
        var handlers = await _handlerResolver.ResolveHandlersAsync(envelope);

        // 执行处理器
        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(envelope, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling replayed event {EventId} with handler {HandlerId}",
                    envelope.Id, handler.HandlerId);
                throw;
            }
        }
    }

    public async Task CompactEventsAsync(string aggregateId, long upToVersion, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting event compaction for aggregate {AggregateId} up to version {UpToVersion}",
            aggregateId, upToVersion);

        try
        {
            // 创建快照
            var snapshot = await CreateSnapshotAsync(aggregateId, upToVersion, cancellationToken);
            await _eventStore.SaveSnapshotAsync(snapshot, cancellationToken);

            // 删除已快照的事件（可选）
            if (await ShouldDeleteCompactedEventsAsync())
            {
                await DeleteEventsUpToVersionAsync(aggregateId, upToVersion, cancellationToken);
            }

            _logger.LogInformation("Event compaction completed for aggregate {AggregateId}", aggregateId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event compaction failed for aggregate {AggregateId}", aggregateId);
            throw;
        }
    }

    private async Task<Snapshot> CreateSnapshotAsync(string aggregateId, long version, CancellationToken cancellationToken)
    {
        // 重建聚合状态
        var aggregate = await RebuildAggregateAsync(aggregateId, version, cancellationToken);

        return new Snapshot
        {
            AggregateId = aggregateId,
            Version = version,
            Timestamp = DateTime.UtcNow,
            State = aggregate.State,
            StateType = aggregate.StateType,
            Metadata = new Dictionary<string, object>
            {
                ["compaction_reason"] = "manual",
                ["event_count"] = version
            }
        };
    }
}
```

## 🔐 可靠性与错误处理

### 1. 事件去重机制

```csharp
public interface IEventDeduplicator
{
    Task<bool> IsDuplicateAsync(string eventId);
    Task RecordProcessedAsync(string eventId);
    Task<bool> IsDuplicateAsync(EventEnvelope envelope);
    Task CleanupAsync(DateTime olderThan);
}

public class MemoryCacheEventDeduplicator : IEventDeduplicator
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryCacheEventDeduplicator> _logger;

    private const string EventIdPrefix = "event_";
    private readonly TimeSpan _defaultTtl = TimeSpan.FromHours(24);

    public MemoryCacheEventDeduplicator(IMemoryCache cache, ILogger<MemoryCacheEventDeduplicator> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task<bool> IsDuplicateAsync(string eventId)
    {
        var cacheKey = EventIdPrefix + eventId;
        var exists = _cache.TryGetValue(cacheKey, out _);

        if (exists)
        {
            _logger.LogDebug("Event {EventId} is a duplicate", eventId);
        }

        return Task.FromResult(exists);
    }

    public Task RecordProcessedAsync(string eventId)
    {
        var cacheKey = EventIdPrefix + eventId;
        _cache.Set(cacheKey, true, _defaultTtl);

        _logger.LogDebug("Recorded event {EventId} as processed", eventId);

        return Task.CompletedTask;
    }

    public async Task<bool> IsDuplicateAsync(EventEnvelope envelope)
    {
        // 检查事件ID
        var isDuplicate = await IsDuplicateAsync(envelope.Id);
        if (isDuplicate)
        {
            return true;
        }

        // 检查幂等键（如果有）
        if (envelope.Headers.TryGetValue("idempotency-key", out var idempotencyKey))
        {
            var cacheKey = $"idempotency_{idempotencyKey}";
            var exists = _cache.TryGetValue(cacheKey, out _);

            if (exists)
            {
                _logger.LogDebug("Event with idempotency key {IdempotencyKey} is a duplicate", idempotencyKey);
                return true;
            }

            // 记录幂等键
            _cache.Set(cacheKey, envelope.Id, _defaultTtl);
        }

        return false;
    }

    public Task CleanupAsync(DateTime olderThan)
    {
        // 内存缓存会自动处理过期项
        _logger.LogDebug("Cleanup completed for events older than {OlderThan}", olderThan);
        return Task.CompletedTask;
    }
}
```

### 2. 重试策略

```csharp
public interface IEventRetryPolicy
{
    Task<bool> ShouldRetryAsync(EventEnvelope envelope, Exception exception);
    Task<TimeSpan> GetRetryDelayAsync(EventEnvelope envelope, int attempt);
    Task UpdateRetryInfoAsync(EventEnvelope envelope, int attempt);
}

public class ExponentialBackoffRetryPolicy : IEventRetryPolicy
{
    private readonly ILogger<ExponentialBackoffRetryPolicy> _logger;

    public async Task<bool> ShouldRetryAsync(EventEnvelope envelope, Exception exception)
    {
        // 检查是否已达到最大重试次数
        if (envelope.RetryCount >= envelope.DeliveryOptions.MaxRetries)
        {
            _logger.LogWarning("Event {EventId} has reached maximum retry count of {MaxRetries}",
                envelope.Id, envelope.DeliveryOptions.MaxRetries);
            return false;
        }

        // 检查异常类型
        if (exception is ArgumentException || exception is UnauthorizedAccessException)
        {
            _logger.LogWarning("Event {EventId} failed with non-retryable exception {ExceptionType}",
                envelope.Id, exception.GetType().Name);
            return false;
        }

        // 检查超时
        if (envelope.DeliveryOptions.TimeToLive.HasValue)
        {
            var age = DateTime.UtcNow - envelope.Timestamp;
            if (age > envelope.DeliveryOptions.TimeToLive.Value)
            {
                _logger.LogWarning("Event {EventId} has exceeded TTL of {TTL}",
                    envelope.Id, envelope.DeliveryOptions.TimeToLive.Value);
                return false;
            }
        }

        await Task.CompletedTask;
        return true;
    }

    public async Task<TimeSpan> GetRetryDelayAsync(EventEnvelope envelope, int attempt)
    {
        // 指数退避：2^attempt * baseDelay，最大1分钟
        var baseDelay = envelope.DeliveryOptions.RetryDelay;
        var exponentialDelay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * baseDelay.TotalMilliseconds);
        var maxDelay = TimeSpan.FromMinutes(1);

        var delay = TimeSpan.FromTicks(Math.Min(exponentialDelay.Ticks, maxDelay.Ticks));

        // 添加随机抖动（0-20%）以避免雷群问题
        var jitter = new Random().NextDouble() * 0.2;
        var jitteredDelay = TimeSpan.FromTicks((long)(delay.Ticks * (1 + jitter)));

        await Task.CompletedTask;
        return jitteredDelay;
    }

    public async Task UpdateRetryInfoAsync(EventEnvelope envelope, int attempt)
    {
        // 更新重试信息
        envelope.Metadata["retry_attempt"] = attempt;
        envelope.Metadata["last_retry_time"] = DateTime.UtcNow;

        await Task.CompletedTask;
    }
}
```

### 3. 死信队列

```csharp
public interface IDeadLetterQueue
{
    Task AddAsync(EventEnvelope envelope, string reason, CancellationToken cancellationToken = default);
    Task<List<DeadLetterEvent>> GetDeadLettersAsync(string sourceAgentId = null, DateTime? fromDate = null,
        CancellationToken cancellationToken = default);
    Task<bool> RetryAsync(string deadLetterId, CancellationToken cancellationToken = default);
    Task<bool> RetryBatchAsync(IEnumerable<string> deadLetterIds, CancellationToken cancellationToken = default);
    Task PurgeAsync(DateTime olderThan, CancellationToken cancellationToken = default);
}

public class DeadLetterEvent
{
    public string Id { get; init; }
    public EventEnvelope Envelope { get; init; }
    public string Reason { get; init; }
    public DateTime DeadLetterTime { get; init; }
    public Dictionary<string, object> FailureInfo { get; init; } = new();
}
```

---

*本文档详细描述了事件系统的架构设计，包括事件模型、路由机制、存储与溯源、可靠性保证等核心组件，为构建可靠的事件驱动系统提供全面指导。*