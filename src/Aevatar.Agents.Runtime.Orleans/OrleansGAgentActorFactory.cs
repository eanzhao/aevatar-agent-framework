using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans 运行时的 Agent Actor 工厂
/// </summary>
public class OrleansGAgentActorFactory : IGAgentActorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<OrleansGAgentActorFactory> _logger;
    private readonly OrleansGAgentActorFactoryOptions _options;
    private readonly IGAgentActorFactoryProvider? _factoryProvider;

    public OrleansGAgentActorFactory(
        IServiceProvider serviceProvider,
        IGrainFactory grainFactory,
        ILogger<OrleansGAgentActorFactory> logger,
        IOptions<OrleansGAgentActorFactoryOptions>? options = null)
    {
        _serviceProvider = serviceProvider;
        _grainFactory = grainFactory;
        _logger = logger;
        _options = options?.Value ?? new OrleansGAgentActorFactoryOptions();
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
        _logger.LogDebug("[Factory] Creating Orleans Actor for Agent - Type: {AgentType}, Id: {Id}", 
            agent.GetType().Name, id);
        
        // 自动注入 Logger
        AgentLoggerInjector.InjectLogger(agent, _serviceProvider);

        // 根据配置获取适当的 Grain
        IGAgentGrain grain;
        
        // 根据配置决定是否使用事件溯源
        if (_options.UseEventSourcing)
        {
            if (_options.UseJournaledGrain)
            {
                // 使用 Journaled Grain
                grain = _grainFactory.GetGrain<IJournaledGAgentGrain>(id.ToString());
                _logger.LogDebug("Using Journaled Grain for agent {Id}", id);
            }
            else
            {
                // 使用标准事件溯源 Grain
                grain = _grainFactory.GetGrain<IEventSourcingGAgentGrain>(id.ToString());
                _logger.LogDebug("Using EventSourcing Grain for agent {Id}", id);
            }
        }
        else
        {
            // 使用标准 Grain
            grain = _grainFactory.GetGrain<IStandardGAgentGrain>(id.ToString());
            _logger.LogDebug("Using Standard Grain for agent {Id}", id);
        }
        
        // 创建 Actor 包装器（持有本地 Agent 和远程 Grain）
        var actor = new OrleansGAgentActor(grain, agent);

        // 激活
        await actor.ActivateAsync(ct);

        _logger.LogInformation("Created and activated Orleans agent actor {Id} with grain type {GrainType}", 
            id, grain.GetType().Name);

        return actor;
    }
}

