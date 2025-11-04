using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.Core.EventRouting;

/// <summary>
/// 事件路由器
/// 提供标准的事件传播逻辑实现
/// </summary>
public class EventRouter
{
    private readonly Guid _agentId;
    private readonly ILogger _logger;
    
    // 层级关系
    private Guid? _parentId;
    private readonly HashSet<Guid> _childrenIds = new();
    
    // 事件发送委托
    private readonly Func<Guid, EventEnvelope, CancellationToken, Task> _sendToActorAsync;
    private readonly Func<EventEnvelope, CancellationToken, Task> _sendToSelfAsync;
    
    public EventRouter(
        Guid agentId,
        Func<Guid, EventEnvelope, CancellationToken, Task> sendToActorAsync,
        Func<EventEnvelope, CancellationToken, Task> sendToSelfAsync,
        ILogger? logger = null)
    {
        _agentId = agentId;
        _sendToActorAsync = sendToActorAsync ?? throw new ArgumentNullException(nameof(sendToActorAsync));
        _sendToSelfAsync = sendToSelfAsync ?? throw new ArgumentNullException(nameof(sendToSelfAsync));
        _logger = logger ?? NullLogger.Instance;
    }
    
    // ============ 层级关系管理 ============
    
    public void AddChild(Guid childId)
    {
        _childrenIds.Add(childId);
        _logger.LogDebug("Agent {AgentId} added child {ChildId}", _agentId, childId);
    }
    
    public void RemoveChild(Guid childId)
    {
        _childrenIds.Remove(childId);
        _logger.LogDebug("Agent {AgentId} removed child {ChildId}", _agentId, childId);
    }
    
    public void SetParent(Guid parentId)
    {
        _parentId = parentId;
        _logger.LogDebug("Agent {AgentId} set parent to {ParentId}", _agentId, parentId);
    }
    
    public void ClearParent()
    {
        _parentId = null;
        _logger.LogDebug("Agent {AgentId} cleared parent", _agentId);
    }
    
    public Guid? GetParent() => _parentId;
    
    public IReadOnlyList<Guid> GetChildren() => _childrenIds.ToList();
    
    // ============ 事件创建 ============
    
    public EventEnvelope CreateEventEnvelope<TEvent>(
        TEvent evt,
        EventDirection direction) where TEvent : IMessage
    {
        var eventId = Guid.NewGuid().ToString();
        
        var envelope = new EventEnvelope
        {
            Id = eventId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Version = 1,
            Payload = Any.Pack(evt),
            CorrelationId = Guid.NewGuid().ToString(), // TODO: 从上下文获取
            PublisherId = _agentId.ToString(),
            Direction = direction,
            ShouldStopPropagation = false,
            MaxHopCount = 50, // 设置合理的默认最大跳数，防止无限递归
            CurrentHopCount = 0,
            MinHopCount = -1,
            Message = $"Published by {_agentId}",
            PublishedTimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        
        envelope.Publishers.Add(_agentId.ToString());
        
        return envelope;
    }
    
    // ============ 事件路由（核心逻辑） ============
    
    public async Task RouteEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        _logger.LogDebug("Agent {AgentId} routing event {EventId} with direction {Direction}",
            _agentId, envelope.Id, envelope.Direction);
        
        // 首先，发布者自己总是应该处理事件（除非事件处理器明确拒绝）
        // 这样符合"发布-订阅"模式的语义
        await _sendToSelfAsync(envelope, ct);
        
        // 然后根据方向传播给其他节点
        switch (envelope.Direction)
        {
            case EventDirection.Up:
                await SendToParentAsync(envelope, ct);
                break;
                
            case EventDirection.Down:
                await SendToChildrenAsync(envelope, ct);
                break;
                
            case EventDirection.Both:
                await SendToParentAsync(envelope, ct);
                await SendToChildrenAsync(envelope, ct);
                break;
        }
    }
    
    /// <summary>
    /// 发送事件到父节点
    /// </summary>
    private async Task SendToParentAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (_parentId == null)
        {
            // 没有父节点，事件传播结束（符合 Up 方向的语义）
            _logger.LogDebug("Event {EventId} has no parent, propagation ends", envelope.Id);
            return;
        }
        
        // UP方向：检查是否会形成循环
        // 如果父节点已经在Publishers列表中，说明形成了循环
        if (envelope.Publishers.Contains(_parentId.ToString()))
        {
            _logger.LogWarning("Event {EventId} already visited parent {ParentId}, skipping to avoid loop",
                envelope.Id, _parentId);
            return;
        }
        
        _logger.LogDebug("Sending event {EventId} to parent {ParentId}",
            envelope.Id, _parentId);
        
        // 创建副本并增加 HopCount，添加当前节点到Publishers列表
        var parentEnvelope = envelope.Clone();
        parentEnvelope.CurrentHopCount++;
        parentEnvelope.Publishers.Add(_agentId.ToString());
        
