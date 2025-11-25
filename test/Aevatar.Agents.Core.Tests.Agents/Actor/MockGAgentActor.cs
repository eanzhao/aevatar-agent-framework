using System.Threading;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.EventRouting;

namespace Aevatar.Agents.Core.Tests.Actor;

public class MockGAgentActor(IGAgent agent) : GAgentActorBase(agent)
{
    private readonly Dictionary<Guid, List<EventEnvelope>> _sentEvents = new();
    private readonly List<EventEnvelope> _selfEvents = new();

    public IReadOnlyDictionary<Guid, List<EventEnvelope>> SentEvents => _sentEvents;
    public IReadOnlyList<EventEnvelope> SelfEvents => _selfEvents;
    public int ActivateCallCount { get; private set; }
    public int DeactivateCallCount { get; private set; }

    // Override abstract methods for sending events
    protected override Task SendEventToActorAsync(Guid actorId, EventEnvelope envelope, CancellationToken ct = default)
    {
        if (!_sentEvents.ContainsKey(actorId))
        {
            _sentEvents[actorId] = new List<EventEnvelope>();
        }

        _sentEvents[actorId].Add(envelope);
        return Task.CompletedTask;
    }

    protected override Task SendToSelfAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        _selfEvents.Add(envelope);
        return Task.CompletedTask;
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        ActivateCallCount++;
        // Call Agent.ActivateAsync to set IsActivated = true
        await Agent.ActivateAsync();
    }

    protected override async Task OnDeactivateAsync(CancellationToken ct)
    {
        DeactivateCallCount++;
        // Call Agent.DeactivateAsync to set IsActivated = false
        await Agent.DeactivateAsync();
    }

    // Helper methods for testing
    public void ClearSentEvents()
    {
        _sentEvents.Clear();
        _selfEvents.Clear();
    }

    public EventRouter GetEventRouter() => EventRouter;

    public async Task<string> GetDescriptionAsync()
    {
        return await Agent.GetDescriptionAsync();
    }

    // Test helpers to expose hierarchy operations
    public new Task SetParentAsync(Guid parentId, CancellationToken ct = default)
        => base.SetParentAsync(parentId, ct);

    public new Task ClearParentAsync(CancellationToken ct = default)
        => base.ClearParentAsync(ct);

    public new Task AddChildAsync(Guid childId, CancellationToken ct = default)
        => base.AddChildAsync(childId, ct);

    public new Task RemoveChildAsync(Guid childId, CancellationToken ct = default)
        => base.RemoveChildAsync(childId, ct);
}