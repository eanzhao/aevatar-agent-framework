using Google.Protobuf;

namespace Aevatar.Agents.Abstractions;

public interface IGAgentActor
{
    Guid Id { get; }
    Task AddSubAgentAsync<TSubAgent, TSubState>(CancellationToken ct = default) 
        where TSubAgent : IGAgent<TSubState> 
        where TSubState : class, new();
    Task RemoveSubAgentAsync(Guid subAgentId, CancellationToken ct = default);
    Task ProduceEventAsync(IMessage message, CancellationToken ct = default);
    Task SubscribeToParentStreamAsync(IGAgentActor parent, CancellationToken ct = default);
}