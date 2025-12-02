using Aevatar.Agents;
using Aevatar.Agents.Abstractions.CQRS;
using Microsoft.Extensions.Logging;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans.CQRS;

/// <summary>
/// Orleans implementation of IStateDispatcher.
/// Publishes state changes to Orleans streams for CQRS projection.
/// </summary>
public class OrleansStateDispatcher : IStateDispatcher
{
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<OrleansStateDispatcher> _logger;
    private readonly StateDispatcherOptions _options;
    private IStreamProvider? _streamProvider;

    public OrleansStateDispatcher(
        IClusterClient clusterClient,
        ILogger<OrleansStateDispatcher> logger,
        StateDispatcherOptions? options = null)
    {
        _clusterClient = clusterClient;
        _logger = logger;
        _options = options ?? new StateDispatcherOptions();
    }

    private IStreamProvider StreamProvider
    {
        get
        {
            _streamProvider ??= _clusterClient.GetStreamProvider(_options.StreamProviderName);
            return _streamProvider;
        }
    }

    public async Task PublishAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        try
        {
            // Create stream ID based on agent type for partitioning
            var streamId = StreamId.Create(
                _options.StreamNamespace,
                GetStreamKey(wrapper));

            var stream = StreamProvider.GetStream<StateWrapper>(streamId);

            await stream.OnNextAsync(wrapper);

            _logger.LogDebug(
                "Published state for agent {AgentId} to stream {StreamId}",
                wrapper.AgentId, streamId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish state for agent {AgentId}",
                wrapper.AgentId);
            throw;
        }
    }

    private string GetStreamKey(StateWrapper wrapper)
    {
        // Use agent type as stream key for partitioning by type
        // This allows different projectors to subscribe to specific agent types
        return wrapper.AgentType;
    }
}

/// <summary>
/// Configuration options for OrleansStateDispatcher
/// </summary>
public class StateDispatcherOptions
{
    /// <summary>
    /// Orleans stream provider name
    /// </summary>
    public string StreamProviderName { get; set; } = "Default";

    /// <summary>
    /// Stream namespace for state projection
    /// </summary>
    public string StreamNamespace { get; set; } = "StateProjection";
}

