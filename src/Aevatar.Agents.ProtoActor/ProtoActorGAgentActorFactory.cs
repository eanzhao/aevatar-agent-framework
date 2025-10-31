using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.ProtoActor;

/// <summary>
/// Proto.Actor 运行时的 Agent Actor 工厂
/// </summary>
public class ProtoActorGAgentActorFactory : IGAgentActorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProtoActorGAgentActorFactory> _logger;
    private readonly ActorSystem _actorSystem;
    private readonly ProtoActorMessageStreamRegistry _streamRegistry;

    public ProtoActorGAgentActorFactory(
        IServiceProvider serviceProvider,
        ActorSystem actorSystem,
        ILogger<ProtoActorGAgentActorFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _actorSystem = actorSystem;
        _logger = logger;
        _streamRegistry = new ProtoActorMessageStreamRegistry(actorSystem.Root);
    }

    public async Task<IGAgentActor> CreateAgentAsync<TAgent, TState>(Guid id, CancellationToken ct = default)
        where TAgent : IGAgent<TState>
        where TState : class, new()
    {
        _logger.LogDebug("Creating agent actor for type {AgentType} with id {Id}",
            typeof(TAgent).Name, id);

        // 检查是否已存在
        if (_streamRegistry.Exists(id))
        {
            throw new InvalidOperationException($"Agent with id {id} already exists");
        }

        // 创建 Agent 实例
        var agent = ActivatorUtilities.CreateInstance<TAgent>(_serviceProvider, id);

        // 创建 Proto.Actor Actor
        var props = Props.FromProducer(() => new AgentActor());
        var pid = _actorSystem.Root.Spawn(props);

        // 创建 ProtoActorGAgentActor 包装器（使用 Stream）
        var gagentActor = new ProtoActorGAgentActor(
            agent,
            _actorSystem.Root,
            pid,
            _streamRegistry,
            _serviceProvider.GetService<ILogger<ProtoActorGAgentActor>>()
        );

        // 设置 GAgentActor 到 Proto.Actor Actor
        _actorSystem.Root.Send(pid, new SetGAgentActor { GAgentActor = gagentActor });

        // 激活
        await gagentActor.ActivateAsync(ct);

        _logger.LogInformation("Created and activated agent actor {Id}", id);

        return gagentActor;
    }
}

