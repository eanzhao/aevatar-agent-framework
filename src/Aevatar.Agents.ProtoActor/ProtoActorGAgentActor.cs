using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Proto;

namespace Aevatar.Agents.ProtoActor;

/// <summary>
/// Proto.Actor 运行时的 Agent Actor 实现
/// </summary>
public class ProtoActorGAgentActor : IGAgentActor, IEventPublisher
{
    private readonly IGAgent _agent;
    private readonly ILogger _logger;
    private readonly IRootContext _rootContext;
    private readonly PID _actorPid;
    private readonly ProtoActorMessageStreamRegistry _streamRegistry;
    private readonly ProtoActorMessageStream _myStream;  // 这个 Actor 的 Stream

    // 层级关系
    private Guid? _parentId;
    private readonly HashSet<Guid> _childrenIds = new();

    public ProtoActorGAgentActor(
        IGAgent agent,
        IRootContext rootContext,
        PID actorPid,
        ProtoActorMessageStreamRegistry streamRegistry,
        ILogger? logger = null)
    {
        _agent = agent;
        _rootContext = rootContext;
        _actorPid = actorPid;
        _streamRegistry = streamRegistry;
        _logger = logger ?? NullLogger.Instance;

        // 注册 PID 并获取 Stream
        _streamRegistry.RegisterPid(agent.Id, actorPid);
        _myStream = _streamRegistry.GetStream(agent.Id)!;

        // 设置 Agent 的 EventPublisher
        var setPublisherMethod = agent.GetType().GetMethod("SetEventPublisher");
        setPublisherMethod?.Invoke(agent, new object[] { this });
    }
    
    public Guid Id => _agent.Id;
    
    public IGAgent GetAgent() => _agent;
    
    /// <summary>
    /// 获取 Proto.Actor PID
    /// </summary>
    public PID GetPid() => _actorPid;
    
    // ============ 层级关系管理 ============
    
    public Task AddChildAsync(Guid childId, CancellationToken ct = default)
    {
        _childrenIds.Add(childId);
        _logger.LogDebug("Agent {AgentId} added child {ChildId}", Id, childId);
        return Task.CompletedTask;
    }
    
    public Task RemoveChildAsync(Guid childId, CancellationToken ct = default)
    {
        _childrenIds.Remove(childId);
        _logger.LogDebug("Agent {AgentId} removed child {ChildId}", Id, childId);
        return Task.CompletedTask;
    }
    
    public Task SetParentAsync(Guid parentId, CancellationToken ct = default)
    {
        _parentId = parentId;
        _logger.LogDebug("Agent {AgentId} set parent to {ParentId}", Id, parentId);
        return Task.CompletedTask;
    }
    
    public Task ClearParentAsync(CancellationToken ct = default)
    {
        _parentId = null;
        _logger.LogDebug("Agent {AgentId} cleared parent", Id);
        return Task.CompletedTask;
    }
    
    public Task<IReadOnlyList<Guid>> GetChildrenAsync()
    {
        return Task.FromResult<IReadOnlyList<Guid>>(_childrenIds.ToList());
    }
    
    public Task<Guid?> GetParentAsync()
    {
        return Task.FromResult(_parentId);
    }
    
    // ============ 事件发布（IEventPublisher 实现） ============
    
    async Task<string> IEventPublisher.PublishAsync<TEvent>(
        TEvent evt,
        EventDirection direction,
        CancellationToken ct)
    {
        return await PublishEventAsync(evt, direction, ct);
    }
    
    // ============ 事件发布和路由 ============
    
    public async Task<string> PublishEventAsync<TEvent>(
        TEvent evt,
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default) 
        where TEvent : IMessage
    {
        var eventId = Guid.NewGuid().ToString();
        
        // 创建 EventEnvelope
        var envelope = new EventEnvelope
        {
            Id = eventId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Version = 1,
            Payload = Any.Pack(evt),
            CorrelationId = Guid.NewGuid().ToString(),
            PublisherId = Id.ToString(),
            Direction = direction,
            ShouldStopPropagation = false,
            MaxHopCount = -1,
            CurrentHopCount = 0,
            MinHopCount = -1,
            Message = $"Published by {Id}",
            PublishedTimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        
        envelope.Publishers.Add(Id.ToString());
        
        _logger.LogDebug("Agent {AgentId} publishing event {EventId} with direction {Direction}",
            Id, eventId, direction);

        // 通过 Stream 路由事件
        await RouteEventViaStreamAsync(envelope, ct);

        return eventId;
    }
    
    /// <summary>
    /// 通过 Stream 路由事件
    /// </summary>
    private async Task RouteEventViaStreamAsync(EventEnvelope envelope, CancellationToken ct)
    {
        switch (envelope.Direction)
        {
            case EventDirection.Up:
                await SendToParentStreamAsync(envelope, ct);
                break;

            case EventDirection.Down:
                await SendToChildrenStreamsAsync(envelope, ct);
                break;

            case EventDirection.UpThenDown:
                await SendToParentStreamAsync(envelope, ct);
                break;

            case EventDirection.Bidirectional:
                await SendToParentStreamAsync(envelope, ct);
                await SendToChildrenStreamsAsync(envelope, ct);
                break;
        }
    }

    /// <summary>
    /// 发送事件到父 Agent 的 Stream
    /// </summary>
    private async Task SendToParentStreamAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (_parentId == null)
        {
            // 没有父节点，发送到自己的 Stream
            _logger.LogDebug("Event {EventId} has no parent, handling locally", envelope.Id);
            await _myStream.ProduceAsync(envelope, ct);
            return;
        }

        var parentStream = _streamRegistry.GetStream(_parentId.Value);
        if (parentStream != null)
        {
            _logger.LogDebug("Sending event {EventId} to parent {ParentId} stream",
                envelope.Id, _parentId);
            await parentStream.ProduceAsync(envelope, ct);
        }
        else
        {
            _logger.LogWarning("Parent stream {ParentId} not found", _parentId);
        }
    }

