using System.Threading.Channels;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Orleans.Streams;

namespace Aevatar.Agents.Orleans;

/// <summary>
/// Orleans实现的消息流
/// </summary>
public class OrleansMessageStream : IMessageStream
{
    private readonly IMessageSerializer _serializer;
    private readonly Channel<byte[]> _channel;
    private readonly Dictionary<Type, List<object>> _handlers = new();
    private readonly IAsyncStream<byte[]>? _orleansStream;

    public Guid StreamId { get; }

    /// <summary>
    /// 使用本地Channel的构造函数（用于测试或单机环境）
    /// </summary>
    public OrleansMessageStream(IMessageSerializer serializer, Guid streamId)
    {
        _serializer = serializer;
        StreamId = streamId;
        _channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(100)
        {
            SingleReader = false,
            SingleWriter = true
        });
    }

    /// <summary>
    /// 使用Orleans Streams的构造函数
    /// </summary>
    public OrleansMessageStream(
        IMessageSerializer serializer,
        Guid streamId,
        IAsyncStream<byte[]> orleansStream)
    {
        _serializer = serializer;
        StreamId = streamId;
        _orleansStream = orleansStream;
        _channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(100)
        {
            SingleReader = false,
            SingleWriter = true
        });
    }

    public async Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage
    {
        var serialized = _serializer.Serialize(message);

        // 如果使用Orleans Streams，发送到流
        if (_orleansStream != null)
        {
            await _orleansStream.OnNextAsync(serialized);
        }

        // 发送到本地Channel
        await _channel.Writer.WriteAsync(serialized, ct);
    }

    public Task SubscribeAsync<T>(Func<T, Task> handler, CancellationToken ct = default) where T : IMessage
    {
        if (!_handlers.ContainsKey(typeof(T)))
        {
            _handlers[typeof(T)] = new List<object>();
        }
        _handlers[typeof(T)].Add(handler);

        // 如果使用Orleans Streams，订阅流
        if (_orleansStream != null)
        {
            _ = Task.Run(async () =>
            {
                var subscription = await _orleansStream.SubscribeAsync(
                    async (data, token) =>
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
                    });
            }, ct);
        }

        // 订阅本地Channel
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

