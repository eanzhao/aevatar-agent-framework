using Google.Protobuf;

namespace Aevatar.Agents.Abstractions;

public interface IMessageStream
{
    Guid StreamId { get; }
    Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage;
    Task SubscribeAsync<T>(Func<T, Task> handler, CancellationToken ct = default) where T : IMessage;
}