using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.EventRouting;
using Aevatar.Agents.Core.EventDeduplication;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Core.Observability;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace Aevatar.Agents.Core;

/// <summary>
/// Base class for Agent Actor
/// Provides standard implementation of event propagation logic
/// </summary>
public abstract class GAgentActorBase : IGAgentActor
{
    // ============ Fields ============

    protected readonly IGAgent Agent;
    private ILogger _logger = NullLogger.Instance;
    protected EventRouter EventRouter;

    // Improved event deduplication mechanism
    protected IEventDeduplicator EventDeduplicator { get; set; }

    // EventRouter factory for creating EventRouter with DI support
    // Internal to allow EventRouterFactoryInjector to replace it
    internal EventRouterFactory _eventRouterFactory = new();

    /// <summary>
    /// Logger property - supports automatic injection
    /// </summary>
    protected ILogger Logger
    {
        get => _logger;
        set => _logger = value ?? NullLogger.Instance;
    }

    // ============ Constructor ============

    /// <summary>
    /// Full constructor - Agent and Logger
    /// </summary>
    protected GAgentActorBase(IGAgent agent)
    {
        Agent = agent ?? throw new ArgumentNullException(nameof(agent));

        // Note: EventRouterFactory should be injected by EventRouterFactoryInjector
        // before EventRouter is created. The factory injection happens in Actor factories.
        // EventRouter creation is deferred to after factory injection via a lazy pattern
        // or we create it with default factory here and recreate if needed.

        // For now, create EventRouter with default factory (no DI)
        // It will be recreated if factory is injected later
        EventRouter = _eventRouterFactory.CreateEventRouter(
            agent.Id,
            SendEventToActorAsync,
            SendToSelfAsync,
            _logger
        );

        // Create event deduplicator (using improved MemoryCache implementation)
        EventDeduplicator = new MemoryCacheEventDeduplicator(
            new DeduplicationOptions
            {
                EventExpiration = TimeSpan.FromMinutes(5),
                MaxCachedEvents = 50_000,
                EnableAutoCleanup = true
            }
        );

        // Use AgentEventPublisherInjector to inject EventPublisher
        AgentEventPublisherInjector.InjectEventPublisher(agent, this);
    }

    /// <summary>
    /// Initialize EventRouter with current factory
    /// Called after EventRouterFactory is injected to recreate EventRouter with DI support
    /// </summary>
    private void InitializeEventRouter()
    {
        EventRouter = _eventRouterFactory.CreateEventRouter(
            Agent.Id,
            SendEventToActorAsync,
            SendToSelfAsync,
            _logger
        );
    }

    // ============ IGAgentActor Implementation ============

    public Guid Id => Agent.Id;

    public IGAgent GetAgent() => Agent;

    // ============ Hierarchy Management ============

    public virtual async Task AddChildAsync(Guid childId, CancellationToken ct = default)
    {
        await EventRouter.AddChildAsync(childId, ct);
    }

    public virtual async Task RemoveChildAsync(Guid childId, CancellationToken ct = default)
    {
        await EventRouter.RemoveChildAsync(childId, ct);
    }

    public virtual async Task SetParentAsync(Guid parentId, CancellationToken ct = default)
    {
        await EventRouter.SetParentAsync(parentId, ct);
    }

    public virtual async Task ClearParentAsync(CancellationToken ct = default)
    {
        await EventRouter.ClearParentAsync(ct);
    }

    public virtual Task<IReadOnlyList<Guid>> GetChildrenAsync()
    {
        return Task.FromResult(EventRouter.GetChildren());
    }

    public virtual Task<Guid?> GetParentAsync()
    {
        return Task.FromResult(EventRouter.GetParent());
    }

    // ============ Event Publishing (IEventPublisher Implementation) ============

    async Task<string> IEventPublisher.PublishEventAsync<TEvent>(
        TEvent evt,
        EventDirection direction,
        CancellationToken ct)
    {
        return await PublishEventAsync(evt, direction, ct);
    }

    // ============ Event Publishing and Routing ============

    public virtual async Task<string> PublishEventAsync<TEvent>(
        TEvent evt,
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default)
        where TEvent : IMessage
    {
        var stopwatch = Stopwatch.StartNew();

        // Use EventRouter to create EventEnvelope
        var envelope = EventRouter.CreateEventEnvelope(evt, direction);

        using var scope = LoggingScope.CreateAgentScope(
            Logger,
            Id,
            "PublishEvent",
            new Dictionary<string, object>
            {
                ["EventId"] = envelope.Id,
                ["EventType"] = typeof(TEvent).Name,
                ["Direction"] = direction.ToString()
            });

        Logger.LogDebug("Agent {AgentId} publishing event {EventId} with direction {Direction}",
            Id, envelope.Id, direction);

        try
        {
            // Route event through EventRouter
            await EventRouter.RouteEventAsync(envelope, ct);

            // Record publish metrics
            stopwatch.Stop();
            AgentMetrics.RecordEventPublished(typeof(TEvent).Name, Id.ToString());
            AgentMetrics.EventPublishLatency.Record(stopwatch.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("event.type", typeof(TEvent).Name),
                new KeyValuePair<string, object?>("agent.id", Id.ToString()),
                new KeyValuePair<string, object?>("direction", direction.ToString()));

            return envelope.Id;
        }
        catch (Exception ex)
        {
            // Record exception metrics
            AgentMetrics.RecordException(ex.GetType().Name, Id.ToString(), "ActorPublishEvent");
            throw;
        }
    }

