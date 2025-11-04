using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Observability;
using Microsoft.Extensions.Logging;

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