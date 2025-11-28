using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.CQRS;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.CQRS;

/// <summary>
/// Stream forwarding implementation of IStateProjector.
/// Forwards state changes to a message stream using IMessageStreamProvider abstraction.
/// The actual stream implementation (Orleans Stream, MassTransit, etc.) is determined by DI configuration.
/// </summary>
public class StreamForwardingProjector : IStateProjector
{
    private readonly IMessageStreamProvider _streamProvider;
    private readonly ILogger<StreamForwardingProjector> _logger;

    /// <summary>
    /// Stream category for state projection.
    /// Consumers should subscribe to this category to receive state updates.
    /// </summary>
    public const string StateProjectionCategory = "StateProjection";

    public StreamForwardingProjector(
        IMessageStreamProvider streamProvider,
        ILogger<StreamForwardingProjector> logger)
    {
        _streamProvider = streamProvider;
        _logger = logger;
    }

    public async Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        try
        {
            // Parse agent ID to get stream
            if (!Guid.TryParse(wrapper.AgentId, out var agentId))
            {
                _logger.LogWarning(
                    "Invalid agent ID format: {AgentId}, cannot forward to stream",
                    wrapper.AgentId);
                return;
            }

            // Get stream using the abstraction
            // The actual implementation depends on DI configuration:
            // - LocalMessageStreamProvider (in-memory/Orleans)
            // - MassTransitMessageStreamProvider (RabbitMQ/Kafka)
            var stream = _streamProvider.GetStream(agentId, StateProjectionCategory);

            // Produce the state wrapper to the stream
            await stream.ProduceAsync(wrapper, ct);

            _logger.LogDebug(
                "Forwarded state to stream for {AgentId} (Type: {AgentType}, Version: {Version})",
                wrapper.AgentId, wrapper.AgentType, wrapper.Version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error forwarding state to stream for {AgentId}",
                wrapper.AgentId);
            throw;
        }
    }
}

/// <summary>
/// Composite projector that delegates to multiple projectors.
/// Use this when you need both direct ES projection and stream forwarding.
/// </summary>
public class CompositeStateProjector : IStateProjector
{
    private readonly IEnumerable<IStateProjector> _projectors;
    private readonly ILogger<CompositeStateProjector> _logger;

    public CompositeStateProjector(
        IEnumerable<IStateProjector> projectors,
        ILogger<CompositeStateProjector> logger)
    {
        _projectors = projectors;
        _logger = logger;
    }

    public async Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        var exceptions = new List<Exception>();

        foreach (var projector in _projectors)
        {
            try
            {
                await projector.ProjectAsync(wrapper, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error in projector {ProjectorType} for {AgentId}",
                    projector.GetType().Name, wrapper.AgentId);
                exceptions.Add(ex);
            }
        }

        if (exceptions.Count > 0)
        {
            throw new AggregateException(
                $"One or more projectors failed for {wrapper.AgentId}",
                exceptions);
        }
    }
}

/// <summary>
/// Logging projector for debugging and development.
/// Simply logs state changes without persisting them.
/// </summary>
public class LoggingStateProjector : IStateProjector
{
    private readonly ILogger<LoggingStateProjector> _logger;

    public LoggingStateProjector(ILogger<LoggingStateProjector> logger)
    {
        _logger = logger;
    }

    public Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[State Projected] AgentId: {AgentId}, Type: {AgentType}, Version: {Version}, PublishedAt: {PublishedAt}",
            wrapper.AgentId,
            wrapper.AgentType,
            wrapper.Version,
            wrapper.PublishedAt?.ToDateTime());

        return Task.CompletedTask;
    }
}

