using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.Logging;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans 运行时的 Agent Actor 工厂
/// </summary>
public class OrleansGAgentActorFactory : GAgentActorFactoryBase
{
    private readonly IClusterClient _clusterClient;
    private readonly IStreamProvider? _streamProvider;

    public OrleansGAgentActorFactory(
        IServiceProvider serviceProvider,
        IClusterClient clusterClient,
        ILogger<OrleansGAgentActorFactory> logger)
        : base(serviceProvider, logger)
    {
        _clusterClient = clusterClient;
        _streamProvider = clusterClient.GetStreamProvider(AevatarAgentsOrleansConstants.StreamProviderName);
    }

    public override async Task<IGAgentActor> CreateActorForAgentAsync(IGAgent agent, Guid id,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[Factory] Creating Orleans Actor for Agent - Type: {AgentType}, Id: {Id}",
            agent.GetType().Name, id);

        // 使用标准 Grain (所有 Agent 都使用相同的 Grain)
        var grain = _clusterClient.GetGrain<IStandardGAgentGrain>(id.ToString());
        _logger.LogDebug("Using Standard Grain for agent {Id}", id);

        // 创建 Orleans Actor (继承自 GAgentActorBase!)
        var actor = new OrleansGAgentActor(agent, _clusterClient, _streamProvider);

        // 激活
        await actor.ActivateAsync(ct);

        _logger.LogInformation("Created and activated Orleans agent actor {Id} with grain type {GrainType}",
            id, grain.GetType().Name);

        return actor;
    }
}