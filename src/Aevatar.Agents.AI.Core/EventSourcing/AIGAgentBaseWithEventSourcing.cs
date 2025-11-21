using System.Collections.Concurrent;
using System.Reflection;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Core.StateProtection;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.EventSourcing;

/// <summary>
/// AI Agent with Event Sourcing capabilities (Non-generic).
/// Inherits from AIGAgentBase to provide standard AI capabilities, 
/// and implements Event Sourcing logic internally (ported from GAgentBaseWithEventSourcing).
/// </summary>
public abstract class AIGAgentBaseWithEventSourcing : AIGAgentBase
{
    // =========================================================================================
    // Part 1: Event Sourcing Infrastructure (Ported from GAgentBaseWithEventSourcing)
    // =========================================================================================

    #region Event Sourcing Dependencies & Fields

    private static readonly IEventTypeResolver _defaultResolver = new ProtobufEventTypeResolver();

    /// <summary>
    /// Event type resolver
    /// </summary>
    protected virtual IEventTypeResolver EventTypeResolver => _defaultResolver;

    /// <summary>
    /// EventStore for persistence
    /// </summary>
    protected IEventStore? EventStore { get; set; }

    private long _currentVersion;
    private readonly List<AgentStateEvent> _pendingEvents = [];

    #endregion

    #region Type Cache (Performance Optimization)

    private static readonly ConcurrentDictionary<string, TypeParserCache> _typeCache = new();

    private sealed class TypeParserCache
    {
        public required System.Type Type { get; init; }
        public required MessageParser Parser { get; init; }
    }

    #endregion

    public AIGAgentBaseWithEventSourcing()
    {
    }

    public AIGAgentBaseWithEventSourcing(Guid id) : base(id)
    {
    }

    #region Event Operations

