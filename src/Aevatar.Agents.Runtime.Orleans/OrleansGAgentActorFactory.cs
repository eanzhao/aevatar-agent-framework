using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Core.Helpers;
using Microsoft.CodeAnalysis.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans 运行时的 Agent Actor 工厂
/// </summary>
public class OrleansGAgentActorFactory : GAgentActorFactoryBase
{
    private readonly IClusterClient _clusterClient;
    private readonly IStreamProvider? _streamProvider;
    private readonly StreamingOptions _streamingOptions;

    public OrleansGAgentActorFactory(
        IServiceProvider serviceProvider,
        IClusterClient clusterClient,
        ILogger<OrleansGAgentActorFactory> logger)
        : base(serviceProvider, logger)
    {
        _clusterClient = clusterClient;

        // Get StreamingOptions from configuration (with fallback to default)
        _streamingOptions = serviceProvider.GetService<IOptions<StreamingOptions>>()?.Value
                            ?? new StreamingOptions();

        var streamProviderName = _streamingOptions.StreamProviderName;
        _streamProvider = clusterClient.GetStreamProvider(streamProviderName)
                          ?? throw new InvalidOperationException($"Stream provider '{streamProviderName}' not found");
    }

    protected override async Task<IGAgentActor> CreateActorForAgentAsync(IGAgent agent, Guid id,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[Factory] Creating Orleans Actor for Agent - Type: {AgentType}, Id: {Id}",
            agent.GetType().Name, id);

        // 使用标准 Grain (所有 Agent 都使用相同的 Grain)
        var grain = _clusterClient.GetGrain<IStandardGAgentGrain>(id.ToString());
        _logger.LogDebug("Using Standard Grain for agent {Id}", id);

        // 创建 Orleans Actor (继承自 GAgentActorBase!)
        var actor = new OrleansGAgentActor(agent, _clusterClient, _streamProvider, _streamingOptions);

        LoggerInjector.InjectLogger(actor, _serviceProvider);

        // 激活
        await actor.ActivateAsync(ct);

        _logger.LogInformation("Created and activated Orleans agent actor {Id} with grain type {GrainType}",
            id, grain.GetType().Name);

        return actor;
    }
}