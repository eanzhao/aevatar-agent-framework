using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Runtime.Orleans.EventSourcing;

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
        var grainId = this.GetPrimaryKey();
        
        // Get current version from repository
        var currentVersion = await _eventRepository.GetLatestVersionAsync(grainId);

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

        // Persist to repository
        await _eventRepository.AppendEventsAsync(grainId, events);

        _logger.LogDebug(
            "Appended {Count} events to grain {GrainId}, new version: {Version}",
            events.Count, grainId, newVersion);

        return newVersion;
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