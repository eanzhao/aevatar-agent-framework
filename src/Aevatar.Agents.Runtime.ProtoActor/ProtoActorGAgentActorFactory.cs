using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// Proto.Actor 运行时的 Agent Actor 工厂
/// </summary>
public class ProtoActorGAgentActorFactory : IGAgentActorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProtoActorGAgentActorFactory> _logger;
    private readonly ActorSystem _actorSystem;
    private readonly ProtoActorMessageStreamRegistry _streamRegistry;
    private readonly IGAgentActorFactoryProvider? _factoryProvider;

    public ProtoActorGAgentActorFactory(
        IServiceProvider serviceProvider,
        ActorSystem actorSystem,
        ILogger<ProtoActorGAgentActorFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _actorSystem = actorSystem;
        _logger = logger;
        _streamRegistry = new ProtoActorMessageStreamRegistry(actorSystem.Root);
        _factoryProvider = serviceProvider.GetService<IGAgentActorFactoryProvider>();
    }

    public async Task<IGAgentActor> CreateGAgentActorAsync<TAgent>(Guid id, CancellationToken ct = default)
        where TAgent : IGAgent
    {
        if (_factoryProvider == null)
        {
            throw new InvalidOperationException(
                $"No IGAgentActorFactoryProvider registered. Please register an agent factory for type {typeof(TAgent).Name}");
        }

        var agentType = typeof(TAgent);
        var factory = _factoryProvider.GetFactory(agentType);
        
        if (factory == null)
        {
            throw new InvalidOperationException(
                $"No factory found for agent type {agentType.Name}. Please ensure the agent is properly registered.");
        }

        _logger.LogDebug("Creating agent actor using factory for type {AgentType} with id {Id}",
            agentType.Name, id);
        
        return await factory(this, id, ct);
    }
    
    /// <summary>
    /// 为已存在的 Agent 实例创建 Actor（内部方法，供自动发现使用）
    /// </summary>
    public async Task<IGAgentActor> CreateActorForAgentAsync(IGAgent agent, Guid id, CancellationToken ct = default)
    {
        _logger.LogDebug("[Factory] Creating ProtoActor Actor for Agent - Type: {AgentType}, Id: {Id}", 
            agent.GetType().Name, id);
        
        // 自动注入 Logger
        AgentLoggerInjector.InjectLogger(agent, _serviceProvider);

        // 创建 ProtoActor Actor
        var props = Props.FromProducer(() => new AgentActor());
        var actorPid = _actorSystem.Root.Spawn(props);
        
        var actor = new ProtoActorGAgentActor(
            agent, 
            _actorSystem.Root,
            actorPid,
            _streamRegistry,
            _serviceProvider.GetService<ILogger<ProtoActorGAgentActor>>());
        
        // 激活
        await actor.ActivateAsync(ct);

        _logger.LogInformation("Created and activated ProtoActor agent actor {Id}", id);

        return actor;
    }
}