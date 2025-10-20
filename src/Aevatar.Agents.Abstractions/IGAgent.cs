using Google.Protobuf;

namespace Aevatar.Agents.Abstractions;

public interface IGAgent
{
    Guid Id { get; }
}

public interface IGAgent<TState> : IGAgent where TState : class, new()
{
    // Id属性继承自IGAgent接口
    Task RegisterEventHandlersAsync(IMessageStream stream, CancellationToken ct = default);

    Task AddSubAgentAsync<TSubAgent, TSubState>(CancellationToken ct = default)
        where TSubAgent : IGAgent<TSubState>
        where TSubState : class, new();

    Task RemoveSubAgentAsync(Guid subAgentId, CancellationToken ct = default);
    IReadOnlyList<IGAgent> GetSubAgents();
    TState GetState();
    IReadOnlyList<EventEnvelope> GetPendingEvents();
    Task RaiseEventAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : class;
    Task ApplyEventAsync(EventEnvelope evt, CancellationToken ct = default);
    Task ProduceEventAsync(IMessage message, CancellationToken ct = default);
}