    /// <summary>
    /// Stage an event for batch commit (does not persist immediately).
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
    /// Commit pending events and apply state transitions.
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
                "Confirmed {Count} events for AI agent {AgentId}, version: {Version}",
                _pendingEvents.Count, Id, _currentVersion);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error confirming events for AI agent {AgentId}", Id);
            throw;
        }
    }

    #endregion

    #region State Transitions (Abstract)

    /// <summary>
    /// Pure functional state transition.
    /// Framework automatically clones the state before calling this method.
    /// </summary>
    protected abstract void TransitionState(AevatarAIAgentState state, IMessage evt);

    #endregion

    #region Internal Event Application

    private async Task ApplyEventInternalAsync(AgentStateEvent evt, CancellationToken ct)
    {
        try
        {
            var simpleTypeName = ExtractSimpleTypeName(evt.EventData.TypeUrl);
            var cache = GetOrBuildTypeCache(simpleTypeName, evt.EventData.TypeUrl);

            if (cache == null)
            {
                Logger?.LogWarning(
                    "Failed to resolve type {TypeName} from TypeUrl {TypeUrl}",
                    simpleTypeName, evt.EventData.TypeUrl);
                return;
            }

            var message = cache.Parser.ParseFrom(evt.EventData.Value);
            if (message == null)
            {
                Logger?.LogWarning("Failed to parse event {TypeName}", simpleTypeName);
                return;
            }

            Logger?.LogDebug(
                "Applying event {TypeName} version {Version} to AI agent {AgentId}",
                simpleTypeName, evt.Version, Id);

            // Clone state and apply transition
            var newState = State.Clone();
            TransitionState(newState, message);

            // Update state using InitializationScope
            using var _ = StateProtectionContext.BeginInitializationScope();
            State = newState;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex,
                "Error applying event {EventType} version {Version}",
                evt.EventType, evt.Version);
            throw;
        }
    }

    #endregion

    #region Event Replay & Snapshot

    /// <summary>
    /// Replay events from event store with snapshot optimization.
    /// </summary>
    public async Task ReplayEventsAsync(CancellationToken ct = default)
    {
        if (EventStore == null)
        {
            Logger.LogWarning("EventStore not configured, cannot replay events");
            return;
        }

        Logger.LogInformation("Replaying events for AI agent {AgentId}, starting from version {CurrentVersion}",
            Id, _currentVersion);

        // Load latest snapshot
        var snapshot = await EventStore.GetLatestSnapshotAsync(Id, ct);
        if (snapshot != null)
        {
            Logger.LogInformation(
                "Loading snapshot at version {Version} for AI agent {AgentId}",
                snapshot.Version, Id);

            var snapshotState = snapshot.StateData.Unpack<AevatarAIAgentState>();
            using var _ = StateProtectionContext.BeginInitializationScope();
            State = snapshotState;
            _currentVersion = snapshot.Version;
        }

        // Replay events after snapshot
        var fromVersion = _currentVersion + 1;
        var events = await EventStore.GetEventsAsync(Id, fromVersion: fromVersion, ct: ct);

        foreach (var evt in events.OrderBy(e => e.Version))
        {
            await ApplyEventInternalAsync(evt, ct);
            _currentVersion = evt.Version;
        }

        Logger.LogInformation(
            "Replayed events for AI agent {AgentId}, current version: {Version}",
            Id, _currentVersion);
    }

    protected virtual ISnapshotStrategy SnapshotStrategy => new IntervalSnapshotStrategy(100);

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
            "Snapshot created for AI agent {AgentId} at version {Version}",
            Id, _currentVersion);
    }

    public async Task CreateSnapshotAsync(CancellationToken ct = default)
    {
        await CreateSnapshotInternalAsync(ct);
    }

    #endregion

    #region Helpers (Type Cache & Resolution)

    private static string ExtractSimpleTypeName(string typeUrl)
    {
        var fullTypeName = typeUrl.Substring(typeUrl.LastIndexOf('/') + 1);
        return fullTypeName.Contains('.')
            ? fullTypeName.Substring(fullTypeName.LastIndexOf('.') + 1)
            : fullTypeName;
    }

    private TypeParserCache? GetOrBuildTypeCache(string simpleTypeName, string typeUrl)
    {
        if (_typeCache.TryGetValue(simpleTypeName, out var cache))
        {
            return cache;
        }

        cache = BuildTypeCache(simpleTypeName, typeUrl);
        if (cache != null)
        {
            _typeCache[simpleTypeName] = cache;
        }

        return cache;
    }

    private TypeParserCache? BuildTypeCache(string simpleTypeName, string typeUrl)
    {
        try
        {
            // Try to resolve using the resolver first
            var typeInfo = EventTypeResolver.Resolve(typeUrl, this.GetType().Assembly);

            if (typeInfo == null)
            {
                // Fallback to simple name search if resolver fails (legacy behavior)
                var assembly = this.GetType().Assembly;
                var matchingType = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == simpleTypeName && typeof(IMessage).IsAssignableFrom(t));

                if (matchingType == null)
                {
                    Logger?.LogWarning(
                        "Type {TypeName} not found in assembly {Assembly}",
                        simpleTypeName, assembly.FullName);
                    return null;
                }

                var parser = matchingType
                    .GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) as MessageParser;

                if (parser == null) return null;

                return new TypeParserCache { Type = matchingType, Parser = parser };
            }

            return new TypeParserCache
            {
                Type = typeInfo.Type,
                Parser = typeInfo.Parser
            };
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error building type cache for {TypeName}", simpleTypeName);
            return null;
        }
    }

    #endregion

    #region Lifecycle Override

    /// <summary>
    /// Override activation to auto-replay events
    /// </summary>
    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        // Auto-replay events if EventStore is configured
        if (EventStore != null)
        {
            await ReplayEventsAsync(ct);
        }
    }

    #endregion

    // =========================================================================================
    // Part 2: AI Specific Logic (Integrated with Event Sourcing)
    // =========================================================================================

    #region AI Properties & Config

    /// <summary>
    /// Auto-confirm events after AI operations
    /// </summary>
    protected virtual bool AutoConfirmEvents => false;

    #endregion

    #region Chat Logic Overrides

    /// <summary>
    /// Override ChatAsync to record AI interactions as events.
    /// </summary>
    public override async Task<ChatResponse> ChatAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        // Call base implementation to get AI response (LLM interaction)
        var response = await base.ChatAsync(request, cancellationToken);

        // Record the AI decision as an event
        RaiseAIDecision(
            request.Message,
            response.Content,
            response.Usage?.TotalTokens ?? 0,
            new Dictionary<string, string>
            {
                ["request_id"] = request.RequestId,
                ["chat_type"] = "sync"
            });

        // Auto-confirm if configured
        if (AutoConfirmEvents)
        {
            await ConfirmEventsAsync(cancellationToken);
        }

        return response;
    }

    #endregion

    #region AI Event Helpers

    /// <summary>
    /// Record an AI decision as an event.
    /// </summary>
    protected void RaiseAIDecision(
        string prompt,
        string response,
        int tokensUsed,
        Dictionary<string, string>? metadata = null)
    {
        var aiEvent = new AIDecisionEvent
        {
            Prompt = prompt,
            Response = response,
            TokensUsed = tokensUsed,
            Model = Config.Model,
            Temperature = Config.Temperature,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        // Add AI-specific metadata
        var eventMetadata = metadata ?? new Dictionary<string, string>();
        eventMetadata["ai_model"] = Config.Model;
        eventMetadata["ai_temperature"] = Config.Temperature.ToString();

        RaiseEvent(aiEvent, eventMetadata);
    }

    #endregion

    #region Monitoring

    public long GetCurrentVersion() => _currentVersion;
    public int GetPendingEventCount() => _pendingEvents.Count;
    public static int CachedTypeCount => _typeCache.Count;
    public static void ClearTypeCache() => _typeCache.Clear();

    #endregion
}