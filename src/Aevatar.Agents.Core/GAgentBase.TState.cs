using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Core.StateProtection;
using System.Diagnostics;
using Google.Protobuf;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core.EventSourcing;
using Google.Protobuf.WellKnownTypes;
using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core;

/// <summary>
/// Stateful agent base class
/// Extends the non-generic GAgentBase with state management capabilities
/// </summary>
/// <typeparam name="TState">Agent state type</typeparam>
public abstract class GAgentBase<TState> : GAgentBase, IStateGAgent<TState>
    where TState : class, IMessage<TState>, new()
{
    // ============ Fields ============

    private TState _state = new();

    /// <summary>
    /// State object - should only be modified within event handlers.
    /// Direct State assignment is protected, but individual property modifications cannot be intercepted
    /// for Protobuf-generated classes. Follow best practices:
    /// - Only modify State within [EventHandler] methods
    /// - Use events to trigger state changes
    /// - Direct modifications outside event handlers break the Actor model consistency
    /// </summary>
    protected TState State
    {
        get
        {
            // For development/debug builds, we can add a warning when accessing State outside handlers
#if DEBUG
            if (!StateProtectionContext.IsModifiable)
            {
                var callerMethod = new StackFrame(1)?.GetMethod()?.Name ?? "Unknown";
                if (!IsAllowedStateAccessMethod(callerMethod))
                {
                    Debug.WriteLine(
                        $"WARNING: State accessed from '{callerMethod}' outside event handler context. " +
                        "State should only be modified within event handlers.");
                }
            }
#endif
            return _state;
        }
        set
        {
            StateProtectionContext.EnsureModifiable("Direct State assignment");
            
            // If we have events in the stream (Version > 0), we are in Event Sourcing mode.
            // Direct state modification is not allowed in this mode to ensure consistency.
            // State should only be updated via RaiseEvent -> ApplyEvent.
            if (_currentVersion > 0)
            {
                throw new InvalidOperationException(
                    "Direct State modification is not allowed when Event Sourcing is active (Version > 0). " +
                    "Use RaiseEvent to modify state, or clear EventStore to reset version.");
            }

            _state = value;
        }
    }

    /// <summary>
    /// Validates if the current context allows State modification.
    /// Throws an exception if not in a valid context.
    /// </summary>
    protected void ValidateStateModificationContext(string operationName = "State modification")
    {
        StateProtectionContext.EnsureModifiable(operationName);
    }

#if DEBUG
    protected virtual bool IsAllowedStateAccessMethod(string methodName)
    {
        // Allow certain methods to access State without warning
        return methodName switch
        {
            nameof(GetState) => true,
            nameof(GetDescription) => true,
            nameof(GetDescriptionAsync) => true,
            nameof(OnActivateAsync) => true,
            nameof(ToString) => true,
            nameof(HandleEventAsync) => true,
            _ => false
        };
    }
#endif

    /// <summary>
    /// StateStore (injected by Actor layer)
    /// </summary>
    protected IStateStore<TState>? StateStore { get; set; }

    // ============ Event Sourcing Dependencies ============

    private static readonly IEventTypeResolver _defaultResolver = new ProtobufEventTypeResolver();

    /// <summary>
    /// Event type resolver (can be overridden or injected)
    /// </summary>
    protected virtual IEventTypeResolver EventTypeResolver => _defaultResolver;

    /// <summary>
    /// EventStore for persistence (supports injection)
    /// </summary>
    protected IEventStore? EventStore { get; set; }

    private long _currentVersion;

    // Batch event management
    private readonly List<AgentStateEvent> _pendingEvents = [];

    // ============ Constructors ============

    public GAgentBase()
    {
    }

    public GAgentBase(Guid id) : base(id)
    {
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        // 1. Load State from StateStore if available
        if (StateStore != null)
        {
            await StateStore.SaveAsync(Id, _state, ct);
        }

        // 2. Replay events from EventStore if available
        if (EventStore != null)
        {
            await ReplayEventsAsync(ct);
        }
    }

    // ============ IStateGAgent Implementation ============

    public TState GetState()
    {
        return _state.Clone(); // Return clone of state for read-only access
    }

    // ============ Event Handling with State Persistence ============

    /// <summary>
    /// Handle event with automatic state loading and saving
    /// Extends the base implementation to add state persistence
    /// </summary>
    public override async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        // 1. Load State (if StateStore is configured)
        if (StateStore != null)
        {
            // Allow state loading without protection during event handling setup
            using (StateProtectionContext.BeginEventHandlerScope())
            {
                _state = await StateStore.LoadAsync(Id, ct) ?? new TState();
            }
        }

        // 2. Call core event handling implementation
        await HandleEventCoreAsync(envelope, ct);

        // 3. Confirm Events (if EventStore is configured)
        if (EventStore != null)
        {
            await ConfirmEventsAsync(ct);
        }

        // 4. Save State (if StateStore is configured)
        if (StateStore != null)
        {
            await StateStore.SaveAsync(Id, _state, ct);
        }
    }

    // ============ Event Operations ============

    /// <summary>
    /// Stage event (does not persist immediately)
    /// </summary>
    protected void RaiseEvent<TEvent>(
        TEvent evt,
        Dictionary<string, string>? metadata = null)
        where TEvent : class, IMessage
    {
        var stateEvent = new AgentStateEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            EventType = evt.Descriptor.FullName,
            EventData = Any.Pack(evt),
            AgentId = Id.ToString(),
            Version = _currentVersion + _pendingEvents.Count + 1,
        };

        // Add metadata
        if (metadata != null)
        {
            foreach (var (key, value) in metadata)
            {
                stateEvent.Metadata[key] = value;
            }
        }

        _pendingEvents.Add(stateEvent);
    }

    /// <summary>
    /// Commit pending events (batch persist)
    /// </summary>
    protected async Task ConfirmEventsAsync(CancellationToken ct = default)
    {
        if (_pendingEvents.Count == 0) return;

        if (EventStore == null)
        {
            Logger?.LogWarning("EventStore not configured, events will not be persisted");
            _pendingEvents.Clear();
            return;
        }

        try
        {
            // Batch persist
            _currentVersion = await EventStore.AppendEventsAsync(
                Id,
                _pendingEvents,
                _currentVersion,
                ct);

            // Apply events to state
            foreach (var evt in _pendingEvents)
            {
                await ApplyEventInternalAsync(evt, ct);
            }

            _pendingEvents.Clear();

            // Check snapshot strategy
            if (SnapshotStrategy.ShouldCreateSnapshot(_currentVersion))
            {
                await CreateSnapshotInternalAsync(ct);
            }

            Logger?.LogDebug(
                "Confirmed {Count} events for agent {AgentId}, version: {Version}",
                _pendingEvents.Count, Id, _currentVersion);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error confirming events for agent {AgentId}", Id);
            throw;
        }
    }

    // ============ Pure Functional State Transition ============

    /// <summary>
    /// Pure functional state transition.
    /// Must be implemented if Event Sourcing is used.
    /// </summary>
    protected virtual void TransitionState(TState state, IMessage evt)
    {
        // Default implementation does nothing.
        // If EventStore is used, this MUST be overridden.
        if (EventStore != null)
        {
             Logger.LogWarning("TransitionState not implemented for {AgentType}, but EventStore is configured. State will not be updated from events.", GetType().Name);
        }
    }

    /// <summary>
    /// Apply event internally (optimized with type caching)
    /// </summary>
    private Task ApplyEventInternalAsync(AgentStateEvent evt, CancellationToken ct)
    {
        try
        {
            // Get or build type cache
            var typeInfo = EventTypeResolver.Resolve(evt.EventData.TypeUrl, this.GetType().Assembly);

            if (typeInfo == null)
            {
                Logger?.LogWarning(
                    "Failed to resolve type from TypeUrl {TypeUrl}",
                    evt.EventData.TypeUrl);
                return Task.CompletedTask;
            }

            // Parse message
            var message = typeInfo.Parser.ParseFrom(evt.EventData.Value);

            if (message == null)
            {
                Logger?.LogWarning("Failed to parse event {TypeName}", typeInfo.Type.Name);
                return Task.CompletedTask;
            }

            Logger?.LogDebug(
                "Applying event {TypeName} version {Version} to agent {AgentId}",
                typeInfo.Type.Name, evt.Version, Id);

            // Clone state
            var newState = State.Clone();

            // Transition state
            TransitionState(newState, message);

            // Update state
            SetState(newState);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex,
                "Error applying event {EventType} version {Version}",
                evt.EventType, evt.Version);
            throw;
        }
    }

    private void SetState(TState newState)
    {
        // Directly set the field to bypass the EventStore check in the property setter.
        // This method is only called by the framework (ApplyEventInternalAsync, ReplayEventsAsync),
        // so it is safe to update the state here.
        _state = newState;
    }

    // ============ Event Replay ============

    /// <summary>
    /// Replay events from event store
    /// </summary>
    public async Task ReplayEventsAsync(CancellationToken ct = default)
    {
        if (EventStore == null)
        {
            Logger?.LogWarning("EventStore not configured, cannot replay events");
            return;
        }

        Logger.LogInformation("Replaying events for agent {AgentId}, starting from version {CurrentVersion}", Id,
            _currentVersion);

        // Step 1: Load latest snapshot
        var snapshot = await EventStore.GetLatestSnapshotAsync(Id, ct);
        if (snapshot != null)
        {
            Logger.LogInformation(
                "Loading snapshot at version {Version} for agent {AgentId}",
                snapshot.Version, Id);

            var snapshotState = snapshot.StateData.Unpack<TState>();
            SetState(snapshotState);
            _currentVersion = snapshot.Version;
            Logger.LogInformation("Snapshot loaded, current version: {Version}", _currentVersion);
        }

        // Step 2: Replay events after snapshot
        var fromVersion = _currentVersion + 1;
        var events = await EventStore.GetEventsAsync(
            Id,
            fromVersion: fromVersion,
            ct: ct);

        if (!events.Any())
        {
            return;
        }

        // Step 3: Apply events
        foreach (var evt in events.OrderBy(e => e.Version))
        {
            await ApplyEventInternalAsync(evt, ct);
            _currentVersion = evt.Version;
        }

        Logger.LogInformation(
            "Replayed {Count} events for agent {AgentId}, current version: {Version}",
            events.Count, Id, _currentVersion);
    }

    // ============ Snapshot Operations ============

    protected virtual ISnapshotStrategy SnapshotStrategy =>
        new IntervalSnapshotStrategy(100);

    private async Task CreateSnapshotInternalAsync(CancellationToken ct)
    {
        if (EventStore == null) return;

        var snapshot = new AgentSnapshot
        {
            Version = _currentVersion,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            StateData = Any.Pack(State)
        };

        await EventStore.SaveSnapshotAsync(Id, snapshot, ct);

        Logger?.LogInformation(
            "Snapshot created for agent {AgentId} at version {Version}",
            Id, _currentVersion);
    }

    public async Task CreateSnapshotAsync(CancellationToken ct = default)
    {
        await CreateSnapshotInternalAsync(ct);
    }

    public long GetCurrentVersion() => _currentVersion;
}