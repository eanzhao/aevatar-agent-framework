using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Plugins.MassTransit;

/// <summary>
/// MassTransit implementation of IMessageStream.
/// </summary>
public class MassTransitMessageStream : IMessageStream
{
    private readonly IBus _bus;
    private readonly IServiceProvider _serviceProvider;
    private readonly MassTransitStreamOptions _options;
    private readonly ILogger<MassTransitMessageStream> _logger;
    private readonly ConcurrentDictionary<Guid, Func<EventEnvelope, Task>> _handlers = new();

    /// <inheritdoc />
    public Guid StreamId { get; }

    public MassTransitMessageStream(
        Guid streamId, 
        IBus bus,
        IServiceProvider serviceProvider,
        IOptions<MassTransitStreamOptions> options)
    {
        StreamId = streamId;
        _bus = bus;
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = serviceProvider.GetRequiredService<ILogger<MassTransitMessageStream>>();
    }

    /// <inheritdoc />
    public async Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage
    {
        System.Console.WriteLine($"DEBUG: [MassTransitMessageStream] ProduceAsync called for StreamId: {StreamId}, MessageType: {typeof(T).Name}");
        if (message is EventEnvelope envelope)
        {
            using var stream = new MemoryStream();
            envelope.WriteTo(stream);
            
            var payload = new ByteArrayMessage 
            { 
                StreamId = StreamId,
                Data = stream.ToArray() 
            };

            System.Console.WriteLine($"DEBUG: [MassTransitMessageStream] TransportType: {_options.TransportType}");

            if (_options.TransportType == MassTransitTransportType.Kafka)
            {
                try 
                {
                    // In Kafka mode, use ITopicProducer (Rider) instead of Bus
                    var producer = _serviceProvider.GetRequiredService<ITopicProducer<ByteArrayMessage>>();
                    System.Console.WriteLine($"DEBUG: [MassTransitMessageStream] Producing to Kafka topic: {_options.TopicPrefix}");
                    await producer.Produce(payload, ct);
                    System.Console.WriteLine($"DEBUG: [MassTransitMessageStream] Successfully produced to Kafka.");
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"DEBUG: [MassTransitMessageStream] ERROR producing to Kafka: {ex}");
                    throw;
                }
            }
            else if (_options.TransportType == MassTransitTransportType.InMemory)
            {
                // InMemory mode: Use Publish which will route to all subscribed consumers
                // MassTransit will automatically route ByteArrayMessage to StreamMessageDispatcher
                System.Diagnostics.Debug.WriteLine($"MassTransitMessageStream {StreamId} publishing ByteArrayMessage with StreamId={payload.StreamId}, DataLength={payload.Data.Length}");
                await _bus.Publish(payload, ct);
            }
            else
            {
                // RabbitMQ use standard Bus Publish
                await _bus.Publish(payload, ct);
            }
        }
        else
        {
            throw new InvalidOperationException(
                $"MassTransitMessageStream only supports EventEnvelope, got {typeof(T).Name}");
        }
    }

    /// <inheritdoc />
    public Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler,
        CancellationToken ct = default) where T : IMessage
    {
        return SubscribeAsync(handler, null, ct);
    }

    /// <inheritdoc />
    public Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler,
        Func<T, bool>? filter,
        CancellationToken ct = default) where T : IMessage
    {
        var subscriptionId = Guid.NewGuid();
        
        Func<EventEnvelope, Task> wrapperHandler = async (envelope) =>
        {
            if (envelope is T typedMsg)
            {
                if (filter == null || filter(typedMsg))
                {
                    await handler(typedMsg);
                }
            }
        };

        _handlers.TryAdd(subscriptionId, wrapperHandler);

        return Task.FromResult<IMessageStreamSubscription>(
            new MassTransitMessageStreamSubscription(
                subscriptionId, 
                StreamId, 
                () => {
                    _handlers.TryRemove(subscriptionId, out _);
                    return Task.CompletedTask;
                }));
    }

    /// <summary>
    /// Dispatches a received message to local subscribers.
    /// </summary>
    internal async Task DispatchAsync(byte[] data)
    {
        try 
        {
            var envelope = EventEnvelope.Parser.ParseFrom(data);
            var tasks = new List<Task>();
            foreach (var handler in _handlers.Values)
            {
                tasks.Add(handler(envelope));
            }
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            // Log but don't throw - allow other handlers to process
            System.Diagnostics.Debug.WriteLine($"Error dispatching message: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the number of registered handlers (for debugging).
    /// </summary>
    internal int GetHandlerCount() => _handlers.Count;
}
