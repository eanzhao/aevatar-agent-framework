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
        return _streams.GetOrAdd(agentId, id => 
            new MassTransitMessageStream(id, _bus, _serviceProvider, _options));
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
