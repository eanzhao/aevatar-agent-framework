using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Observability;
using Microsoft.Extensions.Logging;
using Google.Protobuf;

namespace Aevatar.Agents.Local;

/// <summary>
/// Local 运行时的 Agent Actor 实现
/// 使用 LocalMessageStream 作为消息传输机制
/// </summary>
public class LocalGAgentActor : GAgentActorBase
{
    private static int _activeActorCount = 0;
    private readonly LocalMessageStreamRegistry _streamRegistry;
    private readonly LocalMessageStream _myStream; // 这个 Actor 的 Stream
    private IMessageStreamSubscription? _parentStreamSubscription; // 父节点stream订阅句柄

    public LocalGAgentActor(
        IGAgent agent,
        LocalMessageStreamRegistry streamRegistry,
        ILogger? logger = null)
        : base(agent, logger)
    {
        _streamRegistry = streamRegistry ?? throw new ArgumentNullException(nameof(streamRegistry));

        // 获取这个 Actor 的 Stream
        _myStream = streamRegistry.GetOrCreateStream(agent.Id);
    }

    // ============ 层级关系管理（重写基类方法） ============

    public override async Task SetParentAsync(Guid parentId, CancellationToken ct = default)
    {
        // 如果已有父节点，先清除
        if (EventRouter.GetParent() != null)
        {
            await ClearParentAsync(ct);
        }
        
        // 调用基类方法设置父节点
        await base.SetParentAsync(parentId, ct);
        
        // 订阅父节点的stream
        var parentStream = _streamRegistry.GetStream(parentId);
        if (parentStream != null)
        {
            // 创建类型过滤器（如果Agent有特定的事件类型约束）
            Func<EventEnvelope, bool>? filter = null;
            
            // 检查Agent是否继承自GAgentBase<TState, TEvent>，获取TEvent类型
            var agentType = Agent.GetType();
            var baseType = agentType.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType && 
                    baseType.GetGenericTypeDefinition() == typeof(GAgentBase<,>))
                {
                    var eventType = baseType.GetGenericArguments()[1];
                    // 创建类型过滤器
                    filter = envelope =>
                    {
                        if (envelope.Payload == null) return false;
                        // 检查TypeUrl是否包含事件类型名
                        return envelope.Payload.TypeUrl.Contains(eventType.Name) ||
                               envelope.Payload.TypeUrl.Contains(eventType.FullName);
                    };
                    break;
                }
                baseType = baseType.BaseType;
            }
            
            // 创建组合过滤器：应用类型过滤、Publishers列表检查和方向过滤
            // - DOWN事件：不应该通过父stream广播，过滤掉
            // - UP事件：检查Publishers列表，避免重复处理
            // - BOTH事件：允许通过
            Func<EventEnvelope, bool>? combinedFilter = envelope =>
            {
                Logger.LogDebug("[FILTER] Agent {AgentId} checking envelope from parent stream - EventId={EventId}, Direction={Direction}, PublisherId={PublisherId}, PayloadType={PayloadType}, Publishers={Publishers}", 
                    Id, envelope.Id, envelope.Direction, envelope.PublisherId, envelope.Payload?.TypeUrl, string.Join(",", envelope.Publishers));
                    
                // DOWN事件不应该通过父stream广播，直接过滤掉
                // DOWN事件应该直接发送到子节点的stream，而不是通过父stream广播
                if (envelope.Direction == EventDirection.Down)
                {
                    Logger.LogDebug("[FILTER] Agent {AgentId} filtering out DOWN event {EventId} from parent stream", 
                        Id, envelope.Id);
                    return false;
                }
                    
                // 对于UP事件，检查自己是否已经在Publishers列表中
                if (envelope.Direction == EventDirection.Up && envelope.Publishers.Contains(Id.ToString()))
                {
                    Logger.LogDebug("[FILTER] Agent {AgentId} already in Publishers list for UP event {EventId}, filtering out", 
                        Id, envelope.Id);
                    return false;  // 过滤掉已经处理过的UP事件
                }
                    
                // 应用类型过滤
                if (filter != null)
                {
                    var result = filter(envelope);
                    Logger.LogDebug("[FILTER] Type filter result for Agent {AgentId}: {Result}", Id, result);
                    return result;
                }
                
                Logger.LogDebug("[FILTER] No type filter for Agent {AgentId}, allowing event", Id);
                return true;
            };
            
