using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Orleans.Streams;

namespace Aevatar.Agents.Orleans;

/// <summary>
/// Orleans 运行时的 Message Stream 实现
/// 基于 Orleans Stream 系统
/// </summary>
public class OrleansMessageStream : IMessageStream
{
    private readonly IAsyncStream<byte[]> _stream;  // 使用 byte[] 避免序列化问题
    
    public Guid StreamId { get; }
    
    public OrleansMessageStream(Guid streamId, IAsyncStream<byte[]> stream)
    {
        StreamId = streamId;
        _stream = stream;
    }
    
    /// <summary>
    /// 发布消息到 Stream
    /// </summary>
    public async Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage
    {
        if (message is EventEnvelope envelope)
        {
            // 序列化 EventEnvelope 为 byte[]
            using var stream = new MemoryStream();
            using var output = new CodedOutputStream(stream);
            envelope.WriteTo(output);
            output.Flush();
            
            await _stream.OnNextAsync(stream.ToArray());
        }
        else
        {
            throw new InvalidOperationException($"OrleansMessageStream only supports EventEnvelope, got {typeof(T).Name}");
        }
    }
    
    /// <summary>
    /// 订阅 Stream 消息
    /// </summary>
    public async Task SubscribeAsync<T>(Func<T, Task> handler, CancellationToken ct = default) where T : IMessage
    {
        if (typeof(T) != typeof(EventEnvelope))
        {
            throw new InvalidOperationException($"OrleansMessageStream only supports EventEnvelope subscription, got {typeof(T).Name}");
        }
        
        // 创建 Observer
        var observer = new OrleansStreamObserver(async bytes =>
        {
            try
            {
                // 反序列化 EventEnvelope
                var envelope = EventEnvelope.Parser.ParseFrom(bytes);
                await handler((T)(object)envelope);
            }
            catch (Exception)
            {
                // 忽略反序列化错误
            }
        });
        
        // 订阅 Stream
        await _stream.SubscribeAsync(observer);
    }
}

/// <summary>
/// Orleans Stream Observer
/// </summary>
internal class OrleansStreamObserver : IAsyncObserver<byte[]>
{
    private readonly Func<byte[], Task> _handler;
    
    public OrleansStreamObserver(Func<byte[], Task> handler)
    {
        _handler = handler;
    }
    
    public async Task OnNextAsync(byte[] item, StreamSequenceToken? token = null)
    {
        await _handler(item);
    }
    
    public Task OnCompletedAsync()
    {
        return Task.CompletedTask;
    }
    
    public Task OnErrorAsync(Exception ex)
    {
        return Task.CompletedTask;
    }
}

