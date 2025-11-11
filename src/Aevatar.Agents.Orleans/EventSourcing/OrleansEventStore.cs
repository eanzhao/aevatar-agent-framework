using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Google.Protobuf;

namespace Aevatar.Agents.Orleans.EventSourcing;

/// <summary>
/// Orleans-based EventStore implementation using GrainStorage
/// Thread-safe with optimistic concurrency control
/// </summary>
public class OrleansEventStore : IEventStore
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<OrleansEventStore> _logger;

    public OrleansEventStore(
        IGrainFactory grainFactory,
        ILogger<OrleansEventStore> logger)
    {
        _grainFactory = grainFactory;
        _logger = logger;
    }

    // ========== Event Operations ==========

    public async Task<long> AppendEventsAsync(
        Guid agentId,
        IEnumerable<AgentStateEvent> events,
        long expectedVersion,
        CancellationToken ct = default)
    {
        var storageGrain = _grainFactory.GetGrain<IEventStorageGrain>(agentId);
        return await storageGrain.AppendEventsAsync(events.ToList(), expectedVersion);
    }

    public async Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        Guid agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        CancellationToken ct = default)
    {
        var storageGrain = _grainFactory.GetGrain<IEventStorageGrain>(agentId);
        return await storageGrain.GetEventsAsync(fromVersion, toVersion, maxCount);
    }

    public async Task<long> GetLatestVersionAsync(Guid agentId, CancellationToken ct = default)
    {
        var storageGrain = _grainFactory.GetGrain<IEventStorageGrain>(agentId);
        return await storageGrain.GetLatestVersionAsync();
    }

    // ========== Snapshot Operations ==========

    public async Task SaveSnapshotAsync(Guid agentId, AgentSnapshot snapshot, CancellationToken ct = default)
    {
        var storageGrain = _grainFactory.GetGrain<IEventStorageGrain>(agentId);
        await storageGrain.SaveSnapshotAsync(snapshot);
    }

    public async Task<AgentSnapshot?> GetLatestSnapshotAsync(Guid agentId, CancellationToken ct = default)
    {
        var storageGrain = _grainFactory.GetGrain<IEventStorageGrain>(agentId);
        return await storageGrain.GetLatestSnapshotAsync();
    }
}

/// <summary>
/// Grain interface for event storage
/// </summary>
public interface IEventStorageGrain : IGrainWithGuidKey
{
    Task<long> AppendEventsAsync(List<AgentStateEvent> events, long expectedVersion);
    Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(long? fromVersion, long? toVersion, int? maxCount);
    Task<long> GetLatestVersionAsync();
    Task SaveSnapshotAsync(AgentSnapshot snapshot);
    Task<AgentSnapshot?> GetLatestSnapshotAsync();
}

/// <summary>
/// State for event storage (separate collection)
/// </summary>
[GenerateSerializer]
public class EventsState
{
    /// <summary>
    /// Events stored as serialized Protobuf bytes
    /// </summary>
    [Id(0)]
    public List<byte[]> Events { get; set; } = new();
}

/// <summary>
/// State for snapshot storage (separate collection)
/// </summary>
[GenerateSerializer]
public class SnapshotState
{
    /// <summary>
    /// Latest snapshot stored as serialized Protobuf bytes
    /// </summary>
    [Id(0)]
    public byte[]? Snapshot { get; set; }
    
    /// <summary>
    /// Snapshot version
    /// </summary>
    [Id(1)]
    public long Version { get; set; }
}

/// <summary>
/// Grain implementation for event storage
/// Uses TWO separate GrainStorage collections for events and snapshots
/// </summary>
public class EventStorageGrain : Grain, IEventStorageGrain
{
    private readonly IPersistentState<EventsState> _eventsStorage;
    private readonly IPersistentState<SnapshotState> _snapshotStorage;
    private readonly ILogger<EventStorageGrain> _logger;

    public EventStorageGrain(
        [PersistentState("events", "EventStoreStorage")] IPersistentState<EventsState> eventsStorage,
        [PersistentState("snapshots", "EventStoreStorage")] IPersistentState<SnapshotState> snapshotStorage,
        ILogger<EventStorageGrain> logger)
    {
        _eventsStorage = eventsStorage;
        _snapshotStorage = snapshotStorage;
        _logger = logger;
    }

    public async Task<long> AppendEventsAsync(List<AgentStateEvent> events, long expectedVersion)
    {
        // Deserialize events from storage to check version
        var storedEvents = _eventsStorage.State.Events
            .Select(bytes => AgentStateEvent.Parser.ParseFrom(bytes))
            .ToList();
        
        // Optimistic concurrency check
        var currentVersion = storedEvents.Any() 
            ? storedEvents.Max(e => e.Version) 
            : 0;

        if (currentVersion != expectedVersion)
        {
            _logger.LogWarning(
                "Concurrency conflict for grain {GrainId}: expected {Expected}, got {Current}",
                this.GetPrimaryKey(), expectedVersion, currentVersion);

            throw new InvalidOperationException(
                $"Concurrency conflict: expected version {expectedVersion}, got {currentVersion}");
        }

        var newVersion = currentVersion;
        foreach (var evt in events)
        {
            evt.Version = ++newVersion;
            _eventsStorage.State.Events.Add(evt.ToByteArray());
        }

        // Persist to events storage
        await _eventsStorage.WriteStateAsync();

        _logger.LogDebug(
            "Appended {Count} events to grain {GrainId}, new version: {Version}",
            events.Count, this.GetPrimaryKey(), newVersion);

        return newVersion;
    }

    public Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        long? fromVersion, 
        long? toVersion, 
        int? maxCount)
    {
        var events = _eventsStorage.State.Events
            .Select(bytes => AgentStateEvent.Parser.ParseFrom(bytes))
            .AsEnumerable();

        // Range query
        if (fromVersion.HasValue)
            events = events.Where(e => e.Version >= fromVersion.Value);

        if (toVersion.HasValue)
            events = events.Where(e => e.Version <= toVersion.Value);

        // Order by version
        events = events.OrderBy(e => e.Version);

        // Pagination
        if (maxCount.HasValue)
            events = events.Take(maxCount.Value);

        var result = events.ToList();

        _logger.LogDebug(
            "Retrieved {Count} events from grain {GrainId}",
            result.Count, this.GetPrimaryKey());

        return Task.FromResult<IReadOnlyList<AgentStateEvent>>(result);
    }

    public Task<long> GetLatestVersionAsync()
    {
        if (!_eventsStorage.State.Events.Any())
            return Task.FromResult(0L);
        
        var version = _eventsStorage.State.Events
            .Select(bytes => AgentStateEvent.Parser.ParseFrom(bytes))
            .Max(e => e.Version);

        return Task.FromResult(version);
    }

    public async Task SaveSnapshotAsync(AgentSnapshot snapshot)
    {
        _snapshotStorage.State.Snapshot = snapshot.ToByteArray();
        _snapshotStorage.State.Version = snapshot.Version;
        await _snapshotStorage.WriteStateAsync();

        _logger.LogInformation(
            "Saved snapshot at version {Version} for grain {GrainId}",
            snapshot.Version, this.GetPrimaryKey());
    }

    public Task<AgentSnapshot?> GetLatestSnapshotAsync()
    {
        if (_snapshotStorage.State.Snapshot == null)
            return Task.FromResult<AgentSnapshot?>(null);
        
        var snapshot = AgentSnapshot.Parser.ParseFrom(_snapshotStorage.State.Snapshot);
        return Task.FromResult<AgentSnapshot?>(snapshot);
    }
}

