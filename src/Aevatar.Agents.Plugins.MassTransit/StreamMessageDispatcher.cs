using MassTransit;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Plugins.MassTransit;

/// <summary>
/// Consumer that dispatches received messages to the appropriate MassTransitMessageStream.
/// </summary>
public class StreamMessageDispatcher : IConsumer<ByteArrayMessage>
{
    private readonly MassTransitMessageStreamProvider _provider;
    private readonly ILogger<StreamMessageDispatcher> _logger;

    public StreamMessageDispatcher(MassTransitMessageStreamProvider provider, ILogger<StreamMessageDispatcher> logger)
    {
        System.Console.WriteLine("DEBUG: StreamMessageDispatcher Constructor Called");
        _provider = provider;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ByteArrayMessage> context)
    {
        var streamId = context.Message.StreamId;
        _logger.LogInformation("StreamMessageDispatcher received message for Stream {StreamId}", streamId);
        
        var stream = _provider.GetStreamInternal(streamId);
        
        if (stream != null)
        {
            var handlerCount = stream.GetHandlerCount();
            _logger.LogInformation("Dispatching message to Stream {StreamId}, handler count: {HandlerCount}", streamId, handlerCount);
            await stream.DispatchAsync(context.Message.Data);
        }
        else
        {
            _logger.LogWarning("No stream found for StreamId {StreamId}. Available streams: {StreamIds}", 
                streamId, string.Join(", ", _provider.GetAllStreamIds()));
        }
    }
}

