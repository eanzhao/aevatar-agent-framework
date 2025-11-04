using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Abstractions.Extensions;

/// <summary>
/// 提供对消息操作的扩展方法
/// </summary>
public static class MessageExtensions
{
    /// <summary>
    /// 确定事件信封是否包含指定类型的有效负载
    /// </summary>
    public static bool HasPayload<T>(this EventEnvelope envelope) where T : IMessage, new()
    {
        // 使用静态反射获取Descriptor
        var descriptor = typeof(T).GetProperty("Descriptor",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        if (descriptor != null)
        {
            var descriptorValue = descriptor.GetValue(null);
            if (descriptorValue != null && envelope.Payload != null)
            {
                var isMethod = envelope.Payload.GetType().GetMethod("Is", new[] { descriptorValue.GetType() });
                if (isMethod != null)
                {
                    return (bool)(isMethod.Invoke(envelope.Payload, new[] { descriptorValue }) ?? false);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 从事件信封中解包指定类型的有效负载
    /// </summary>
    public static T? UnpackPayload<T>(this EventEnvelope envelope) where T : IMessage, new()
    {
        if (envelope.Payload == null || !envelope.HasPayload<T>())
        {
            return default;
        }

        return envelope.Payload.Unpack<T>();
    }

    /// <summary>
    /// 创建带有指定负载的事件信封
    /// </summary>
    public static EventEnvelope CreateEventEnvelope<T>(this T payload, long version) where T : IMessage
    {
        return new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Version = version,
            Payload = Any.Pack(payload)
        };
    }
}