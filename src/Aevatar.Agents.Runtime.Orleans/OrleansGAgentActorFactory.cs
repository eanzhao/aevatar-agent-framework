using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans 运行时的 Agent Actor 工厂
/// </summary>
public class OrleansGAgentActorFactory : IGAgentActorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<OrleansGAgentActorFactory> _logger;
    private readonly IGAgentActorFactoryProvider? _factoryProvider;
    private readonly IStreamProvider? _streamProvider;

    public OrleansGAgentActorFactory(
        IServiceProvider serviceProvider,
        IClusterClient clusterClient,
        ILogger<OrleansGAgentActorFactory> logger)
    {
        _factoryProvider = serviceProvider.GetService<IGAgentActorFactoryProvider>();
        _serviceProvider = serviceProvider;
        _clusterClient = clusterClient;
        _streamProvider = clusterClient.GetStreamProvider(AevatarAgentsOrleansConstants.StreamProviderName);
        _logger = logger;
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

    public async Task<IGAgentActor> CreateActorForAgentAsync(IGAgent agent, Guid id, CancellationToken ct = default)
    {
        _logger.LogDebug("[Factory] Creating Orleans Actor for Agent - Type: {AgentType}, Id: {Id}",
            agent.GetType().Name, id);
    
        // 自动注入 Logger
        AgentLoggerInjector.InjectLogger(agent, _serviceProvider);
    
        // 自动注入 StateStore
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
    
        // 自动注入 ConfigurationStore
        AgentConfigurationInjector.InjectConfigurationStore(agent, _serviceProvider);
    
        // 自动注入 EventStore（如果 Agent 使用 EventSourcing）
        AgentEventStoreInjector.InjectEventStore(agent, _serviceProvider);
    
        // 使用标准 Grain (所有 Agent 都使用相同的 Grain)
        var grain = _clusterClient.GetGrain<IStandardGAgentGrain>(id.ToString());
        _logger.LogDebug("Using Standard Grain for agent {Id}", id);
    
        // 创建 Orleans Actor (继承自 GAgentActorBase!)
        var actor = new OrleansGAgentActor(agent, _clusterClient, _streamProvider, _logger);
    
        // 激活
        await actor.ActivateAsync(ct);
    
        _logger.LogInformation("Created and activated Orleans agent actor {Id} with grain type {GrainType}",
            id, grain.GetType().Name);
    
        return actor;
    }
}