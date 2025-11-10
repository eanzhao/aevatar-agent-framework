using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace Aevatar.Agents.Core.EventSourcing;

/// <summary>
/// Agent base class with EventSourcing support
/// State changes are persisted as events and can be replayed
/// 
/// Features:
/// - Batch event commit (borrowed from JournaledGrain)
/// - Pure functional state transitions
/// - Snapshot optimization
/// - Metadata support
/// </summary>
public abstract class GAgentBaseWithEventSourcing<TState> : GAgentBase<TState>
    where TState : class, IMessage<TState>, new()
{
    private IEventStore? _eventStore;
    private long _currentVersion = 0;

    // Batch event management (borrowed from JournaledGrain)
    private readonly List<AgentStateEvent> _pendingEvents = new();

    protected GAgentBaseWithEventSourcing(
        Guid id,
        IEventStore? eventStore = null,
        ILogger? logger = null)
        : base(id, logger)
    {
        _eventStore = eventStore;
    }

    // ========== Event Operations (Borrowed from JournaledGrain) ==========

    /// <summary>
    /// Stage event (does not persist immediately)
    /// Borrowed from: JournaledGrain.RaiseEvent()
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
    /// Borrowed from: JournaledGrain.ConfirmEvents()
    /// </summary>
    protected async Task ConfirmEventsAsync(CancellationToken ct = default)
    {
        if (_pendingEvents.Count == 0) return;

        if (_eventStore == null)
        {
            Logger?.LogWarning("EventStore not configured, events will not be persisted");
            _pendingEvents.Clear();
            return;
        }

        try
        {
            // Batch persist
            _currentVersion = await _eventStore.AppendEventsAsync(
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

    // ========== Pure Functional State Transition ==========

    /// <summary>
    /// Pure functional state transition
    /// Borrowed from: JournaledGrain.TransitionState()
    /// 
    /// Given the same state + event, always produces the same result
    /// No side effects, easy to test
    /// </summary>
    /// <param name="state">Current state (immutable)</param>
    /// <param name="evt">Event</param>
    /// <returns>New state</returns>
    protected abstract TState TransitionState(TState state, IMessage evt);

    /// <summary>
    /// Apply event internally (uses pure function)
    /// </summary>
    private Task ApplyEventInternalAsync(AgentStateEvent evt, CancellationToken ct)
    {
        try
        {
            // ✅ Extract type name from TypeUrl (format: "type.googleapis.com/PackageName.MessageName")
            var typeUrl = evt.EventData.TypeUrl;
            var fullTypeName = typeUrl.Substring(typeUrl.LastIndexOf('/') + 1);
            
            // Extract simple type name (last part after '.')
            var simpleTypeName = fullTypeName.Contains('.') 
                ? fullTypeName.Substring(fullTypeName.LastIndexOf('.') + 1)
                : fullTypeName;
            
            Logger?.LogDebug("Unpacking event {SimpleTypeName} (full: {FullTypeName}) from TypeUrl {TypeUrl}", 
                simpleTypeName, fullTypeName, typeUrl);
            
            // ✅ Lookup type in the same assembly as the agent (this class)
            // Match by simple name since Protobuf package names don't match C# namespaces
            var assembly = this.GetType().Assembly;
            var matchingType = assembly.GetTypes()
                .FirstOrDefault(t => t.Name == simpleTypeName && typeof(IMessage).IsAssignableFrom(t));
            
            if (matchingType == null)
            {
                Logger?.LogWarning("Unknown event type from TypeUrl: {TypeUrl}. Cannot find type {TypeName} in assembly {Assembly}", 
                    typeUrl, simpleTypeName, assembly.FullName);
                return Task.CompletedTask;
            }
            
            Logger?.LogDebug("Found matching type: {FullName}", matchingType.FullName);
            
            // ✅ Get Parser property
            var parser = matchingType.GetProperty("Parser", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                ?.GetValue(null);
                
            if (parser == null)
            {
                Logger?.LogWarning("Cannot find Parser property for type {TypeName}", matchingType.FullName);
                return Task.CompletedTask;
            }
            
            // ✅ Parse from bytes
            var parseFromMethod = parser.GetType().GetMethod("ParseFrom", new[] { typeof(byte[]) });
            if (parseFromMethod == null)
            {
                Logger?.LogWarning("Cannot find ParseFrom method for parser of type {TypeName}", matchingType.FullName);
                return Task.CompletedTask;
            }
            
            var message = parseFromMethod.Invoke(parser, new object[] { evt.EventData.Value.ToByteArray() }) as IMessage;
            
            if (message == null)
            {
                Logger?.LogWarning("Failed to parse event {TypeName} - ParseFrom returned null", matchingType.FullName);
                return Task.CompletedTask;
            }
            
            Logger?.LogDebug("Successfully unpacked event: {MessageType}", message.GetType().Name);

            // ✅ Pure function call
            var newState = TransitionState(State, message);

            // ✅ Update state
            SetStateInternal(newState);

            Logger?.LogDebug(
                "Applied event {TypeName} version {Version} to agent {AgentId}",
                simpleTypeName, evt.Version, Id);

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

    /// <summary>
    /// Set state internally (workaround for readonly State field)
    /// </summary>
    private void SetStateInternal(TState newState)
    {
        // Use reflection to set readonly field
        var stateField = typeof(GAgentBase<TState>).GetField("_state", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (stateField != null)
        {
            stateField.SetValue(this, newState);
        }
    }

    // ========== Event Replay ==========

    /// <summary>
    /// Replay events from event store (with snapshot optimization)
    /// </summary>
    public async Task ReplayEventsAsync(CancellationToken ct = default)
    {
        if (_eventStore == null)
        {
            Logger?.LogWarning("EventStore not configured, cannot replay events");
            return;
        }

        Logger?.LogInformation("Replaying events for agent {AgentId}", Id);

        // Step 1: Load latest snapshot
        var snapshot = await _eventStore.GetLatestSnapshotAsync(Id, ct);
        if (snapshot != null)
        {
            Logger?.LogInformation(
                "Loading snapshot at version {Version} for agent {AgentId}",
                snapshot.Version, Id);

            var snapshotState = snapshot.StateData.Unpack<TState>();
            SetStateInternal(snapshotState);
            _currentVersion = snapshot.Version;
        }

        // Step 2: Replay events after snapshot
        var events = await _eventStore.GetEventsAsync(
            Id,
            fromVersion: _currentVersion + 1,
            ct: ct);

        if (!events.Any())
        {
            Logger?.LogInformation("No new events to replay for agent {AgentId}", Id);
            return;
        }

        // Step 3: Apply events
        foreach (var evt in events.OrderBy(e => e.Version))
        {
            await ApplyEventInternalAsync(evt, ct);
            _currentVersion = evt.Version;
        }

        Logger?.LogInformation(
            "Replayed {Count} events for agent {AgentId}, current version: {Version}",
            events.Count, Id, _currentVersion);
    }

    // ========== Snapshot Operations ==========

    /// <summary>
    /// Snapshot strategy (can be overridden)
    /// </summary>
    protected virtual ISnapshotStrategy SnapshotStrategy =>
        new IntervalSnapshotStrategy(100);

    /// <summary>
    /// Create snapshot internally
    /// </summary>
    private async Task CreateSnapshotInternalAsync(CancellationToken ct)
    {
        if (_eventStore == null) return;

        var snapshot = new AgentSnapshot
        {
            Version = _currentVersion,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            StateData = Any.Pack(State)
        };

        await _eventStore.SaveSnapshotAsync(Id, snapshot, ct);

        Logger?.LogInformation(
            "Snapshot created for agent {AgentId} at version {Version}",
            Id, _currentVersion);
    }

    /// <summary>
    /// Manual snapshot creation (for API)
    /// </summary>
    public async Task CreateSnapshotAsync(CancellationToken ct = default)
    {
        await CreateSnapshotInternalAsync(ct);
    }

    // ========== Helper Methods ==========

    /// <summary>
    /// Deep copy protection (borrowed from JournaledGrain)
    /// Prevents external code from accidentally modifying state
    /// Note: Not used in current implementation due to readonly State field
    /// Kept for reference
    /// </summary>
    private TState DeepCopy(TState state)
    {
        // Use Protobuf serialization for deep copy
        var bytes = state.ToByteArray();
        
        // Get parser using reflection
        var parserProperty = typeof(TState).GetProperty("Parser", 
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        
        if (parserProperty == null)
        {
            throw new InvalidOperationException($"Cannot find Parser property for type {typeof(TState).Name}");
        }

        var parser = parserProperty.GetValue(null) as MessageParser<TState>;
        if (parser == null)
        {
            throw new InvalidOperationException($"Cannot get parser for type {typeof(TState).Name}");
        }

        return parser.ParseFrom(bytes);
    }

    /// <summary>
    /// Get current version (for monitoring)
    /// </summary>
    public long GetCurrentVersion() => _currentVersion;

    /// <summary>
    /// Set event store (for DI scenarios)
    /// </summary>
    public void SetEventStore(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    // ========== Lifecycle ==========

    /// <summary>
    /// Override activation to auto-replay events
    /// </summary>
    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        // Auto-replay events
        if (_eventStore != null)
        {
        await ReplayEventsAsync(ct);
        }
    }
}

// ========== Snapshot Strategies ==========

/// <summary>
/// Snapshot strategy interface
/// </summary>
public interface ISnapshotStrategy
{
    bool ShouldCreateSnapshot(long version);
}

/// <summary>
/// Interval-based snapshot strategy
/// </summary>
public class IntervalSnapshotStrategy : ISnapshotStrategy
{
    private readonly long _interval;

    public IntervalSnapshotStrategy(long interval)
    {
        _interval = interval;
    }

    public bool ShouldCreateSnapshot(long version)
    {
        return version % _interval == 0;
    }
}

/// <summary>
/// Hybrid snapshot strategy (interval + time)
/// </summary>
public class HybridSnapshotStrategy : ISnapshotStrategy
{
    private readonly long _interval;
    private readonly TimeSpan _timeSpan;
    private DateTime _lastSnapshotTime = DateTime.UtcNow;

    public HybridSnapshotStrategy(long interval, TimeSpan timeSpan)
    {
        _interval = interval;
        _timeSpan = timeSpan;
    }

    public bool ShouldCreateSnapshot(long version)
    {
        // Strategy 1: Every N events
        if (version % _interval == 0) return true;

        // Strategy 2: Time-based
        if ((DateTime.UtcNow - _lastSnapshotTime) > _timeSpan)
        {
            _lastSnapshotTime = DateTime.UtcNow;
            return true;
        }

        return false;
    }
}
