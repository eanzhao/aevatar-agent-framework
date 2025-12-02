using Aevatar.Agents.Abstractions;
using MassTransit;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aevatar.Agents.Plugins.MassTransit;

public class StreamMessageDispatcher : IConsumer<ByteArrayMessage>
{
    private readonly MassTransitMessageStreamProvider _provider;
    private readonly IEnumerable<IStreamNotFoundHandler> _notFoundHandlers;
    private readonly ILogger<StreamMessageDispatcher> _logger;

    public StreamMessageDispatcher(
        MassTransitMessageStreamProvider provider,
        IEnumerable<IStreamNotFoundHandler> notFoundHandlers,
        ILogger<StreamMessageDispatcher> logger)
    {
        _provider = provider;
        _notFoundHandlers = notFoundHandlers;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ByteArrayMessage> context)
    {
        var streamId = context.Message.StreamId;
        
        // 1. Try to get the stream directly
        var stream = _provider.GetStreamInternal(streamId);

        if (stream == null)
        {
            // 2. If stream not found, invoke handlers to try to activate the actor
            _logger.LogWarning("Stream not found for StreamId {StreamId}. Attempting to activate actor via handlers...", streamId);
            
            foreach (var handler in _notFoundHandlers)
            {
                try 
                {
                    await handler.HandleStreamNotFoundAsync(streamId);
                }
                catch (System.Exception ex)
                {
                    _logger.LogError(ex, "Error executing StreamNotFoundHandler for StreamId {StreamId}", streamId);
                }
            }

            // 3. Retry getting the stream
            stream = _provider.GetStreamInternal(streamId);
        }

        if (stream != null)
        {
            await stream.DispatchAsync(context.Message.Data);
            _logger.LogDebug("Dispatched message to StreamId {StreamId}", streamId);
        }
        else
        {
            // 4. If still not found, we have to throw exception to trigger MassTransit retry
            //    This is crucial for ensuring message delivery guarantees
            _logger.LogWarning("Stream still not found for StreamId {StreamId} after activation attempt. Throwing exception to trigger retry.", streamId);
            throw new System.InvalidOperationException($"Stream {streamId} not found. Actor might be failing to activate.");
        }
    }
}
