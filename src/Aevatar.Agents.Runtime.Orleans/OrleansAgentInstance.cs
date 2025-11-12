using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans implementation of an agent instance that wraps an Orleans grain.
/// </summary>
public class OrleansAgentInstance : IAgentInstance
{
    private readonly IGAgentGrain _grain;
    private readonly ILogger _logger;
    private readonly DateTime _activationTime;
    private DateTime _lastActivityTime;
    private long _eventsProcessed;

    /// <inheritdoc />
    public Guid AgentId { get; }

    /// <inheritdoc />
    public string RuntimeId { get; }

    /// <inheritdoc />
    public string AgentTypeName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OrleansAgentInstance"/> class.
    /// </summary>
    /// <param name="agentId">The unique identifier of the agent.</param>
    /// <param name="agentTypeName">The type name of the agent.</param>
    /// <param name="grain">The underlying Orleans grain.</param>
    /// <param name="logger">The logger instance.</param>
    public OrleansAgentInstance(
        Guid agentId,
        string agentTypeName,
        IGAgentGrain grain,
        ILogger? logger = null)
    {
        AgentId = agentId;
        AgentTypeName = agentTypeName ?? throw new ArgumentNullException(nameof(agentTypeName));
        RuntimeId = $"orleans-{agentId}";
        _grain = grain ?? throw new ArgumentNullException(nameof(grain));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        _activationTime = DateTime.UtcNow;
        _lastActivityTime = DateTime.UtcNow;
        _eventsProcessed = 0;
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        try
        {
            // Orleans grains are automatically activated when accessed
            // We can perform any additional initialization here if needed
            _logger.LogDebug("Initializing Orleans agent {AgentId}", AgentId);
            
            // Ensure the grain is activated
            await _grain.GetIdAsync();
            
            _logger.LogInformation("Orleans agent {AgentId} initialized", AgentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Orleans agent {AgentId}", AgentId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task PublishEventAsync(EventEnvelope envelope)
    {
        if (envelope == null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        try
        {
            _logger.LogDebug("Publishing event {EventId} to Orleans agent {AgentId}", 
                envelope.Id, AgentId);
            
            // Serialize the envelope to bytes for Orleans
            var envelopeBytes = envelope.ToByteArray();
            await _grain.HandleEventAsync(envelopeBytes);
            _eventsProcessed++;
            _lastActivityTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event {EventId} to Orleans agent {AgentId}",
                envelope.Id, AgentId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IMessage?> GetStateAsync()
    {
        try
        {
            // This requires the grain to expose a method to get its state
            // For now, we return null as the implementation depends on the specific grain
            _logger.LogWarning("GetStateAsync is not yet implemented for Orleans agents");
            await Task.CompletedTask;
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get state for Orleans agent {AgentId}", AgentId);
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
            // This requires the grain to expose a method to set its state
            // For now, this is a no-op as the implementation depends on the specific grain
            _logger.LogWarning("SetStateAsync is not yet implemented for Orleans agents");
            await Task.CompletedTask;
            _lastActivityTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set state for Orleans agent {AgentId}", AgentId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeactivateAsync()
    {
        _logger.LogInformation("Deactivating Orleans agent {AgentId}", AgentId);

        try
        {
            // Orleans grains have automatic lifecycle management
            // We can't directly deactivate them, but we can call a method if needed
            await _grain.DeactivateAsync();
            _logger.LogInformation("Orleans agent {AgentId} deactivated", AgentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating Orleans agent {AgentId}", AgentId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<AgentMetadata> GetMetadataAsync()
    {
        var parentId = await _grain.GetParentAsync();
        var children = await _grain.GetChildrenAsync();
        
        return new AgentMetadata
        {
            ActivationTime = _activationTime,
            LastActivityTime = _lastActivityTime,
            EventsProcessed = _eventsProcessed,
            IsActive = true, // Orleans grains are active when referenced
            ParentAgentId = parentId,
            ChildAgentIds = children?.ToList() ?? new List<Guid>()
        };
    }

    /// <summary>
    /// Gets the underlying Orleans grain.
    /// </summary>
    public IGAgentGrain GetGrain() => _grain;
}
