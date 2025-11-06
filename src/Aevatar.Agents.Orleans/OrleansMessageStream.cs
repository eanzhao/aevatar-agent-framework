using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Orleans.Streams;
using System.Collections.Concurrent;

namespace Aevatar.Agents.Orleans;

/// <summary>
/// Orleans 运行时的 Message Stream 实现
/// 基于 Orleans Stream 系统，支持订阅管理
/// </summary>
public class OrleansMessageStream : IMessageStream
{
    private readonly IAsyncStream<byte[]> _stream;  // 使用 byte[] 避免序列化问题
    private readonly ConcurrentDictionary<Guid, OrleansMessageStreamSubscription> _subscriptions = new();
    
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
    public async Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler, 
        CancellationToken ct = default) where T : IMessage
    {
        return await SubscribeAsync(handler, filter: null, ct);
    }
    
    /// <summary>
    /// 订阅 Stream 消息（带过滤器）
    /// </summary>
    public async Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler,
        Func<T, bool>? filter,
        CancellationToken ct = default) where T : IMessage
    {
        if (typeof(T) != typeof(EventEnvelope))
        {
            throw new InvalidOperationException(
                $"OrleansMessageStream only supports EventEnvelope subscription, got {typeof(T).Name}");
        }
        
        // 创建带过滤器的 Observer
        var observer = new OrleansStreamObserver(async bytes =>
        {
            try
            {
                // 反序列化 EventEnvelope
                var envelope = EventEnvelope.Parser.ParseFrom(bytes);
                var typedMessage = (T)(object)envelope;
                
                // 应用过滤器
                if (filter != null && !filter(typedMessage))
                {
                    return;
                }
                
                await handler(typedMessage);
            }
            catch (Exception)
            {
                // 忽略反序列化错误
            }
        });
        
        // 订阅 Stream 并获取句柄
        var streamHandle = await _stream.SubscribeAsync(observer);
        
        // 创建订阅包装器
        var subscriptionId = Guid.NewGuid();
        var subscription = new OrleansMessageStreamSubscription(
            subscriptionId,
            StreamId,
            streamHandle,
            observer,
            _stream,
            () => _subscriptions.TryRemove(subscriptionId, out _));
        
        _subscriptions.TryAdd(subscriptionId, subscription);
        return subscription;
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

