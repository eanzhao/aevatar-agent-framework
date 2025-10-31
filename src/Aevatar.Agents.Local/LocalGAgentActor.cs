using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.Local;

/// <summary>
/// Local 运行时的 Agent Actor 实现
/// 负责层级关系管理、事件路由、Stream 管理
/// </summary>
public class LocalGAgentActor : IGAgentActor, IEventPublisher
{
    private readonly IGAgent _agent;
    private readonly ILogger _logger;
    private readonly IGAgentActorFactory _factory;
    private readonly Dictionary<Guid, LocalGAgentActor> _actorRegistry; // 全局 Actor 注册表（共享）

    // 层级关系
    private Guid? _parentId;
    private readonly HashSet<Guid> _childrenIds = new();

    // Stream（每个 Actor 一个独立的事件队列）
    private readonly Queue<EventEnvelope> _eventQueue = new();
    private readonly SemaphoreSlim _queueLock = new(1, 1);

    public LocalGAgentActor(
        IGAgent agent,
        IGAgentActorFactory factory,
        Dictionary<Guid, LocalGAgentActor> actorRegistry,
        ILogger? logger = null)
    {
        _agent = agent;
        _factory = factory;
        _actorRegistry = actorRegistry;
        _logger = logger ?? NullLogger.Instance;

        // 设置 Agent 的 EventPublisher（如果是 GAgentBase）
        if (agent is GAgentBase<object> baseAgent)
        {
            baseAgent.SetEventPublisher(this);
        }
        else
        {
            // 使用反射设置 EventPublisher
            var setPublisherMethod = agent.GetType().GetMethod("SetEventPublisher");
            setPublisherMethod?.Invoke(agent, [this]);
        }

        // 注册到全局 Actor 表
        _actorRegistry[agent.Id] = this;
    }

    public Guid Id => _agent.Id;

    public IGAgent GetAgent() => _agent;

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
            CorrelationId = Guid.NewGuid().ToString(), // TODO: 从上下文获取
            PublisherId = Id.ToString(),
            Direction = direction,
            ShouldStopPropagation = false,
            MaxHopCount = -1, // -1 表示无限制
            CurrentHopCount = 0,
            MinHopCount = -1,
            Message = $"Published by {Id}",
            PublishedTimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        envelope.Publishers.Add(Id.ToString());

        _logger.LogDebug("Agent {AgentId} publishing event {EventId} with direction {Direction}",
            Id, eventId, direction);

        // 路由事件
        await RouteEventAsync(envelope, ct);

        return eventId;
    }

    /// <summary>
    /// 路由事件到合适的目标
    /// </summary>
    private async Task RouteEventAsync(EventEnvelope envelope, CancellationToken ct)
    {
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
                // Parent 会再发送给它的 Children（形成兄弟节点广播）
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
            // 没有父节点，停止向上传播
            _logger.LogDebug("Event {EventId} has no parent, stopping upward propagation", envelope.Id);
            return;
        }

        if (_actorRegistry.TryGetValue(_parentId.Value, out var parentActor))
        {
            _logger.LogDebug("Sending event {EventId} to parent {ParentId}",
                envelope.Id, _parentId);
            await parentActor.HandleEventAsync(envelope, ct);
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
            if (_actorRegistry.TryGetValue(childId, out var childActor))
            {
                _logger.LogDebug("Sending event {EventId} to child {ChildId}",
                    envelope.Id, childId);

                // 创建副本并增加 HopCount
                var childEnvelope = envelope.Clone();
                childEnvelope.CurrentHopCount++;
                childEnvelope.Publishers.Add(Id.ToString());

                await childActor.HandleEventAsync(childEnvelope, ct);
            }
            else
            {
                _logger.LogWarning("Child actor {ChildId} not found", childId);
            }
        }
    }

    /// <summary>
    /// 处理接收到的事件
    /// </summary>
    public async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        _logger.LogDebug("Agent {AgentId} handling event {EventId}", Id, envelope.Id);

        // 检查 MinHopCount
        bool shouldProcess = envelope.MinHopCount <= 0 || envelope.CurrentHopCount >= envelope.MinHopCount;

        if (shouldProcess)
        {
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling event {EventId} in agent {AgentId}",
                    envelope.Id, Id);
            }
        }
        else
        {
            _logger.LogDebug("Event {EventId} not yet reached min hop count {MinHop}, skipping processing",
                envelope.Id, envelope.MinHopCount);
        }

        // 继续路由事件（传播给 Children/Parent，取决于 Direction）
        // 注意：这里不再调用 RouteEventAsync，而是直接根据 Direction 继续传播
        await ContinuePropagationAsync(envelope, ct);
    }

    /// <summary>
    /// 继续传播事件（不重复处理）
    /// </summary>
    private async Task ContinuePropagationAsync(EventEnvelope envelope, CancellationToken ct)
    {
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

        // 根据方向继续传播（只向下传播）
        if (envelope.Direction == EventDirection.Down ||
            envelope.Direction == EventDirection.Bidirectional)
        {
            await SendToChildrenAsync(envelope, ct);
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

        // 从注册表移除
        _actorRegistry.Remove(Id);
    }
}