    /// <summary>
    /// Handle received event (standard flow)
    /// </summary>
    public virtual async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        var eventType = envelope.Payload?.TypeUrl?.Split('/').LastOrDefault() ?? "Unknown";

        using var scope = LoggingScope.CreateEventHandlingScope(
            Logger,
            Id,
            envelope.Id,
            eventType,
            envelope.CorrelationId);

        Logger.LogDebug("Agent {AgentId} handling event {EventId}", Id, envelope.Id);

        // Use improved event deduplication mechanism
        if (!await EventDeduplicator.TryRecordEventAsync(envelope.Id))
        {
            Logger.LogDebug("Skipping duplicate event {EventId}", envelope.Id);
            AgentMetrics.EventsDropped.Add(1,
                new KeyValuePair<string, object?>("event.type", eventType),
                new KeyValuePair<string, object?>("agent.id", Id.ToString()),
                new KeyValuePair<string, object?>("reason", "Duplicate"));
            return;
        }

        // Use EventRouter to check if event should be processed
        if (!EventRouter.ShouldProcessEvent(envelope))
        {
            // Record skipped event
            AgentMetrics.EventsDropped.Add(1,
                new KeyValuePair<string, object?>("event.type", eventType),
                new KeyValuePair<string, object?>("agent.id", Id.ToString()),
                new KeyValuePair<string, object?>("reason", "FilteredOut"));
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Process event
            await ProcessEventAsync(envelope, ct);

            // Record processing metrics
            stopwatch.Stop();
            AgentMetrics.RecordEventHandled(eventType, Id.ToString(), stopwatch.ElapsedMilliseconds);

            // Determine if propagation should continue
            // If this is a self-published event (PublisherId == Id), it has already been propagated in RouteEventAsync
            // If this is a received event (PublisherId != Id), propagation should continue
            bool isInitialPublisher = envelope.PublisherId == Id.ToString();

            if (!isInitialPublisher)
            {
                // Received event, continue propagation (recursive propagation)
                Logger.LogDebug("Agent {AgentId} continuing propagation of event {EventId} from {PublisherId}",
                    Id, envelope.Id, envelope.PublisherId);
                await EventRouter.ContinuePropagationAsync(envelope, ct);
            }
        }
        catch (Exception ex)
        {
            // Record exception metrics
            AgentMetrics.RecordException(ex.GetType().Name, Id.ToString(), "ActorHandleEvent");
            throw;
        }
    }

    /// <summary>
    /// Process event (call Agent's handler)
    /// </summary>
    protected virtual async Task ProcessEventAsync(EventEnvelope envelope, CancellationToken ct)
    {
        try
        {
            // Directly call Agent's HandleEventAsync method (no reflection needed)
            await Agent.HandleEventAsync(envelope, ct);
            Logger.LogDebug("ProcessEventAsync: HandleEventAsync completed for event {EventId}", envelope.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling event {EventId} in agent {AgentId}",
                envelope.Id, Id);
            throw;
        }
    }

    // ============ Abstract Methods (Implemented by Subclass) ============

    /// <summary>
    /// Send event to self (subclass implements specific transport mechanism)
    /// </summary>
    protected abstract Task SendToSelfAsync(EventEnvelope envelope, CancellationToken ct);

    /// <summary>
    /// Send event to specified Actor (subclass implements specific transport mechanism)
    /// </summary>
    protected abstract Task SendEventToActorAsync(Guid actorId, EventEnvelope envelope, CancellationToken ct);

    /// <summary>
    /// Activate Actor
    /// </summary>
    public async Task ActivateAsync(CancellationToken ct = default)
    {
        // Reinitialize EventRouter with the injected factory (if any)
        // This ensures EventRouter uses the DI-injected store
        InitializeEventRouter();

        Logger.LogInformation("Activating agent actor {Id}", Id);

        await Agent.ActivateAsync(ct);

        // Load persisted hierarchy (if store is configured)
        await EventRouter.LoadHierarchyAsync(ct);

        // Call derived class activation logic
        await OnActivateAsync(ct);
    }

    /// <summary>
    /// Derived classes should override this method to perform specific activation logic
    /// </summary>
    protected virtual Task OnActivateAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Deactivate Actor
    /// </summary>
    public async Task DeactivateAsync(CancellationToken ct = default)
    {
        await Agent.DeactivateAsync(ct);

        await OnDeactivateAsync(ct);
    }

    /// <summary>
    /// Derived classes should override this method to perform specific deactivation logic
    /// </summary>
    protected virtual Task OnDeactivateAsync(CancellationToken ct) => Task.CompletedTask;
}