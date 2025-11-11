namespace Aevatar.Agents.Abstractions.EventSourcing;

/// <summary>
/// EventSourcing storage interface (unified across all runtimes)
/// Uses Protobuf AgentStateEvent for serialization consistency
/// 
/// Design reference: Aevatar.EventSourcing.Core.ILogConsistentStorage
/// Enhanced features: Snapshot, range query, optimistic concurrency
/// </summary>
public interface IEventStore
{
    // ========== Event Operations ==========

    /// <summary>
    /// Append events with optimistic concurrency control
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="events">Events to append</param>
    /// <param name="expectedVersion">Expected current version (optimistic concurrency)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>New version number</returns>
    Task<long> AppendEventsAsync(
        Guid agentId,
        IEnumerable<AgentStateEvent> events,
        long expectedVersion,
        CancellationToken ct = default);

    /// <summary>
    /// Get events with range query and pagination support
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="fromVersion">Start version (inclusive)</param>
    /// <param name="toVersion">End version (inclusive)</param>
    /// <param name="maxCount">Maximum count (pagination)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Event list</returns>
    Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        Guid agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get latest version number
    /// </summary>
    Task<long> GetLatestVersionAsync(Guid agentId, CancellationToken ct = default);
    
    // ========== Snapshot Operations (Optional Implementation) ==========

    /// <summary>
    /// Save snapshot for performance optimization
    /// </summary>
    Task SaveSnapshotAsync(Guid agentId, AgentSnapshot snapshot, CancellationToken ct = default);

/// <summary>
    /// Get latest snapshot
/// </summary>
    Task<AgentSnapshot?> GetLatestSnapshotAsync(Guid agentId, CancellationToken ct = default);
}
