using System.Threading.Channels;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;

namespace Aevatar.Agents.Local;

/// <summary>
/// Local 运行时的 Message Stream 实现
/// 基于 System.Threading.Channels 提供高性能的消息队列
/// </summary>
public class LocalMessageStream : IMessageStream
{
    private readonly Channel<EventEnvelope> _channel;
    private readonly List<Func<EventEnvelope, Task>> _subscribers = new();
    private readonly CancellationTokenSource _cts = new();
    
    public Guid StreamId { get; }
    
    public LocalMessageStream(Guid streamId, int capacity = 1000)
    {
        StreamId = streamId;
        _channel = Channel.CreateBounded<EventEnvelope>(new BoundedChannelOptions(capacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        
        // 启动消息处理循环
        _ = ProcessMessagesAsync();
    }
    
    /// <summary>
    /// 发布消息到 Stream
    /// </summary>
    public async Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage
    {
        if (message is EventEnvelope envelope)
        {
            await _channel.Writer.WriteAsync(envelope, ct);
        }
        else
        {
            throw new InvalidOperationException($"LocalMessageStream only supports EventEnvelope, got {typeof(T).Name}");
        }
    }
    
    /// <summary>
    /// 订阅 Stream 消息
    /// </summary>
    public Task SubscribeAsync<T>(Func<T, Task> handler, CancellationToken ct = default) where T : IMessage
    {
        if (typeof(T) == typeof(EventEnvelope))
        {
            _subscribers.Add(async env => await handler((T)(object)env));
        }
        else
        {
            // 只订阅特定类型的事件（通过 Payload 类型 URL 过滤）
            var expectedTypeUrl = $"type.googleapis.com/{typeof(T).FullName}";
            _subscribers.Add(async env =>
            {
                if (env.Payload != null && env.Payload.TypeUrl.Contains(typeof(T).Name))
                {
                    try
                    {
                        // 使用反射 Unpack
                        var unpackMethod = typeof(Google.Protobuf.WellKnownTypes.Any)
                            .GetMethod("Unpack", Type.EmptyTypes)
                            ?.MakeGenericMethod(typeof(T));
                        
                        if (unpackMethod != null)
                        {
                            var message = (T)unpackMethod.Invoke(env.Payload, null)!;
                            await handler(message);
                        }
                    }
                    catch (Exception)
                    {
                        // 忽略类型不匹配的事件
                    }
                }
            });
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// 处理消息循环
    /// </summary>
    private async Task ProcessMessagesAsync()
    {
        await foreach (var envelope in _channel.Reader.ReadAllAsync(_cts.Token))
        {
            // 并发调用所有订阅者
            var tasks = _subscribers.Select(subscriber => 
                Task.Run(async () =>
                {
                    try
                    {
                        await subscriber(envelope);
                    }
                    catch (Exception)
                    {
                        // 忽略订阅者错误，不影响其他订阅者
                    }
                }));
            
            await Task.WhenAll(tasks);
        }
    }
    
    /// <summary>
    /// 停止 Stream
    /// </summary>
    public void Stop()
    {
        _channel.Writer.Complete();
        _cts.Cancel();
    }
}

