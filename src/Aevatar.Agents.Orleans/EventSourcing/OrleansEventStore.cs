using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

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
/// State stored in Orleans GrainStorage
/// </summary>
[GenerateSerializer]
public class EventStorageState
{
    [Id(0)]
    public List<AgentStateEvent> Events { get; set; } = new();

    [Id(1)]
    public AgentSnapshot? LatestSnapshot { get; set; }
}

/// <summary>
/// Grain implementation for event storage
/// Uses Orleans GrainStorage for persistence
/// </summary>
public class EventStorageGrain : Grain, IEventStorageGrain
{
    private readonly IPersistentState<EventStorageState> _storage;
    private readonly ILogger<EventStorageGrain> _logger;

    public EventStorageGrain(
        [PersistentState("eventstore", "EventStoreStorage")] IPersistentState<EventStorageState> storage,
        ILogger<EventStorageGrain> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task<long> AppendEventsAsync(List<AgentStateEvent> events, long expectedVersion)
    {
        // Optimistic concurrency check
        var currentVersion = _storage.State.Events.Any() 
            ? _storage.State.Events.Max(e => e.Version) 
            : 0;

        if (currentVersion != expectedVersion)
        {
            _logger.LogWarning(
                "Concurrency conflict for grain {GrainId}: expected {Expected}, got {Current}",
                this.GetPrimaryKey(), expectedVersion, currentVersion);

            throw new InvalidOperationException(
                $"Concurrency conflict: expected version {expectedVersion}, got {currentVersion}");
        }

        // Append events with incremented versions
        var newVersion = currentVersion;
        foreach (var evt in events)
        {
            evt.Version = ++newVersion;
            _storage.State.Events.Add(evt);
        }

        // Persist to storage
        await _storage.WriteStateAsync();

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
        var query = _storage.State.Events.AsEnumerable();

        // Range query
        if (fromVersion.HasValue)
            query = query.Where(e => e.Version >= fromVersion.Value);

        if (toVersion.HasValue)
            query = query.Where(e => e.Version <= toVersion.Value);

        // Order by version
        query = query.OrderBy(e => e.Version);

        // Pagination
        if (maxCount.HasValue)
            query = query.Take(maxCount.Value);

        var result = query.ToList();

        _logger.LogDebug(
            "Retrieved {Count} events from grain {GrainId}",
            result.Count, this.GetPrimaryKey());

        return Task.FromResult<IReadOnlyList<AgentStateEvent>>(result);
    }

    public Task<long> GetLatestVersionAsync()
    {
        var version = _storage.State.Events.Any()
            ? _storage.State.Events.Max(e => e.Version)
            : 0;

        return Task.FromResult(version);
    }

    public async Task SaveSnapshotAsync(AgentSnapshot snapshot)
    {
        _storage.State.LatestSnapshot = snapshot;
        await _storage.WriteStateAsync();

        _logger.LogInformation(
            "Saved snapshot at version {Version} for grain {GrainId}",
            snapshot.Version, this.GetPrimaryKey());
    }

    public Task<AgentSnapshot?> GetLatestSnapshotAsync()
    {
        return Task.FromResult(_storage.State.LatestSnapshot);
    }
}

