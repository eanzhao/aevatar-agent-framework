using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans implementation of IMassTransitEventHandler.
/// Routes MassTransit events to Orleans Grains via RPC, ensuring callbacks run on Grain turn.
/// </summary>
public class OrleansMassTransitEventHandler : IMassTransitEventHandler
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<OrleansMassTransitEventHandler> _logger;
    
    // Cache: AgentId -> GrainKey (AgentType:AgentId)
    // Populated when grains register themselves during activation
    private static readonly ConcurrentDictionary<Guid, string> _agentGrainKeyCache = new();

    public OrleansMassTransitEventHandler(
        IGrainFactory grainFactory,
        ILogger<OrleansMassTransitEventHandler> logger)
    {
        _grainFactory = grainFactory;
        _logger = logger;
    }

    /// <summary>
    /// Register a grain key for an agent ID.
    /// Called by OrleansGAgentGrain during activation.
    /// </summary>
    public static void RegisterGrainKey(Guid agentId, string grainKey)
    {
        _agentGrainKeyCache[agentId] = grainKey;
    }

    /// <summary>
    /// Unregister a grain key.
    /// Called by OrleansGAgentGrain during deactivation.
    /// </summary>
    public static void UnregisterGrainKey(Guid agentId)
    {
        _agentGrainKeyCache.TryRemove(agentId, out _);
    }

    /// <inheritdoc />
    public async Task<bool> HandleEventAsync(Guid agentId, EventEnvelope envelope)
    {
        // Try to find the grain key from cache
        if (!_agentGrainKeyCache.TryGetValue(agentId, out var grainKey))
        {
            _logger.LogDebug("Grain key not found in cache for AgentId {AgentId}", agentId);
            return false;
        }

        try
        {
            _logger.LogDebug("Routing event {EventId} to Grain {GrainKey} via RPC", envelope.Id, grainKey);
            
            // Serialize envelope to bytes
            using var stream = new MemoryStream();
            envelope.WriteTo(stream);
            var envelopeBytes = stream.ToArray();
            
            // Get grain reference and call HandleEventAsync through Orleans RPC
            // This ensures the callback runs on the Grain's turn
            var grain = _grainFactory.GetGrain<IGAgentGrain>(grainKey);
            await grain.HandleEventAsync(envelopeBytes);
            
            _logger.LogDebug("Successfully handled event {EventId} for Grain {GrainKey}", envelope.Id, grainKey);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to route event {EventId} to Grain {GrainKey}", envelope.Id, grainKey);
            throw;
        }
    }
}

