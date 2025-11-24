using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Observability;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Agent Actor implementation for Local runtime.
/// Uses LocalMessageStream as the messaging transport mechanism.
/// </summary>
public class LocalGAgentActor : GAgentActorBase
{
    private static int _activeActorCount = 0;
    private readonly LocalMessageStreamRegistry _streamRegistry;
    private readonly LocalMessageStream _myStream; // The stream for this actor
    private IMessageStreamSubscription? _parentStreamSubscription; // Subscription handle for parent stream

    public LocalGAgentActor(
        IGAgent agent,
        LocalMessageStreamRegistry streamRegistry)
        : base(agent)
    {
        _streamRegistry = streamRegistry ?? throw new ArgumentNullException(nameof(streamRegistry));
        _myStream = streamRegistry.GetOrCreateStream(agent.Id);
    }

    // ============ Hierarchy Management (Overrides) ============

    public override async Task SetParentAsync(Guid parentId, CancellationToken ct = default)
    {
        // If parent already exists, clear it first
        if (EventRouter.GetParent() != null)
        {
            await ClearParentAsync(ct);
        }

        // Call base method to set parent
        await base.SetParentAsync(parentId, ct);

        // Subscribe to parent's stream
        var parentStream = _streamRegistry.GetStream(parentId);
        if (parentStream != null)
        {
            // Note: Event type filtering is deprecated.
            // Since Protobuf does not support inheritance, effective filtering at the type level is not possible.
            // All event filtering should be done within the Agent's event handler based on event content.

            // Create filter: Check Publishers list and direction
            // - DOWN events: Should not be broadcast via parent stream, filter out
            // - UP events: Check Publishers list to avoid duplicate processing
            // - BOTH events: Allowed
            bool CombinedFilter(EventEnvelope envelope)
            {
                Logger.LogDebug(
                    "[FILTER] Agent {AgentId} checking envelope from parent stream - EventId={EventId}, Direction={Direction}, PublisherId={PublisherId}, PayloadType={PayloadType}, Publishers={Publishers}",
                    Id, envelope.Id, envelope.Direction, envelope.PublisherId, envelope.Payload?.TypeUrl,
                    string.Join(",", envelope.Publishers));

                // DOWN events should not be broadcast via parent stream, filter out directly
                // DOWN events should be sent directly to children's streams, not broadcast via parent stream
                if (envelope.Direction == EventDirection.Down)
                {
                    Logger.LogDebug("[FILTER] Agent {AgentId} filtering out DOWN event {EventId} from parent stream",
                        Id, envelope.Id);
                    return false;
                }

                // For UP events, check if self is already in the Publishers list
                if (envelope.Direction == EventDirection.Up && envelope.Publishers.Contains(Id.ToString()))
                {
                    Logger.LogDebug(
                        "[FILTER] Agent {AgentId} already in Publishers list for UP event {EventId}, filtering out", Id,
                        envelope.Id);
                    return false; // Filter out UP events that have already been processed
                }

                return true;
            }

            // Agent subscribes to parent stream to receive group broadcasts
            _parentStreamSubscription = await parentStream.SubscribeAsync<EventEnvelope>(
                async envelope =>
                {
                    // Logic for handling events received from parent stream:
                    // - UP events: Only need processing, no further propagation (already broadcast in parent stream)
                    // - DOWN events: Need propagation to children after processing (multi-level propagation)

                    Logger.LogDebug(
                        "[SUBSCRIPTION] Agent {AgentId} received event {EventId} from parent stream, PublisherId={PublisherId}, PayloadType={PayloadType}",
                        Id, envelope.Id, envelope.PublisherId, envelope.Payload?.TypeUrl);

                    try
                    {
                        // Process event
                        Logger.LogDebug("Processing event {EventId} from parent stream on agent {AgentId}",
                            envelope.Id, Id);

                        // First call Agent's HandleEventAsync method to process the event
                        var handleMethod = Agent.GetType().GetMethod("HandleEventAsync",
                            [typeof(EventEnvelope), typeof(CancellationToken)]);

                        if (handleMethod != null)
                        {
                            var task = handleMethod.Invoke(Agent, new object[] { envelope, ct }) as Task;
                            if (task != null)
                            {
                                await task;
                                Logger.LogDebug("Event {EventId} processed by agent {AgentId}",
                                    envelope.Id, Id);
                            }
                        }
                        else
                        {
                            Logger.LogWarning("HandleEventAsync method not found on agent {AgentId}", Id);
                        }

                        // Logic for handling events received from parent stream:
                        // - UP events: Only need processing, no further propagation (already broadcast in parent stream)
                        // - DOWN events: Need propagation to children after processing (multi-level propagation)
                        // - BOTH events: Propagate DOWN only to children (cannot go UP again to avoid loops)
                        if (envelope.Direction == EventDirection.Down)
                        {
                            // DOWN event: Continue propagation down
                            Logger.LogDebug(
                                "Continuing DOWN propagation of event {EventId} from agent {AgentId} to children",
                                envelope.Id, Id);
                            await EventRouter.ContinuePropagationAsync(envelope, ct);
                        }
                        else if (envelope.Direction == EventDirection.Both)
                        {
                            // BOTH event from parent: Propagate DOWN only, not UP (to avoid loops)
                            Logger.LogDebug(
                                "Continuing DOWN-ONLY propagation for BOTH event {EventId} from parent stream",
                                envelope.Id);

                            // Create a new envelope with DOWN direction to continue propagation
                            var downOnlyEnvelope = envelope.Clone();
                            downOnlyEnvelope.Direction = EventDirection.Down;
                            await EventRouter.ContinuePropagationAsync(downOnlyEnvelope, ct);
                        }
                        // UP events do not need further propagation as they are already broadcast in parent stream
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error handling event {EventId} from parent stream on agent {AgentId}",
                            envelope.Id, Id);
                    }
                },
                CombinedFilter,
                ct);

            Logger.LogDebug("Agent {AgentId} subscribed to parent {ParentId} stream", Id, parentId);
        }
    }

    public override async Task ClearParentAsync(CancellationToken ct = default)
    {
        // Call base method to clear parent
        await base.ClearParentAsync(ct);

        // Unsubscribe from parent stream
        if (_parentStreamSubscription != null)
        {
            await _parentStreamSubscription.UnsubscribeAsync();
            _parentStreamSubscription = null;
            Logger.LogDebug("Agent {AgentId} unsubscribed from parent stream", Id);
        }
    }

    // ============ Abstract Method Implementation ============

    /// <summary>
    /// Send event to self (via own Stream)
    /// </summary>
    protected override async Task SendToSelfAsync(EventEnvelope envelope, CancellationToken ct)
    {
        await _myStream.ProduceAsync(envelope, ct);
    }

    /// <summary>
    /// Send event to specified Actor (via target Actor's Stream)
    /// </summary>
    protected override async Task SendEventToActorAsync(Guid actorId, EventEnvelope envelope, CancellationToken ct)
    {
        var targetStream = _streamRegistry.GetStream(actorId);
        if (targetStream != null)
        {
            await targetStream.ProduceAsync(envelope, ct);
        }
        else
        {
            Logger.LogWarning("Stream for actor {ActorId} not found", actorId);
        }
    }

    // ============ Event Publishing ============

    // PublishEventAsync uses the base class GAgentActorBase implementation.
    // The base implementation already includes EventRouter routing logic and complete Metrics recording.
    // No override needed here.

    // ============ Lifecycle ============

    protected override async Task OnActivateAsync(CancellationToken ct)
    {
        // Subscribe to own Stream
        await _myStream.SubscribeAsync<EventEnvelope>(
            async envelope =>
            {
                Logger.LogDebug("[SUBSCRIPTION] Agent {AgentId} received event {EventId} from self stream", Id,
                    envelope.Id);
                await HandleEventAsync(envelope, ct);
            },
            null, // No filter needed for self stream
            ct);

        Logger.LogInformation("LocalGAgentActor {Id} activated and subscribed to stream", Id);

        // Update active Actor count
        var count = Interlocked.Increment(ref _activeActorCount);
        AgentMetrics.UpdateActiveActorCount(count);
        Logger.LogDebug("Active actor count: {Count}", count);
    }

    protected override Task OnDeactivateAsync(CancellationToken ct = default)
    {
        Logger.LogInformation("Deactivating agent {AgentId}", Id);

        _streamRegistry.RemoveStream(Id);

        var count = Interlocked.Decrement(ref _activeActorCount);
        AgentMetrics.UpdateActiveActorCount(count);
        Logger.LogDebug("Active actor count: {Count}", count);

        return Task.CompletedTask;
    }
}