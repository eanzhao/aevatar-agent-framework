using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// ProtoActor implementation of an agent instance that wraps a Proto.Actor PID.
/// </summary>
public class ProtoActorAgentInstance : IAgentInstance
{
    private readonly ProtoActorGAgentActor _actor;
    private readonly IRootContext _rootContext;
    private readonly PID _pid;
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
    /// Initializes a new instance of the <see cref="ProtoActorAgentInstance"/> class.
    /// </summary>
    /// <param name="agentId">The unique identifier of the agent.</param>
    /// <param name="agentTypeName">The type name of the agent.</param>
    /// <param name="actor">The underlying ProtoActor agent actor.</param>
    /// <param name="rootContext">The root context for actor operations.</param>
    /// <param name="pid">The process ID of the actor.</param>
    /// <param name="logger">The logger instance.</param>
    public ProtoActorAgentInstance(
        Guid agentId,
        string agentTypeName,
        ProtoActorGAgentActor actor,
        IRootContext rootContext,
        PID pid,
        ILogger? logger = null)
    {
        AgentId = agentId;
        AgentTypeName = agentTypeName ?? throw new ArgumentNullException(nameof(agentTypeName));
        RuntimeId = $"protoactor-{agentId}";
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        _rootContext = rootContext ?? throw new ArgumentNullException(nameof(rootContext));
        _pid = pid ?? throw new ArgumentNullException(nameof(pid));
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
        _logger.LogDebug("ProtoActor agent {AgentId} initialized", AgentId);
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
            _logger.LogDebug("Publishing event {EventId} to ProtoActor agent {AgentId}", 
                envelope.Id, AgentId);
            
            // Send the event to the actor through its PID
            await _rootContext.RequestAsync<bool>(_pid, envelope, CancellationToken.None);
            
            _eventsProcessed++;
            _lastActivityTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event {EventId} to ProtoActor agent {AgentId}",
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
            // This would need to be implemented through actor messages
            _logger.LogWarning("GetStateAsync is not yet implemented for ProtoActor agents");
            await Task.CompletedTask;
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get state for ProtoActor agent {AgentId}", AgentId);
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
            // This would need to be implemented through actor messages
            _logger.LogWarning("SetStateAsync is not yet implemented for ProtoActor agents");
            await Task.CompletedTask;
            _lastActivityTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set state for ProtoActor agent {AgentId}", AgentId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeactivateAsync()
    {
        _logger.LogInformation("Deactivating ProtoActor agent {AgentId}", AgentId);

        try
        {
            _isActive = false;
            
            // Stop the actor
            await _rootContext.StopAsync(_pid);
            
            _logger.LogInformation("ProtoActor agent {AgentId} deactivated", AgentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating ProtoActor agent {AgentId}", AgentId);
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

    /// <summary>
    /// Gets the underlying ProtoActor PID.
    /// </summary>
    public PID GetPid() => _pid;

    /// <summary>
    /// Gets the underlying ProtoActor agent actor.
    /// </summary>
    public ProtoActorGAgentActor GetActor() => _actor;
}
