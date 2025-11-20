using System.Collections.Concurrent;
using System.Reflection;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Core.StateProtection;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

/// <summary>
/// AI Agent with Event Sourcing capabilities.
/// Combines AI decision making with event-sourced state management.
/// AI代理与事件溯源能力的结合。
/// 将AI决策与基于事件的状态管理相结合。
/// </summary>
/// <typeparam name="TState">The business state type (must be protobuf)</typeparam>
/// <typeparam name="TConfig">The configuration type (must be protobuf)</typeparam>
public abstract class AIGAgentBaseWithEventSourcing<TState, TConfig> : AIGAgentBase<TState, TConfig>
    where TState : class, IMessage<TState>, new()
    where TConfig : class, IMessage<TConfig>, new()
{
    #region Type Cache (Performance Optimization)

    /// <summary>
    /// Cache for Protobuf type parsers to avoid reflection on every event
    /// Shared across all instances for memory efficiency
    /// </summary>
    private static readonly ConcurrentDictionary<string, TypeParserCache> _typeCache = new();

    private sealed class TypeParserCache
    {
        public required System.Type Type { get; init; }
        public required MessageParser Parser { get; init; }
        public static int EstimatedSizeBytes => 150;
    }

    #endregion

    #region Event Sourcing Fields

    /// <summary>
    /// EventStore for persistence
    /// </summary>
    protected IEventStore? EventStore { get; set; }

    private long _currentVersion;
    private readonly List<AgentStateEvent> _pendingEvents = [];

    #endregion

    #region Constructors

    /// <summary>
    /// Default constructor - ID will be set by factory if needed
    /// </summary>
    public AIGAgentBaseWithEventSourcing()
    {
    }

    /// <summary>
    /// Constructor with ID
    /// </summary>
    public AIGAgentBaseWithEventSourcing(Guid id) : base(id)
    {
    }

    #endregion

    #region Event Operations

    /// <summary>
    /// Stage an event for batch commit (does not persist immediately).
    /// Thread-safe only within the agent's execution context.
    /// 暂存事件以批量提交（不立即持久化）。
    /// 仅在代理执行上下文中线程安全。
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
    /// 提交挂起的事件并应用状态转换。
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

    #region AI-Specific Event Sourcing

    /// <summary>
    /// Record an AI decision as an event.
    /// 将AI决策记录为事件。
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
            Model = Configuration.Model,
            Temperature = Configuration.Temperature,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        // Add AI-specific metadata
        var eventMetadata = metadata ?? new Dictionary<string, string>();
        eventMetadata["ai_model"] = Configuration.Model;
        eventMetadata["ai_temperature"] = Configuration.Temperature.ToString();

        RaiseEvent(aiEvent, eventMetadata);
    }

    /// <summary>
    /// Override ChatAsync to record AI interactions as events.
    /// 重写ChatAsync以将AI交互记录为事件。
    /// </summary>
    public override async Task<ChatResponse> ChatAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
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

    #region State Transitions

    /// <summary>
    /// Pure functional state transition.
    /// Framework automatically clones the state before calling this method.
    /// 纯函数状态转换。
    /// 框架在调用此方法前自动克隆状态。
    /// </summary>
    protected abstract void TransitionState(TState state, IMessage evt);

    /// <summary>
    /// Apply event to state internally.
    /// 内部应用事件到状态。
    /// </summary>
    private async Task ApplyEventInternalAsync(AgentStateEvent evt, CancellationToken ct)
    {
        try
        {
            var simpleTypeName = ExtractSimpleTypeName(evt.EventData.TypeUrl);
            var cache = GetOrBuildTypeCache(simpleTypeName);
            
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

    #region Event Replay

    /// <summary>
    /// Replay events from event store with snapshot optimization.
    /// 从事件存储重放事件，支持快照优化。
    /// </summary>
    public async Task ReplayEventsAsync(CancellationToken ct = default)
    {
        if (EventStore == null)
        {
            Logger?.LogWarning("EventStore not configured, cannot replay events");
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

            var snapshotState = snapshot.StateData.Unpack<TState>();
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

    #endregion

    #region Snapshot Management

    /// <summary>
    /// Snapshot strategy (can be overridden)
    /// </summary>
    protected virtual ISnapshotStrategy SnapshotStrategy =>
        new IntervalSnapshotStrategy(100);

    /// <summary>
    /// Auto-confirm events after AI operations
    /// </summary>
    protected virtual bool AutoConfirmEvents => false;

    /// <summary>
    /// Create snapshot internally
    /// </summary>
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

    /// <summary>
    /// Manual snapshot creation
    /// </summary>
    public async Task CreateSnapshotAsync(CancellationToken ct = default)
    {
        await CreateSnapshotInternalAsync(ct);
    }

    #endregion

    #region Helper Methods

    private static string ExtractSimpleTypeName(string typeUrl)
    {
        var fullTypeName = typeUrl.Substring(typeUrl.LastIndexOf('/') + 1);
        return fullTypeName.Contains('.')
            ? fullTypeName.Substring(fullTypeName.LastIndexOf('.') + 1)
            : fullTypeName;
    }

    private TypeParserCache? GetOrBuildTypeCache(string simpleTypeName)
    {
        if (_typeCache.TryGetValue(simpleTypeName, out var cache))
        {
            return cache;
        }

        cache = BuildTypeCache(simpleTypeName);
        if (cache != null)
        {
            _typeCache[simpleTypeName] = cache;
            Logger?.LogInformation(
                "Type {TypeName} cached for AI agent. Total cached types: {Count}",
                simpleTypeName, _typeCache.Count);
        }

        return cache;
    }

    private TypeParserCache? BuildTypeCache(string simpleTypeName)
    {
        try
        {
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

            if (parser == null)
            {
                Logger?.LogWarning(
                    "Parser property not found for type {TypeName}",
                    matchingType.FullName);
                return null;
            }

            return new TypeParserCache
            {
                Type = matchingType,
                Parser = parser
            };
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error building type cache for {TypeName}", simpleTypeName);
            return null;
        }
    }

    #endregion

    #region Lifecycle

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

    #region Monitoring

    /// <summary>
    /// Get current event sourcing version
    /// </summary>
    public long GetCurrentVersion() => _currentVersion;

    /// <summary>
    /// Get number of pending events
    /// </summary>
    public int GetPendingEventCount() => _pendingEvents.Count;

    /// <summary>
    /// Get cached type count for monitoring
    /// </summary>
    public static int CachedTypeCount => _typeCache.Count;

    /// <summary>
    /// Clear type cache (for testing)
    /// </summary>
    public static void ClearTypeCache() => _typeCache.Clear();

    #endregion
}
