using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core.Helpers;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// Proto.Actor 运行时的 Agent Actor 工厂
/// </summary>
public class ProtoActorGAgentActorFactory : GAgentActorFactoryBase
{
    private readonly ActorSystem _actorSystem;
    private readonly ProtoActorMessageStreamRegistry _streamRegistry;

    public ProtoActorGAgentActorFactory(
        IServiceProvider serviceProvider,
        ActorSystem actorSystem,
        ILogger<ProtoActorGAgentActorFactory> logger)
        : base(serviceProvider, logger)
    {
        _actorSystem = actorSystem;
        _streamRegistry = new ProtoActorMessageStreamRegistry(actorSystem.Root);
    }

    /// <summary>
    /// 为已存在的 Agent 实例创建 Actor（内部方法，供自动发现使用）
    /// </summary>
    public override async Task<IGAgentActor> CreateActorForAgentAsync(IGAgent agent, Guid id,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[Factory] Creating ProtoActor Actor for Agent - Type: {AgentType}, Id: {Id}",
            agent.GetType().Name, id);

        // 执行通用依赖注入
        InjectCommonDependencies(agent);

        // 创建 ProtoActor Actor
        var props = Props.FromProducer(() => new AgentActor());
        var actorPid = _actorSystem.Root.Spawn(props);

        var actor = new ProtoActorGAgentActor(
            agent,
            _actorSystem.Root,
            actorPid,
            _streamRegistry);

        // 激活
        await actor.ActivateAsync(ct);

        _logger.LogInformation("Created and activated ProtoActor agent actor {Id}", id);

        return actor;
    }
}