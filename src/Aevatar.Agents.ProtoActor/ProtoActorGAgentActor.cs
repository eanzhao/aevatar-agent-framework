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
    private readonly IGAgentActorFactory _factory;
    private readonly IRootContext _rootContext;
    private readonly PID _actorPid;
    private readonly Dictionary<Guid, PID> _actorRegistry; // 全局 Actor PID 注册表（共享）
    
    // 层级关系
    private Guid? _parentId;
    private readonly HashSet<Guid> _childrenIds = new();
    
    public ProtoActorGAgentActor(
        IGAgent agent,
        IGAgentActorFactory factory,
        IRootContext rootContext,
        PID actorPid,
        Dictionary<Guid, PID> actorRegistry,
        ILogger? logger = null)
    {
        _agent = agent;
        _factory = factory;
        _rootContext = rootContext;
        _actorPid = actorPid;
        _actorRegistry = actorRegistry;
        _logger = logger ?? NullLogger.Instance;
        
        // 设置 Agent 的 EventPublisher（使用反射）
        var setPublisherMethod = agent.GetType().GetMethod("SetEventPublisher");
        setPublisherMethod?.Invoke(agent, new object[] { this });
        
        // 注册到全局 Actor PID 表
        _actorRegistry[agent.Id] = actorPid;
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
            Message = $"Published by {Id}"
        };
        
        envelope.Publishers.Add(Id.ToString());
        
        _logger.LogDebug("Agent {AgentId} publishing event {EventId} with direction {Direction}",
            Id, eventId, direction);
        
        // 路由事件
        await RouteEventAsync(envelope, ct);
        
        return eventId;
    }
    
    /// <summary>
    /// 路由事件
    /// </summary>
    private async Task RouteEventAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (envelope.ShouldStopPropagation)
            return;
        
        if (envelope.MaxHopCount > 0 && envelope.CurrentHopCount >= envelope.MaxHopCount)
        {
            _logger.LogDebug("Event {EventId} reached max hop count {MaxHop}", 
                envelope.Id, envelope.MaxHopCount);
            return;
        }
        
        switch (envelope.Direction)
        {
            case EventDirection.Up:
                await SendToParentAsync(envelope, ct);
                break;
            
            case EventDirection.Down:
                await SendToChildrenAsync(envelope, ct);
                break;
            
            case EventDirection.UpThenDown:
                await SendToParentAsync(envelope, ct);
                break;
            
            case EventDirection.Bidirectional:
                await SendToParentAsync(envelope, ct);
                await SendToChildrenAsync(envelope, ct);
                break;
        }
    }
    
    /// <summary>
    /// 发送事件到父 Actor
    /// </summary>
    private async Task SendToParentAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (_parentId == null)
        {
            _logger.LogDebug("Event {EventId} has no parent, handling locally", envelope.Id);
            await HandleEventAsync(envelope, ct);
            return;
        }
        
        if (_actorRegistry.TryGetValue(_parentId.Value, out var parentPid))
        {
            _logger.LogDebug("Sending event {EventId} to parent {ParentId}", 
                envelope.Id, _parentId);
            
            // 通过 Proto.Actor 发送消息
            _rootContext.Send(parentPid, new HandleEventMessage { Envelope = envelope });
        }
        else
        {
            _logger.LogWarning("Parent actor {ParentId} not found", _parentId);
        }
    }
    
    /// <summary>
    /// 发送事件到所有子 Actor
    /// </summary>
    private async Task SendToChildrenAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (_childrenIds.Count == 0)
        {
            _logger.LogDebug("Event {EventId} has no children", envelope.Id);
            return;
        }
        
        foreach (var childId in _childrenIds)
        {
            if (_actorRegistry.TryGetValue(childId, out var childPid))
            {
                _logger.LogDebug("Sending event {EventId} to child {ChildId}", 
                    envelope.Id, childId);
                
                var childEnvelope = envelope.Clone();
                childEnvelope.CurrentHopCount++;
                childEnvelope.Publishers.Add(Id.ToString());
                
                // 通过 Proto.Actor 发送消息
                _rootContext.Send(childPid, new HandleEventMessage { Envelope = childEnvelope });
            }
            else
            {
                _logger.LogWarning("Child actor {ChildId} not found", childId);
            }
        }
        
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// 处理接收到的事件（由 AgentActor 调用）
    /// </summary>
    public async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        _logger.LogDebug("Agent {AgentId} handling event {EventId}", Id, envelope.Id);
        
        // 检查 MinHopCount
        if (envelope.MinHopCount > 0 && envelope.CurrentHopCount < envelope.MinHopCount)
        {
            _logger.LogDebug("Event {EventId} not yet reached min hop count {MinHop}, continuing routing",
                envelope.Id, envelope.MinHopCount);
            
            await RouteEventAsync(envelope, ct);
            return;
        }
        
        try
        {
            // 让 Agent 处理事件（使用反射调用）
            var handleMethod = _agent.GetType().GetMethod("HandleEventAsync");
            if (handleMethod != null)
            {
                var task = handleMethod.Invoke(_agent, new object[] { envelope, ct }) as Task;
                if (task != null)
                {
                    await task;
                }
            }
            
            // 继续路由事件
            await RouteEventAsync(envelope, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling event {EventId} in agent {AgentId}", 
                envelope.Id, Id);
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
        _actorRegistry.Remove(Id);
    }
}

/// <summary>
/// Proto.Actor 消息：处理事件
/// </summary>
public class HandleEventMessage
{
    public required EventEnvelope Envelope { get; init; }
}
