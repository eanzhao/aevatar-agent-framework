using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions.CQRS;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Plugins.MassTransit.CQRS;

/// <summary>
/// MassTransit Consumer for StateProjectionMessage.
/// Receives state changes from RabbitMQ and projects them via IStateProjector.
/// </summary>
public class StateProjectionConsumer : IConsumer<StateProjectionMessage>
{
    private readonly IEnumerable<IStateProjector> _projectors;
    private readonly ILogger<StateProjectionConsumer> _logger;

    public StateProjectionConsumer(
        IEnumerable<IStateProjector> projectors,
        ILogger<StateProjectionConsumer> logger)
    {
        _projectors = projectors ?? throw new ArgumentNullException(nameof(projectors));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<StateProjectionMessage> context)
    {
        var message = context.Message;

        _logger.LogDebug(
            "Received StateProjectionMessage: AgentId={AgentId}, Type={AgentType}, Version={Version}",
            message.AgentId, message.AgentType, message.Version);

        // Convert to StateWrapper for projectors
        var wrapper = new StateWrapper
        {
            AgentId = message.AgentId,
            AgentType = message.AgentType,
            StateData = message.StateData,
            Version = message.Version,
            PublishedAt = message.PublishedAt
        };

        // Project to all registered projectors
        foreach (var projector in _projectors)
        {
            try
            {
                await projector.ProjectAsync(wrapper);

                _logger.LogDebug(
                    "Projected state via {ProjectorType}: AgentId={AgentId}",
                    projector.GetType().Name, message.AgentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Projector {ProjectorType} failed for AgentId={AgentId}",
                    projector.GetType().Name, message.AgentId);

                // Continue with other projectors even if one fails
            }
        }
    }
}