            // Agent订阅父节点的stream，接收组内广播的事件
            _parentStreamSubscription = await parentStream.SubscribeAsync<EventEnvelope>(
                async envelope =>
                {
                    // 从父stream接收到的事件处理逻辑：
                    // - UP事件：只需要处理，不需要继续传播（已在父stream广播）
                    // - DOWN事件：处理后需要继续向下传播给子节点（多层级传播）
                    
                    Logger.LogDebug("[SUBSCRIPTION] Agent {AgentId} received event {EventId} from parent stream, PublisherId={PublisherId}, PayloadType={PayloadType}", 
                        Id, envelope.Id, envelope.PublisherId, envelope.Payload?.TypeUrl);
                    
                    try
                    {
                        // 处理事件
                        Logger.LogDebug("Processing event {EventId} from parent stream on agent {AgentId}", 
                            envelope.Id, Id);
                            
                        // 先调用Agent的HandleEventAsync方法处理事件
                        var handleMethod = Agent.GetType().GetMethod("HandleEventAsync", 
                            new[] { typeof(EventEnvelope), typeof(CancellationToken) });
                        
                        if (handleMethod != null)
                        {
                            var task = handleMethod.Invoke(Agent, new object[] { envelope, ct }) as Task;
                            if (task != null)
                            {
                                await task;
                                Logger.LogDebug("Event {EventId} processed by agent {AgentId}", 
                                    envelope.Id, Id);
                            }
                        }
                        else
                        {
                            Logger.LogWarning("HandleEventAsync method not found on agent {AgentId}", Id);
                        }
                        
                        // 从父stream接收到的事件处理逻辑：
                        // - UP事件：只需要处理，不需要继续传播（已在父stream广播）
                        // - DOWN事件：处理后需要继续向下传播给子节点（多层级传播）
                        // - BOTH事件：继续向下传播给子节点
                        if (envelope.Direction == EventDirection.Down || 
                            envelope.Direction == EventDirection.Both)
                        {
                            Logger.LogDebug("Continuing {Direction} propagation of event {EventId} from agent {AgentId} to children", 
                                envelope.Direction, envelope.Id, Id);
                            await EventRouter.ContinuePropagationAsync(envelope, ct);
                        }
                        // UP事件不需要继续传播，因为它已经在父stream中广播
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error handling event {EventId} from parent stream on agent {AgentId}", 
                            envelope.Id, Id);
                    }
                },
                combinedFilter,
                ct);
            
            Logger.LogDebug("Agent {AgentId} subscribed to parent {ParentId} stream", Id, parentId);
        }
    }
    
    public override async Task ClearParentAsync(CancellationToken ct = default)
    {
        // 调用基类方法清除父节点
        await base.ClearParentAsync(ct);
        
        // 取消订阅父节点的stream
        if (_parentStreamSubscription != null)
        {
            await _parentStreamSubscription.UnsubscribeAsync();
            _parentStreamSubscription = null;
            Logger.LogDebug("Agent {AgentId} unsubscribed from parent stream", Id);
        }
    }

    // ============ 抽象方法实现 ============

    /// <summary>
    /// 发送事件给自己（通过自己的 Stream）
    /// </summary>
    protected override async Task SendToSelfAsync(EventEnvelope envelope, CancellationToken ct)
    {
        await _myStream.ProduceAsync(envelope, ct);
    }

    /// <summary>
    /// 发送事件到指定的 Actor（通过目标 Actor 的 Stream）
    /// </summary>
    protected override async Task SendEventToActorAsync(Guid actorId, EventEnvelope envelope, CancellationToken ct)
    {
        var targetStream = _streamRegistry.GetStream(actorId);
        if (targetStream != null)
        {
            await targetStream.ProduceAsync(envelope, ct);
        }
        else
        {
            Logger.LogWarning("Stream for actor {ActorId} not found", actorId);
        }
    }

    // ============ 事件发布（重写基类方法）============
    
    public override async Task<string> PublishEventAsync<TEvent>(
        TEvent evt,
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // 使用 EventRouter 创建 EventEnvelope
        var envelope = EventRouter.CreateEventEnvelope(evt, direction);

        using var scope = Core.Observability.LoggingScope.CreateAgentScope(
            Logger, 
            Id, 
            "PublishEvent",
            new Dictionary<string, object>
            {
                ["EventId"] = envelope.Id,
                ["EventType"] = typeof(TEvent).Name,
                ["Direction"] = direction.ToString()
            });

        Logger.LogDebug("Agent {AgentId} publishing event {EventId} with direction {Direction}",
            Id, envelope.Id, direction);

        try
        {
            // 根据不同的方向处理
            switch (direction)
            {
                case EventDirection.Up:
                    // UP方向：直接使用EventRouter来路由事件
                    // EventRouter会：1) 先发送给自己处理 2) 然后发送到父节点的stream
                    await EventRouter.RouteEventAsync(envelope, ct);
                    Logger.LogDebug("Event {EventId} routed via EventRouter for UP direction", envelope.Id);
                    break;
                    
                case EventDirection.Down:
                    // DOWN方向：使用EventRouter来路由事件
                    await EventRouter.RouteEventAsync(envelope, ct);
                    Logger.LogDebug("Event {EventId} routed via EventRouter for DOWN direction", envelope.Id);
                    break;
                    
                case EventDirection.Both:
                    // BOTH方向：使用EventRouter来路由事件
                    await EventRouter.RouteEventAsync(envelope, ct);
                    Logger.LogDebug("Event {EventId} routed via EventRouter for BOTH direction", envelope.Id);
                    break;
            }
            
            // 记录发布指标
            stopwatch.Stop();
            AgentMetrics.RecordEventPublished(typeof(TEvent).Name, Id.ToString());
            AgentMetrics.EventPublishLatency.Record(stopwatch.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("event_type", typeof(TEvent).Name));
            
            Logger.LogDebug("Agent {AgentId} successfully published event {EventId}",
                Id, envelope.Id);
            
            return envelope.Id;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Agent {AgentId} failed to publish event {EventType}",
                Id, typeof(TEvent).Name);
            
            // 记录失败指标
            AgentMetrics.RecordException(ex.GetType().Name, Id.ToString(), "PublishEvent");
            
            throw;
        }
    }
    
    // ============ 生命周期 ============

    public override async Task ActivateAsync(CancellationToken ct = default)
    {
        Logger.LogInformation("Activating agent {AgentId}", Id);

        // 订阅自己的 Stream
        await _myStream.SubscribeAsync<EventEnvelope>(
            async envelope => await HandleEventAsync(envelope, ct),
            ct);

        // 调用 Agent 的激活回调
        var activateMethod = Agent.GetType().GetMethod("OnActivateAsync");
        if (activateMethod != null)
        {
            var task = activateMethod.Invoke(Agent, new object[] { ct }) as Task;
            if (task != null)
            {
                await task;
            }
        }
        
        // 更新活跃 Actor 计数
        var count = Interlocked.Increment(ref _activeActorCount);
        AgentMetrics.UpdateActiveActorCount(count);
        Logger.LogDebug("Active actor count: {Count}", count);
    }

    public override async Task DeactivateAsync(CancellationToken ct = default)
    {
        Logger.LogInformation("Deactivating agent {AgentId}", Id);

        // 调用 Agent 的停用回调
        var deactivateMethod = Agent.GetType().GetMethod("OnDeactivateAsync");
        if (deactivateMethod != null)
        {
            var task = deactivateMethod.Invoke(Agent, new object[] { ct }) as Task;
            if (task != null)
            {
                await task;
            }
        }

        // 停止并移除 Stream
        _streamRegistry.RemoveStream(Id);
        
        // 更新活跃 Actor 计数
        var count = Interlocked.Decrement(ref _activeActorCount);
        AgentMetrics.UpdateActiveActorCount(count);
        Logger.LogDebug("Active actor count: {Count}", count);
    }
}