    /// <summary>
    /// 发送事件到所有子 Agent 的 Stream
    /// </summary>
    private async Task SendToChildrenStreamsAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (_childrenIds.Count == 0)
        {
            _logger.LogDebug("Event {EventId} has no children", envelope.Id);
            return;
        }

        foreach (var childId in _childrenIds)
        {
            var childStream = _streamRegistry.GetStream(childId);
            if (childStream != null)
            {
                _logger.LogDebug("Sending event {EventId} to child {ChildId} stream",
                    envelope.Id, childId);

                var childEnvelope = envelope.Clone();
                childEnvelope.CurrentHopCount++;
                childEnvelope.Publishers.Add(Id.ToString());

                await childStream.ProduceAsync(childEnvelope, ct);
            }
            else
            {
                _logger.LogWarning("Child stream {ChildId} not found", childId);
            }
        }
    }
    
    /// <summary>
    /// 处理接收到的事件（由 AgentActor 调用，从消息系统接收）
    /// </summary>
    public async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        _logger.LogDebug("Agent {AgentId} handling event {EventId} from actor message", Id, envelope.Id);

        // 检查是否应该停止传播
        if (envelope.ShouldStopPropagation)
            return;

        // 检查 MaxHopCount
        if (envelope.MaxHopCount > 0 && envelope.CurrentHopCount >= envelope.MaxHopCount)
        {
            _logger.LogDebug("Event {EventId} reached max hop count {MaxHop}",
                envelope.Id, envelope.MaxHopCount);
            return;
        }

        // 检查 MinHopCount
        bool shouldProcess = envelope.MinHopCount <= 0 || envelope.CurrentHopCount >= envelope.MinHopCount;

        if (shouldProcess)
        {
            try
            {
                // 让 Agent 处理事件
                var handleMethod = _agent.GetType().GetMethod("HandleEventAsync");
                if (handleMethod != null)
                {
                    var task = handleMethod.Invoke(_agent, new object[] { envelope, ct }) as Task;
                    if (task != null)
                    {
                        await task;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling event {EventId} in agent {AgentId}",
                    envelope.Id, Id);
            }
        }

        // 继续传播（向下）
        if (envelope.Direction == EventDirection.Down ||
            envelope.Direction == EventDirection.Bidirectional)
        {
            await SendToChildrenStreamsAsync(envelope, ct);
        }
    }
    
    // ============ 生命周期 ============
    
    public async Task ActivateAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Activating agent {AgentId}", Id);
        
        // 调用 OnActivateAsync（如果存在）
        var activateMethod = _agent.GetType().GetMethod("OnActivateAsync");
        if (activateMethod != null)
        {
            var task = activateMethod.Invoke(_agent, new object[] { ct }) as Task;
            if (task != null)
            {
                await task;
            }
        }
    }
    
    public async Task DeactivateAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Deactivating agent {AgentId}", Id);
        
        // 调用 OnDeactivateAsync（如果存在）
        var deactivateMethod = _agent.GetType().GetMethod("OnDeactivateAsync");
        if (deactivateMethod != null)
        {
            var task = deactivateMethod.Invoke(_agent, new object[] { ct }) as Task;
            if (task != null)
            {
                await task;
            }
        }
        
        // 停止 Proto.Actor Actor
        await _rootContext.StopAsync(_actorPid);

        // 从注册表移除
        _streamRegistry.Remove(Id);
    }
}


/// <summary>
/// Proto.Actor 消息：处理事件
/// </summary>
public class HandleEventMessage
{
    public required EventEnvelope Envelope { get; init; }
}
