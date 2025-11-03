using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.ProtoActor;

/// <summary>
/// Proto.Actor 运行时的 Agent Actor 实现
/// 使用 ProtoActorMessageStream 作为消息传输机制
/// </summary>
public class ProtoActorGAgentActor : GAgentActorBase
{
    private readonly IRootContext _rootContext;
    private readonly PID _actorPid;
    private readonly ProtoActorMessageStreamRegistry _streamRegistry;
    private readonly ProtoActorMessageStream _myStream; // 这个 Actor 的 Stream

    public ProtoActorGAgentActor(
        IGAgent agent,
        IRootContext rootContext,
        PID actorPid,
        ProtoActorMessageStreamRegistry streamRegistry,
        ILogger? logger = null)
        : base(agent, logger)
    {
        _rootContext = rootContext ?? throw new ArgumentNullException(nameof(rootContext));
        _actorPid = actorPid ?? throw new ArgumentNullException(nameof(actorPid));
        _streamRegistry = streamRegistry ?? throw new ArgumentNullException(nameof(streamRegistry));

        // 注册 PID 并获取 Stream
        _streamRegistry.RegisterPid(agent.Id, actorPid);
        _myStream = _streamRegistry.GetStream(agent.Id)!;
    }

    /// <summary>
    /// 获取 Proto.Actor PID
    /// </summary>
    public PID GetPid() => _actorPid;

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

        // 停止 Proto.Actor
        _rootContext.Send(_actorPid, new Stop());
        
        // 从 Registry 中移除 Agent，以允许后续重新创建相同 ID 的 Actor
        _streamRegistry.Remove(Id);
        
        Logger.LogDebug("Agent {AgentId} removed from registry", Id);
    }
}