        await _sendToActorAsync(_parentId.Value, parentEnvelope, ct);
    }
    
    /// <summary>
    /// 发送事件到所有子节点
    /// </summary>
    private async Task SendToChildrenAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (_childrenIds.Count == 0)
        {
            // 没有子节点，事件传播结束（符合 Down 方向的语义）
            _logger.LogDebug("Event {EventId} has no children, propagation ends", envelope.Id);
            return;
        }
        
        // 检查最大跳数限制，防止无限递归
        if (envelope.MaxHopCount > 0 && envelope.CurrentHopCount >= envelope.MaxHopCount)
        {
            _logger.LogWarning("Event {EventId} reached max hop count {MaxHop}, stopping DOWN propagation",
                envelope.Id, envelope.MaxHopCount);
            return;
        }
        
        // 安全检查：如果当前跳数异常高，强制停止以防止栈溢出
        const int SafetyMaxHops = 100;
        if (envelope.CurrentHopCount >= SafetyMaxHops)
        {
            _logger.LogError("Event {EventId} exceeded safety max hop count {SafetyMax}, force stopping to prevent stack overflow",
                envelope.Id, SafetyMaxHops);
            return;
        }
        
        foreach (var childId in _childrenIds)
        {
            // DOWN方向也需要检查循环：如果子节点已经在Publishers列表中，说明形成了循环
            if (envelope.Publishers.Contains(childId.ToString()))
            {
                _logger.LogWarning("Event {EventId} already visited child {ChildId}, skipping to avoid loop in DOWN direction",
                    envelope.Id, childId);
                continue;
            }
            
            // 防止节点将事件发送给自己（父子关系配置错误的情况）
            if (childId == _agentId)
            {
                _logger.LogError("Agent {AgentId} attempted to send event to itself as child, skipping to prevent loop",
                    _agentId);
                continue;
            }
            
            _logger.LogDebug("Sending event {EventId} to child {ChildId}",
                envelope.Id, childId);
            
            // 创建副本并增加 HopCount
            var childEnvelope = envelope.Clone();
            childEnvelope.CurrentHopCount++;
            childEnvelope.Publishers.Add(_agentId.ToString());
            
            await _sendToActorAsync(childId, childEnvelope, ct);
        }
    }
    
    /// <summary>
    /// 检查是否应该处理事件
    /// </summary>
    public bool ShouldProcessEvent(EventEnvelope envelope)
    {
        // 检查是否应该停止传播
        if (envelope.ShouldStopPropagation)
            return false;
        
        // 检查 MaxHopCount
        if (envelope.MaxHopCount > 0 && envelope.CurrentHopCount >= envelope.MaxHopCount)
        {
            _logger.LogDebug("Event {EventId} reached max hop count {MaxHop}",
                envelope.Id, envelope.MaxHopCount);
            return false;
        }
        
        // 检查 MinHopCount
        bool shouldProcess = envelope.MinHopCount <= 0 || envelope.CurrentHopCount >= envelope.MinHopCount;
        
        return shouldProcess;
    }
    
    /// <summary>
    /// 处理接收到的事件的继续传播
    /// </summary>
    public async Task ContinuePropagationAsync(EventEnvelope envelope, CancellationToken ct)
    {
        // 根据方向继续传播（递归传播）
        switch (envelope.Direction)
        {
            case EventDirection.Down:
                // Down 方向继续向下传播给子节点
                await SendToChildrenAsync(envelope, ct);
                break;
                
            case EventDirection.Up:
                // Up 方向：只有当事件不是从父节点stream接收到的时候才继续向上传播
                // 如果Publishers列表中包含父节点ID，说明事件已经通过父节点stream广播过了
                if (_parentId.HasValue && !envelope.Publishers.Contains(_parentId.Value.ToString()))
                {
                    await SendToParentAsync(envelope, ct);
                }
                else if (!_parentId.HasValue)
                {
                    // 没有父节点时也尝试发送（可能是根节点）
                    await SendToParentAsync(envelope, ct);
                }
                break;
                
            case EventDirection.Both:
                // 双向传播
                // Up方向同样需要检查是否已经在父节点stream中
                if (_parentId.HasValue && !envelope.Publishers.Contains(_parentId.Value.ToString()))
                {
                    await SendToParentAsync(envelope, ct);
                }
                else if (!_parentId.HasValue)
                {
                    await SendToParentAsync(envelope, ct);
                }
                await SendToChildrenAsync(envelope, ct);
                break;
        }
    }
    
    /// <summary>
    /// 处理 UpThenDown 事件（在根节点调用）
    /// </summary>
    [Obsolete("Use ContinuePropagationAsync instead")]
    public async Task HandleUpThenDownAtRootAsync(EventEnvelope envelope, CancellationToken ct)
    {
        // 保留以兼容，但标记为过时
        await ContinuePropagationAsync(envelope, ct);
    }
}
