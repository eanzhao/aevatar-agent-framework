using Aevatar.Agents.Abstractions.EventSourcing;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Runtime.Orleans.EventSourcing;

/// <summary>
/// Orleans-based EventStore implementation
/// - Events: Stored via IEventRepository (decoupled from Orleans)
/// - Snapshots: Stored via Orleans GrainStorage
/// - Concurrency: Coordinated by Orleans Grain
/// </summary>
public class OrleansEventStore : IEventStore
{
    private readonly IGrainFactory _grainFactory;
    private readonly IEventRepository _eventRepository;
    private readonly ILogger<OrleansEventStore> _logger;

    public OrleansEventStore(
        IGrainFactory grainFactory,
        IEventRepository eventRepository,
        ILogger<OrleansEventStore> logger)
    {
        _grainFactory = grainFactory;
        _eventRepository = eventRepository;
        _logger = logger;
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
        // Directly query from repository (no grain needed for reads)
        return await _eventRepository.GetEventsAsync(agentId, fromVersion, toVersion, maxCount, ct);
    }

    public async Task<long> GetLatestVersionAsync(Guid agentId, CancellationToken ct = default)
    {
        // Directly query from repository
        return await _eventRepository.GetLatestVersionAsync(agentId, ct);
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