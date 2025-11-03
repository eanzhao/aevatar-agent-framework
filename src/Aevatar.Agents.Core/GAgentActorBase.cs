using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.EventRouting;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.Core;

/// <summary>
/// Agent Actor 基类
/// 提供事件传播逻辑的标准实现
/// </summary>
public abstract class GAgentActorBase : IGAgentActor, IEventPublisher
{
    // ============ 字段 ============

    protected readonly IGAgent Agent;
    protected readonly ILogger Logger;
    protected readonly EventRouter EventRouter;

    // ============ 构造函数 ============

    protected GAgentActorBase(IGAgent agent, ILogger? logger = null)
    {
        Agent = agent ?? throw new ArgumentNullException(nameof(agent));
        Logger = logger ?? NullLogger.Instance;

        // 创建事件路由器
        EventRouter = new EventRouter(
            agent.Id,
            SendEventToActorAsync,
            SendToSelfAsync,
            logger
        );

        // 设置 Agent 的 EventPublisher
        var setPublisherMethod = agent.GetType().GetMethod("SetEventPublisher");
        setPublisherMethod?.Invoke(agent, new object[] { this });
    }

    // ============ IGAgentActor 实现 ============

    public Guid Id => Agent.Id;

    public IGAgent GetAgent() => Agent;

    // ============ 层级关系管理 ============

    public virtual Task AddChildAsync(Guid childId, CancellationToken ct = default)
    {
        EventRouter.AddChild(childId);
        return Task.CompletedTask;
    }

    public virtual Task RemoveChildAsync(Guid childId, CancellationToken ct = default)
    {
        EventRouter.RemoveChild(childId);
        return Task.CompletedTask;
    }

    public virtual Task SetParentAsync(Guid parentId, CancellationToken ct = default)
    {
        EventRouter.SetParent(parentId);
        return Task.CompletedTask;
    }

    public virtual Task ClearParentAsync(CancellationToken ct = default)
    {
        EventRouter.ClearParent();
        return Task.CompletedTask;
    }

    public virtual Task<IReadOnlyList<Guid>> GetChildrenAsync()
    {
        return Task.FromResult(EventRouter.GetChildren());
    }

    public virtual Task<Guid?> GetParentAsync()
    {
        return Task.FromResult(EventRouter.GetParent());
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
        // 使用 EventRouter 创建 EventEnvelope
        var envelope = EventRouter.CreateEventEnvelope(evt, direction);

        Logger.LogDebug("Agent {AgentId} publishing event {EventId} with direction {Direction}",
            Id, envelope.Id, direction);

        // 通过 EventRouter 路由事件
        await EventRouter.RouteEventAsync(envelope, ct);

        return envelope.Id;
    }


    /// <summary>
    /// 处理接收到的事件（标准流程）
    /// </summary>
    public virtual async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        Logger.LogDebug("Agent {AgentId} handling event {EventId}", Id, envelope.Id);

        // 使用 EventRouter 检查是否应该处理事件
        if (!EventRouter.ShouldProcessEvent(envelope))
            return;

        // 处理事件
        await ProcessEventAsync(envelope, ct);

        // 判断是否需要继续传播
        // 如果是自己发布的事件（PublisherId == Id），则已经在 RouteEventAsync 中传播过了
        // 如果是收到的事件（PublisherId != Id），则需要继续传播
        bool isInitialPublisher = envelope.PublisherId == Id.ToString();

        if (!isInitialPublisher)
        {
            // 收到的事件，需要继续传播（递归传播）
            Logger.LogDebug("Agent {AgentId} continuing propagation of event {EventId} from {PublisherId}",
                Id, envelope.Id, envelope.PublisherId);
            await EventRouter.ContinuePropagationAsync(envelope, ct);
        }
    }

    /// <summary>
    /// 处理事件（调用 Agent 的处理器）
    /// </summary>
    protected virtual async Task ProcessEventAsync(EventEnvelope envelope, CancellationToken ct)
    {
        try
        {
            // 让 Agent 处理事件
            var handleMethod = Agent.GetType().GetMethod("HandleEventAsync");
            if (handleMethod != null)
            {
                var task = handleMethod.Invoke(Agent, new object[] { envelope, ct }) as Task;
                if (task != null)
                {
                    await task;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling event {EventId} in agent {AgentId}",
                envelope.Id, Id);
        }
    }

    // ============ 抽象方法（子类实现） ============

    /// <summary>
    /// 发送事件给自己（子类实现具体的传输机制）
    /// </summary>
    protected abstract Task SendToSelfAsync(EventEnvelope envelope, CancellationToken ct);

    /// <summary>
    /// 发送事件到指定的 Actor（子类实现具体的传输机制）
    /// </summary>
    protected abstract Task SendEventToActorAsync(Guid actorId, EventEnvelope envelope, CancellationToken ct);

    /// <summary>
    /// 激活 Actor
    /// </summary>
    public abstract Task ActivateAsync(CancellationToken ct = default);

    /// <summary>
    /// 停用 Actor
    /// </summary>
    public abstract Task DeactivateAsync(CancellationToken ct = default);
}