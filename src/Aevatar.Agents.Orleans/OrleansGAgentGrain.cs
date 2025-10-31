using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Aevatar.Agents.Orleans;

/// <summary>
/// Orleans Grain 实现
/// 包装 GAgentBase 并提供事件路由
/// </summary>
public class OrleansGAgentGrain : Grain, IGAgentGrain, IEventPublisher
{
    private GAgentBase<object>? _agent;
    private IGrainFactory? _grainFactory;

    // 层级关系
    private Guid? _parentId;
    private readonly HashSet<Guid> _childrenIds = new();

    public Task<Guid> GetIdAsync()
    {
        return Task.FromResult(this.GetGrainId().GetGuidKey());
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _grainFactory = GrainFactory;
        return base.OnActivateAsync(cancellationToken);
    }

    /// <summary>
    /// 设置关联的 Agent（由 Factory 调用）
    /// </summary>
    public void SetAgent(GAgentBase<object> agent)
    {
        _agent = agent;
        _agent.SetEventPublisher(this);
    }

    // ============ 层级关系管理 ============

    public Task AddChildAsync(Guid childId)
    {
        _childrenIds.Add(childId);
        return Task.CompletedTask;
    }

    public Task RemoveChildAsync(Guid childId)
    {
        _childrenIds.Remove(childId);
        return Task.CompletedTask;
    }

    public Task SetParentAsync(Guid parentId)
    {
        _parentId = parentId;
        return Task.CompletedTask;
    }

    public Task ClearParentAsync()
    {
        _parentId = null;
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
        var eventId = Guid.NewGuid().ToString();

        var envelope = new EventEnvelope
        {
            Id = eventId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Version = 1,
            Payload = Any.Pack(evt),
            CorrelationId = Guid.NewGuid().ToString(),
            PublisherId = this.GetGrainId().GetGuidKey().ToString(),
            Direction = direction,
            ShouldStopPropagation = false,
            MaxHopCount = -1,
            CurrentHopCount = 0,
            MinHopCount = -1,
            Message = $"Published by {this.GetGrainId()}"
        };

        envelope.Publishers.Add(this.GetGrainId().GetGuidKey().ToString());

        await RouteEventAsync(envelope, ct);

        return eventId;
    }

    // ============ 事件路由 ============

    private async Task RouteEventAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (envelope.ShouldStopPropagation)
            return;

        if (envelope.MaxHopCount > 0 && envelope.CurrentHopCount >= envelope.MaxHopCount)
            return;

        switch (envelope.Direction)
        {
            case EventDirection.Up:
                await SendToParentAsync(envelope);
                break;

            case EventDirection.Down:
                await SendToChildrenAsync(envelope);
                break;

            case EventDirection.UpThenDown:
                await SendToParentAsync(envelope);
                break;

            case EventDirection.Bidirectional:
                await SendToParentAsync(envelope);
                await SendToChildrenAsync(envelope);
                break;
        }
    }

    private async Task SendToParentAsync(EventEnvelope envelope)
    {
        if (_parentId == null)
        {
            // 没有父节点，停止向上传播
            return;
        }

        var parentGrain = _grainFactory!.GetGrain<IGAgentGrain>(_parentId.Value);
        await parentGrain.HandleEventAsync(envelope.ToByteArray());
    }

    private async Task SendToChildrenAsync(EventEnvelope envelope)
    {
        foreach (var childId in _childrenIds)
        {
            var childEnvelope = envelope.Clone();
            childEnvelope.CurrentHopCount++;
            childEnvelope.Publishers.Add(this.GetGrainId().GetGuidKey().ToString());

            var childGrain = _grainFactory!.GetGrain<IGAgentGrain>(childId);
            await childGrain.HandleEventAsync(childEnvelope.ToByteArray());
        }
    }

    // ============ 事件处理 ============

    public async Task HandleEventAsync(byte[] envelopeBytes)
    {
        if (_agent == null)
        {
            throw new InvalidOperationException("Agent not set");
        }

        // 反序列化 EventEnvelope
        var envelope = EventEnvelope.Parser.ParseFrom(envelopeBytes);

        // 检查 MinHopCount
        var shouldProcess = envelope.MinHopCount <= 0 || envelope.CurrentHopCount >= envelope.MinHopCount;

        if (shouldProcess)
        {
            try
            {
                // 让 Agent 处理事件（使用反射调用）
                var handleMethod = _agent.GetType().GetMethod("HandleEventAsync");
                if (handleMethod != null)
                {
                    if (handleMethod.Invoke(_agent, [envelope, CancellationToken.None]) is Task task)
                    {
                        await task;
                    }
                }
            }
            catch (Exception ex)
            {
                var logger =
                    ServiceProvider?.GetService(typeof(ILogger<OrleansGAgentGrain>)) as ILogger<OrleansGAgentGrain>;
                logger?.LogError(ex, "Error handling event {EventId} in agent {AgentId}",
                    envelope.Id, this.GetGrainId().GetGuidKey());
            }
        }

        // 继续路由事件
        await ContinuePropagationAsync(envelope);
    }

    /// <summary>
    /// 继续传播事件
    /// </summary>
    private async Task ContinuePropagationAsync(EventEnvelope envelope)
    {
        if (envelope.ShouldStopPropagation)
            return;

        if (envelope.MaxHopCount > 0 && envelope.CurrentHopCount >= envelope.MaxHopCount)
            return;

        // 根据方向继续传播（只向下传播）
        if (envelope.Direction is EventDirection.Down or EventDirection.Bidirectional)
        {
            await SendToChildrenAsync(envelope);
        }
    }

    // ============ 生命周期 ============

    public async Task ActivateAsync()
    {
        if (_agent != null)
        {
            await _agent.OnActivateAsync();
        }
    }

    public async Task DeactivateAsync()
    {
        if (_agent != null)
        {
            await _agent.OnDeactivateAsync();
        }
    }
}