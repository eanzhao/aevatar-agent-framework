using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core.Helpers;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Factory;

/// <summary>
/// GAgent Actor 工厂的抽象基类，包含通用逻辑
/// </summary>
public abstract class GAgentActorFactoryBase : IGAgentActorFactory
{
    protected readonly IServiceProvider _serviceProvider;
    protected readonly ILogger _logger;
    protected readonly IGAgentActorFactoryProvider? _factoryProvider;
    protected readonly IGAgentFactory? _agentFactory;

    protected GAgentActorFactoryBase(
        IServiceProvider serviceProvider,
        ILogger logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _factoryProvider = serviceProvider.GetService<IGAgentActorFactoryProvider>();
        _agentFactory = serviceProvider.GetService<IGAgentFactory>();
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

    public abstract Task<IGAgentActor> CreateActorForAgentAsync(IGAgent agent, Guid id, CancellationToken ct = default);
}