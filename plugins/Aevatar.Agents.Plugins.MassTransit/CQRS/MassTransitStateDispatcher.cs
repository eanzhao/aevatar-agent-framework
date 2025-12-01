using System;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions.CQRS;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Plugins.MassTransit.CQRS;

/// <summary>
/// MassTransit implementation of IStateDispatcher.
/// Publishes state changes to RabbitMQ/other message brokers via MassTransit.
/// </summary>
public class MassTransitStateDispatcher : IStateDispatcher
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<MassTransitStateDispatcher> _logger;
    private readonly MassTransitStateDispatcherOptions _options;

    public MassTransitStateDispatcher(
        IPublishEndpoint publishEndpoint,
        ILogger<MassTransitStateDispatcher> logger,
        IOptions<MassTransitStateDispatcherOptions>? options = null)
    {
        _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new MassTransitStateDispatcherOptions();
    }

    public async Task PublishAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        try
        {
            // Create the message to publish
            var message = new StateProjectionMessage
            {
                AgentId = wrapper.AgentId,
                AgentType = wrapper.AgentType,
                StateData = wrapper.StateData,
                Version = wrapper.Version,
                PublishedAt = wrapper.PublishedAt,
                CorrelationId = Guid.NewGuid().ToString()
            };

            // Publish to MassTransit
            await _publishEndpoint.Publish(message, ctx =>
            {
                // Set message headers for routing
                ctx.Headers.Set("AgentType", wrapper.AgentType);
                ctx.Headers.Set("Version", wrapper.Version.ToString());

                // Set message TTL if configured
                if (_options.MessageTtlSeconds > 0)
                {
                    ctx.TimeToLive = TimeSpan.FromSeconds(_options.MessageTtlSeconds);
                }
            }, ct);

            _logger.LogDebug(
                "Published state to MassTransit: AgentId={AgentId}, Type={AgentType}, Version={Version}",
                wrapper.AgentId, wrapper.AgentType, wrapper.Version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish state via MassTransit: AgentId={AgentId}, Type={AgentType}",
                wrapper.AgentId, wrapper.AgentType);
            throw;
        }
    }
}

/// <summary>
/// Message published to MassTransit for state projection
/// </summary>
public class StateProjectionMessage
{
    public string AgentId { get; set; } = string.Empty;
    public string AgentType { get; set; } = string.Empty;
    public Google.Protobuf.WellKnownTypes.Any? StateData { get; set; }
    public long Version { get; set; }
    public Google.Protobuf.WellKnownTypes.Timestamp? PublishedAt { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Configuration options for MassTransitStateDispatcher
/// </summary>
public class MassTransitStateDispatcherOptions
{
    /// <summary>
    /// Message time-to-live in seconds. 0 = no expiration.
    /// </summary>
    public int MessageTtlSeconds { get; set; } = 0;

    /// <summary>
    /// Enable message deduplication by AgentId+Version
    /// </summary>
    public bool EnableDeduplication { get; set; } = true;
}

