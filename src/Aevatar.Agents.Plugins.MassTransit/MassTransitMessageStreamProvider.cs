using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Plugins.MassTransit;

/// <summary>
/// Provider for MassTransit message streams.
/// </summary>
public class MassTransitMessageStreamProvider : IMessageStreamProvider
{
    private readonly IBus _bus;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<MassTransitStreamOptions> _options;
    private readonly ConcurrentDictionary<Guid, MassTransitMessageStream> _streams = new();

    public MassTransitMessageStreamProvider(
        IBus bus,
        IServiceProvider serviceProvider,
        IOptions<MassTransitStreamOptions> options)
    {
        _bus = bus;
        _serviceProvider = serviceProvider;
        _options = options;
    }

    /// <inheritdoc />
    public IMessageStream GetStream(Guid agentId)
    {
        return GetStream(agentId, null);
    }

    /// <inheritdoc />
    public IMessageStream GetStream(Guid agentId, string? category = null)
    {
        // We use GetOrAdd, but we need to make sure if the stream exists, its category is updated or compatible?
        // Actually, StreamId (AgentId) is unique. The category is mainly used for Producing.
        // A stream instance is tied to an AgentId. 
        // If we create it with a category, that category determines where it publishes TO.
        
        return _streams.GetOrAdd(agentId, id => 
            new MassTransitMessageStream(id, category, _bus, _serviceProvider, _options));
    }

    /// <summary>
    /// Internal method to retrieve a stream if it exists locally.
    /// Used by StreamMessageDispatcher.
    /// </summary>
    internal MassTransitMessageStream? GetStreamInternal(Guid streamId)
    {
        _streams.TryGetValue(streamId, out var stream);
        return stream;
    }

    /// <summary>
    /// Gets all registered stream IDs (for debugging).
    /// </summary>
    internal IEnumerable<Guid> GetAllStreamIds() => _streams.Keys;
}
