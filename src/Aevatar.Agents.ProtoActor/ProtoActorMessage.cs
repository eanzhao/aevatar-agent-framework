using Aevatar.Agents.Abstractions;
using Google.Protobuf;

namespace Aevatar.Agents.ProtoActor;

/// <summary>
/// Proto.Actor消息包装器，用于传递序列化的消息
/// </summary>
public class ProtoActorMessage
{
    public byte[] SerializedMessage { get; }
    public Type MessageType { get; }

    public ProtoActorMessage(byte[] serializedMessage, Type messageType)
    {
        SerializedMessage = serializedMessage;
        MessageType = messageType;
    }
    
    public static ProtoActorMessage Create<T>(T message, IMessageSerializer serializer) where T : IMessage
    {
        var serialized = serializer.Serialize(message);
        return new ProtoActorMessage(serialized, typeof(T));
    }
    
    public IMessage GetMessage(IMessageSerializer serializer)
    {
        // 使用反射方法调用序列化器的泛型Deserialize方法
        var deserializeMethod = typeof(IMessageSerializer)
            .GetMethod(nameof(IMessageSerializer.Deserialize))
            ?.MakeGenericMethod(MessageType);
        
        if (deserializeMethod == null)
            throw new InvalidOperationException("Deserialize method not found on IMessageSerializer");
        
        var result = deserializeMethod.Invoke(serializer, new object[] { SerializedMessage });
        if (result == null)
            throw new InvalidOperationException("Failed to deserialize message");
            
        return (IMessage)result;
    }
}

/// <summary>
/// Proto.Actor错误消息
/// </summary>
public class ProtoActorError
{
    public string ErrorMessage { get; set; } = string.Empty;
}
