using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Aevatar.Agents.Orleans;

/// <summary>
/// Orleans 运行时的 Agent Actor 工厂
/// </summary>
public class OrleansGAgentActorFactory : IGAgentActorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<OrleansGAgentActorFactory> _logger;

    public OrleansGAgentActorFactory(
        IServiceProvider serviceProvider,
        IGrainFactory grainFactory,
        ILogger<OrleansGAgentActorFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _grainFactory = grainFactory;
        _logger = logger;
    }

    public async Task<IGAgentActor> CreateAgentAsync<TAgent, TState>(Guid id, CancellationToken ct = default)
        where TAgent : IGAgent<TState>
        where TState : class, new()
    {
        _logger.LogDebug("Creating agent actor for type {AgentType} with id {Id}",
            typeof(TAgent).Name, id);

        // 创建本地 Agent 实例
        var agent = ActivatorUtilities.CreateInstance<TAgent>(_serviceProvider, id);

        // 获取 Orleans Grain
        var grain = _grainFactory.GetGrain<IGAgentGrain>(id.ToString());
        
        // 创建 Actor 包装器（持有本地 Agent 和远程 Grain）
        var actor = new OrleansGAgentActor(grain, agent);

        // 激活
        await actor.ActivateAsync(ct);

        _logger.LogInformation("Created and activated agent actor {Id}", id);

        return actor;
    }
}

