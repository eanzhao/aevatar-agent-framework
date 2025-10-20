using Google.Protobuf;
using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Serialization;

public class ProtobufSerializer : IMessageSerializer
{
    public byte[] Serialize<T>(T message) where T : IMessage
    {
        if (message is IMessage protoMessage)
        {
            return protoMessage.ToByteArray();
        }

        throw new NotSupportedException($"Type {typeof(T).FullName} does not implement IMessage");
    }

    public T Deserialize<T>(byte[] data) where T : IMessage
    {
        var parserProperty = typeof(T).GetProperty("Parser",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        if (parserProperty != null)
        {
            // 获取MessageParser的值
            var parser = parserProperty.GetValue(null);
            if (parser != null)
            {
                // 使用反射调用ParseFrom方法，避免类型转换问题
                var parseFromMethod = parser.GetType().GetMethod("ParseFrom", new[] { typeof(byte[]) });
                if (parseFromMethod != null)
                {
                    object? result = parseFromMethod.Invoke(parser, new object[] { data });
                    if (result != null)
                    {
                        return (T)result;
                    }
                }
            }
        }

        throw new NotSupportedException($"Type {typeof(T).FullName} does not have a Protobuf Parser");
    }
}