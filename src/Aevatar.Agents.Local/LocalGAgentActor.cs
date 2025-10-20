using Aevatar.Agents.Abstractions;
using Google.Protobuf;

namespace Aevatar.Agents.Local;

public class LocalGAgentActor<TState> : IGAgentActor where TState : class, new()
{
    private readonly IMessageStream _stream;
    private readonly IGAgent<TState> _businessAgent;
    private readonly Dictionary<Guid, IGAgentActor> _subAgents = new();
    private readonly IGAgentFactory _factory;
    private readonly Dictionary<Guid, List<EventEnvelope>> _eventStore;

    public LocalGAgentActor(IMessageStream stream, IGAgent<TState> businessAgent, IGAgentFactory factory, Dictionary<Guid, List<EventEnvelope>> eventStore)
    {
        _stream = stream;
        _businessAgent = businessAgent;
        _factory = factory;
        _eventStore = eventStore;
        if (!_eventStore.ContainsKey(businessAgent.Id))
        {
            _eventStore[businessAgent.Id] = new List<EventEnvelope>();
        }
    }

    public Guid Id => _businessAgent.Id;

    public async Task AddSubAgentAsync<TSubAgent, TSubState>(CancellationToken ct = default) 
        where TSubAgent : IGAgent<TSubState> 
        where TSubState : class, new()
    {
        var subAgentActor = await _factory.CreateAgentAsync<TSubAgent, TSubState>(Guid.NewGuid(), ct);
        _subAgents[subAgentActor.Id] = subAgentActor;
        await _businessAgent.AddSubAgentAsync<TSubAgent, TSubState>(ct);
        _eventStore[_businessAgent.Id].AddRange(_businessAgent.GetPendingEvents());
        await subAgentActor.SubscribeToParentStreamAsync(this, ct);
    }

    public async Task RemoveSubAgentAsync(Guid subAgentId, CancellationToken ct = default)
    {
        if (_subAgents.Remove(subAgentId))
        {
            await _businessAgent.RemoveSubAgentAsync(subAgentId, ct);
            _eventStore[_businessAgent.Id].AddRange(_businessAgent.GetPendingEvents());
        }
    }

    public async Task ProduceEventAsync(IMessage message, CancellationToken ct = default)
    {
        await _stream.ProduceAsync(message, ct);
    }

    public async Task SubscribeToParentStreamAsync(IGAgentActor parent, CancellationToken ct = default)
    {
        await _businessAgent.RegisterEventHandlersAsync(_stream, ct);
        // 订阅父级流消息
    }
}