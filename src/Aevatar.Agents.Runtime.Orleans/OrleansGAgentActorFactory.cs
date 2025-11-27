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
    private readonly IMessageStreamProvider? _messageStreamProvider;
    private readonly IOptions<MessageStreamProviderOptions>? _providerOptions;

    public OrleansGAgentActorFactory(
        IServiceProvider serviceProvider,
        IClusterClient clusterClient,
        ILogger<OrleansGAgentActorFactory> logger,
        IMessageStreamProvider? messageStreamProvider = null,
        IOptions<MessageStreamProviderOptions>? providerOptions = null)
        : base(serviceProvider, logger)
    {
        _clusterClient = clusterClient;
        _messageStreamProvider = messageStreamProvider;
        _providerOptions = providerOptions;

        // Get StreamingOptions from configuration (with fallback to default)
        _streamingOptions = serviceProvider.GetService<IOptions<StreamingOptions>>()?.Value
                            ?? new StreamingOptions();

        // Orleans Stream Provider (依然需要初始化，作为 fallback 或默认)
        var streamProviderName = _streamingOptions.StreamProviderName;
        try 
        {
            _streamProvider = clusterClient.GetStreamProvider(streamProviderName);
        }
        catch (Exception ex)
        {
            // 如果配置了 MassTransit，Orleans Stream Provider 失败可能是预期的
            // 只有在没配置 MassTransit 时才抛出异常
            if (messageStreamProvider == null || providerOptions?.Value.Provider != "MassTransit")
            {
                logger.LogWarning(ex, "Stream provider '{StreamProviderName}' not found", streamProviderName);
            }
        }
    }

    protected override Task<IGAgentActor> CreateActorInstanceAsync(IGAgent agent, Guid id,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[Factory] Creating Orleans Actor for Agent - Type: {AgentType}, Id: {Id}",
            agent.GetType().Name, id);

        // 使用标准 Grain (所有 Agent 都使用相同的 Grain)
        var grain = _clusterClient.GetGrain<IGAgentGrain>(id.ToString());
        _logger.LogDebug("Using Standard Grain for agent {Id}", id);

        // 创建 Orleans Actor
        // 传入所有必要的依赖，包括可选的 MessageStreamProvider
        var actor = new OrleansGAgentActor(
            agent, 
            _clusterClient, // IGrainFactory
            _streamProvider!, // IStreamProvider (可能为 null，但在 Actor 中会检查)
            _streamingOptions,
            _serviceProvider.GetRequiredService<ILogger<OrleansGAgentActor>>(),
            _messageStreamProvider,
            _providerOptions);

        _logger.LogInformation("Created Orleans agent actor instance {Id} with grain type {GrainType}",
            id, grain.GetType().Name);

        return Task.FromResult<IGAgentActor>(actor);
    }
}
