using System.Threading.Channels;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;

namespace Aevatar.Agents.Local;

public class LocalMessageStream : IMessageStream
{
    private readonly Channel<byte[]> _channel;
    private readonly IMessageSerializer _serializer;
    private readonly Dictionary<Type, List<object>> _handlers = new();
    
    public Guid StreamId { get; }

    public LocalMessageStream(IMessageSerializer serializer, Guid streamId)
    {
        _serializer = serializer;
        StreamId = streamId;
        _channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(100)
        {
            SingleReader = false,
            SingleWriter = true
        });
    }

    public async Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage
    {
        var serialized = _serializer.Serialize(message);
        await _channel.Writer.WriteAsync(serialized, ct);
    }

    public Task SubscribeAsync<T>(Func<T, Task> handler, CancellationToken ct = default) where T : IMessage
    {
        if (!_handlers.ContainsKey(typeof(T)))
        {
            _handlers[typeof(T)] = new List<object>();
        }
        _handlers[typeof(T)].Add(handler);

        _ = Task.Run(async () =>
        {
            await foreach (var data in _channel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var message = _serializer.Deserialize<T>(data);
                    foreach (var h in _handlers[typeof(T)].Cast<Func<T, Task>>())
                    {
                        await h(message);
                    }
                }
                catch (Exception)
                {
                    // 忽略非T类型事件
                }
            }
        }, ct);
        
        return Task.CompletedTask;
    }
}