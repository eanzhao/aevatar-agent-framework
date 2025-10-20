using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Proto;

namespace Aevatar.Agents.ProtoActor;

/// <summary>
/// 消息流Actor，负责接收并分发消息给订阅者
/// </summary>
public class StreamActor : IActor
{
    private readonly IMessageSerializer _serializer;
    private readonly Dictionary<Type, List<PID>> _typeSubscribers = new();

    public StreamActor(IMessageSerializer serializer)
    {
        _serializer = serializer;
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            Started => HandleStarted(context),
            // 收到新消息时分发给订阅者
            StreamMessage streamMsg => HandleStreamMessage(context, streamMsg),
            // 订阅请求
            SubscriptionRequest subReq => HandleSubscription(context, subReq),
            Stopping => HandleStopping(context),
            _ => Task.CompletedTask
        };
    }

    private Task HandleStarted(IContext context)
    {
        // Stream Actor启动时的初始化逻辑
        return Task.CompletedTask;
    }
    
    private Task HandleStreamMessage(IContext context, StreamMessage streamMsg)
    {
        try
        {
            // 获取消息类型，然后将消息发送给所有订阅该类型的Actor
            if (_typeSubscribers.TryGetValue(streamMsg.MessageType, out var subscribers))
            {
                var message = new ProtoActorMessage(streamMsg.SerializedMessage, streamMsg.MessageType);
                
                // 发送给所有订阅者
                foreach (var subscriber in subscribers)
                {
                    context.Send(subscriber, message);
                }
            }
            
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            context.Respond(new ProtoActorError { ErrorMessage = $"Error handling stream message: {ex.Message}" });
            return Task.CompletedTask;
        }
    }
    
    private Task HandleSubscription(IContext context, SubscriptionRequest subReq)
    {
        try
        {
            // 如果类型不存在，添加新列表
            if (!_typeSubscribers.ContainsKey(subReq.MessageType))
            {
                _typeSubscribers[subReq.MessageType] = new List<PID>();
            }
            
            // 添加订阅者
            if (!_typeSubscribers[subReq.MessageType].Contains(subReq.SubscriberPid))
            {
                _typeSubscribers[subReq.MessageType].Add(subReq.SubscriberPid);
                
                // 监视订阅者，如果它停止了，我们可以移除订阅
                context.Watch(subReq.SubscriberPid);
            }
            
            context.Respond(new SubscriptionResponse { Success = true });
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            context.Respond(new SubscriptionResponse 
            { 
                Success = false,
                ErrorMessage = $"Error adding subscription: {ex.Message}" 
            });
            return Task.CompletedTask;
        }
    }
    
    private Task HandleStopping(IContext context)
    {
        // Stream Actor停止时的清理逻辑
        _typeSubscribers.Clear();
        return Task.CompletedTask;
    }
}

/// <summary>
/// 流消息
/// </summary>
public class StreamMessage
{
    public byte[] SerializedMessage { get; }
    public Type MessageType { get; }

    public StreamMessage(byte[] serializedMessage, Type messageType)
    {
        SerializedMessage = serializedMessage;
        MessageType = messageType;
    }
    
    public static StreamMessage Create<T>(T message, IMessageSerializer serializer) where T : IMessage
    {
        var serialized = serializer.Serialize(message);
        return new StreamMessage(serialized, typeof(T));
    }
}

/// <summary>
/// 订阅请求
/// </summary>
public class SubscriptionRequest
{
    public required PID SubscriberPid { get; set; }
    public required Type MessageType { get; set; }
    public required Func<object, Task> Handler { get; set; }
}

/// <summary>
/// 订阅响应
/// </summary>
public class SubscriptionResponse
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}
