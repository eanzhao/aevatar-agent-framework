using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Google.Protobuf;

namespace Aevatar.Agents.Orleans.EventSourcing;

/// <summary>
/// Orleans-based EventStore implementation
/// - Events: Stored via IEventRepository (decoupled from Orleans)
/// - Snapshots: Stored via Orleans GrainStorage
/// - Concurrency: Coordinated by Orleans Grain
/// </summary>
public class OrleansEventStore : IEventStore
{
    private readonly IGrainFactory _grainFactory;

    public OrleansEventStore(IGrainFactory grainFactory)
    {
        _grainFactory = grainFactory;
    }

    // ========== Event Operations ==========

    public async Task<long> AppendEventsAsync(
        Guid agentId,
        IEnumerable<AgentStateEvent> events,
        long expectedVersion,
        CancellationToken ct = default)
    {
        // Use Grain for concurrency control
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
        // Query through Grain to ensure we use the same repository instance as writes
        // This is important in test scenarios where Silo and Client may have different ServiceProviders
        var storageGrain = _grainFactory.GetGrain<IEventStorageGrain>(agentId);
        return await storageGrain.GetEventsAsync(fromVersion, toVersion, maxCount);
    }

    public async Task<long> GetLatestVersionAsync(Guid agentId, CancellationToken ct = default)
    {
        // Query through Grain to ensure we use the same repository instance as writes
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
/// Grain interface for event storage operations
/// Responsible ONLY for:
/// - Concurrency control (optimistic locking via version tracking)
/// - Snapshot management (via Orleans GrainStorage)
/// 
/// Event persistence is handled by IEventRepository (decoupled)
/// </summary>
public interface IEventStorageGrain : IGrainWithGuidKey
{
    Task<long> AppendEventsAsync(List<AgentStateEvent> events, long expectedVersion);
    Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(long? fromVersion = null, long? toVersion = null, int? maxCount = null);
    Task<long> GetLatestVersionAsync();
    Task SaveSnapshotAsync(AgentSnapshot snapshot);
    Task<AgentSnapshot?> GetLatestSnapshotAsync();
}

/// <summary>
/// State for snapshot storage (uses Orleans GrainStorage)
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
/// Grain implementation for event storage coordination
/// Responsibilities:
/// - Concurrency control: Ensures optimistic locking via version checking
/// - Snapshot management: Stores snapshots via Orleans GrainStorage
/// 
/// Does NOT store events directly - delegates to IEventRepository
/// </summary>
public class EventStorageGrain : Grain, IEventStorageGrain
{
    private readonly IEventRepository _eventRepository;
    private readonly IPersistentState<SnapshotState> _snapshotStorage;
    private readonly ILogger<EventStorageGrain> _logger;

    public EventStorageGrain(
        IEventRepository eventRepository,
        [PersistentState("snapshots", "EventStoreStorage")] IPersistentState<SnapshotState> snapshotStorage,
        ILogger<EventStorageGrain> logger)
    {
        _eventRepository = eventRepository;
        _snapshotStorage = snapshotStorage;
        _logger = logger;
    }

    public async Task<long> AppendEventsAsync(List<AgentStateEvent> events, long expectedVersion)
    {
        try
        {
            if (events == null)
            {
                _logger.LogError("Events list is null!");
                throw new ArgumentNullException(nameof(events));
            }
            
            if (events.Count == 0)
            {
                _logger.LogWarning("Attempted to append empty event list, returning expected version: {ExpectedVersion}", expectedVersion);
                return expectedVersion;
            }

            var grainId = this.GetPrimaryKey();
            
            _logger.LogDebug(
                "AppendEventsAsync called for grain {GrainId}, events count: {Count}, expected version: {ExpectedVersion}",
                grainId, events.Count, expectedVersion);
            
            // Get current version from repository
            var currentVersion = await _eventRepository.GetLatestVersionAsync(grainId);
            
            _logger.LogDebug(
                "Current version for grain {GrainId}: {CurrentVersion}",
                grainId, currentVersion);

            // Optimistic concurrency check
            if (currentVersion != expectedVersion)
            {
                _logger.LogWarning(
                    "Concurrency conflict for grain {GrainId}: expected {Expected}, got {Current}",
                    grainId, expectedVersion, currentVersion);

                throw new InvalidOperationException(
                    $"Concurrency conflict: expected version {expectedVersion}, got {currentVersion}");
            }

            // Assign versions to events
            var newVersion = currentVersion;
            foreach (var evt in events)
            {
                evt.Version = ++newVersion;
            }

            _logger.LogDebug(
                "Assigned versions to events, new version will be: {NewVersion}",
                newVersion);

            // Persist to repository
            await _eventRepository.AppendEventsAsync(grainId, events);

            _logger.LogDebug(
                "Appended {Count} events to grain {GrainId}, new version: {Version}",
                events.Count, grainId, newVersion);

            return newVersion;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AppendEventsAsync for grain {GrainId}", this.GetPrimaryKey());
            throw;
        }
    }

    public async Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(long? fromVersion = null, long? toVersion = null, int? maxCount = null)
    {
        var grainId = this.GetPrimaryKey();
        return await _eventRepository.GetEventsAsync(grainId, fromVersion, toVersion, maxCount);
    }

    public async Task<long> GetLatestVersionAsync()
    {
        var grainId = this.GetPrimaryKey();
        return await _eventRepository.GetLatestVersionAsync(grainId);
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

