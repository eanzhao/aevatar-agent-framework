using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime;
using Microsoft.Extensions.Logging;
using Google.Protobuf;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Local implementation of an agent instance that wraps a LocalGAgentActor.
/// </summary>
public class LocalAgentInstance : IAgentInstance
{
    private readonly IGAgentActor _actor;
    private readonly ILogger _logger;
    private readonly DateTime _activationTime;
    private DateTime _lastActivityTime;
    private long _eventsProcessed;
    private bool _isActive;

    /// <inheritdoc />
    public Guid AgentId { get; }

    /// <inheritdoc />
    public string RuntimeId { get; }

    /// <inheritdoc />
    public string AgentTypeName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalAgentInstance"/> class.
    /// </summary>
    /// <param name="agentId">The unique identifier of the agent.</param>
    /// <param name="agentTypeName">The type name of the agent.</param>
    /// <param name="actor">The underlying LocalGAgentActor.</param>
    /// <param name="logger">The logger instance.</param>
    public LocalAgentInstance(
        Guid agentId,
        string agentTypeName,
        IGAgentActor actor,
        ILogger? logger = null)
    {
        AgentId = agentId;
        AgentTypeName = agentTypeName ?? throw new ArgumentNullException(nameof(agentTypeName));
        RuntimeId = $"local-{agentId}";
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        _activationTime = DateTime.UtcNow;
        _lastActivityTime = DateTime.UtcNow;
        _isActive = false;
        _eventsProcessed = 0;
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _isActive = true;
        _logger.LogDebug("Agent {AgentId} initialized", AgentId);
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task PublishEventAsync(EventEnvelope envelope)
    {
        if (!_isActive)
        {
            throw new InvalidOperationException($"Agent {AgentId} is not active");
        }

        if (envelope == null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        try
        {
            _logger.LogDebug("Publishing event {EventId} to agent {AgentId}", 
                envelope.Id, AgentId);
            
            await _actor.PublishEventAsync(envelope);
            _eventsProcessed++;
            _lastActivityTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event {EventId} to agent {AgentId}",
                envelope.Id, AgentId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IMessage?> GetStateAsync()
    {
        try
        {
            // For now, return null as we don't have direct access to the agent's state
            // This would need to be implemented through the actor's public methods
            await Task.CompletedTask;
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get state for agent {AgentId}", AgentId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SetStateAsync(IMessage state)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        try
        {
            // For now, this is a no-op as we don't have direct access to set the agent's state
            // This would need to be implemented through the actor's public methods
            await Task.CompletedTask;
            _lastActivityTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set state for agent {AgentId}", AgentId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeactivateAsync()
    {
        _logger.LogInformation("Deactivating agent {AgentId}", AgentId);

        try
        {
            _isActive = false;
            await _actor.DeactivateAsync();
            _logger.LogInformation("Agent {AgentId} deactivated", AgentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating agent {AgentId}", AgentId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<AgentMetadata> GetMetadataAsync()
    {
        var parentId = await _actor.GetParentAsync();
        var children = await _actor.GetChildrenAsync();
        
        return new AgentMetadata
        {
            ActivationTime = _activationTime,
            LastActivityTime = _lastActivityTime,
            EventsProcessed = _eventsProcessed,
            IsActive = _isActive,
            ParentAgentId = parentId,
            ChildAgentIds = children?.ToList() ?? new List<Guid>()
        };
    }

}
