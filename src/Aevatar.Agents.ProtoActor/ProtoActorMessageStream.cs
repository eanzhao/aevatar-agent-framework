using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Proto;

namespace Aevatar.Agents.ProtoActor;

/// <summary>
/// Proto.Actor实现的消息流
/// </summary>
public class ProtoActorMessageStream : IMessageStream
{
    private readonly IMessageSerializer _serializer;
    private readonly IRootContext _rootContext;
    private readonly PID _streamActorPid;
    private readonly Dictionary<Type, List<Func<IMessage, Task>>> _localHandlers = new();
    
    public ProtoActorMessageStream(IMessageSerializer serializer, Guid streamId, IRootContext rootContext)
    {
        _serializer = serializer;
        _rootContext = rootContext;
        StreamId = streamId;
        
        // 创建流Actor
        var streamProps = Props.FromProducer(() => new StreamActor(serializer));
        _streamActorPid = _rootContext.SpawnNamed(streamProps, $"stream-{streamId}");
    }

    public Guid StreamId { get; }

    public async Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage
    {
        // 创建流消息并发送到流Actor
        var streamMessage = StreamMessage.Create(message, _serializer);
        
        try
        {
            // 直接发送到流Actor
            _rootContext.Send(_streamActorPid, streamMessage);
            
            // 本地处理（如果有订阅者）
            if (_localHandlers.TryGetValue(typeof(T), out var handlers))
            {
                foreach (var handler in handlers.Cast<Func<T, Task>>())
                {
                    await handler(message);
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to produce message: {ex.Message}", ex);
        }
    }

    public Task SubscribeAsync<T>(Func<T, Task> handler, CancellationToken ct = default) where T : IMessage
    {
        try
        {
            // 将处理程序添加到本地处理程序列表中
            if (!_localHandlers.ContainsKey(typeof(T)))
            {
                _localHandlers[typeof(T)] = new List<Func<IMessage, Task>>();
            }
            
            // 添加类型化的处理程序
            _localHandlers[typeof(T)].Add(async (msg) => await handler((T)msg));
            
            // 创建一个Handler Actor来处理从流中接收到的消息
            var handlerProps = Props.FromFunc(context =>
            {
                if (context.Message is ProtoActorMessage protoMsg && 
                    protoMsg.MessageType == typeof(T))
                {
                    try
                    {
                        var typedMessage = (T)protoMsg.GetMessage(_serializer);
                        // 异步启动处理，但不等待它
                        _ = handler(typedMessage);
                    }
                    catch (Exception ex)
                    {
                        // 错误处理
                        Console.WriteLine($"Error handling message: {ex.Message}");
                    }
                }
                
                return Task.CompletedTask;
            });
            
            // 创建Handler Actor
            var handlerPid = _rootContext.Spawn(handlerProps);
            
            // 发送订阅请求到流Actor
            var subRequest = new SubscriptionRequest
            {
                SubscriberPid = handlerPid,
                MessageType = typeof(T),
                Handler = async (obj) => await handler((T)obj)
            };
            
            // 发送订阅请求，并等待响应
            var response = _rootContext.RequestAsync<SubscriptionResponse>(_streamActorPid, subRequest, ct).Result;
            
            if (!response.Success)
            {
                throw new InvalidOperationException($"Failed to subscribe: {response.ErrorMessage}");
            }
            
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to subscribe: {ex.Message}", ex);
        }
    }
}
