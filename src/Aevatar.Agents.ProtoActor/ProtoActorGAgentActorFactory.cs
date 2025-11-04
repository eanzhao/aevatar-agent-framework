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
}