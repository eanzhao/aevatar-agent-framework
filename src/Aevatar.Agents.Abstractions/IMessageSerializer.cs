using Google.Protobuf;

namespace Aevatar.Agents.Abstractions;

public interface IMessageSerializer
{
    byte[] Serialize<T>(T message) where T : IMessage;
    T Deserialize<T>(byte[] data) where T : IMessage;
}