namespace Aevatar.Agents.Runtime.Orleans.EventSourcing;

/// <summary>
/// Repository interface for event storage
/// Abstracts the underlying storage implementation (MongoDB, SQL, etc.)
/// </summary>
public interface IEventRepository
{
    /// <summary>
    /// Append events for an agent
    /// </summary>
    Task<long> AppendEventsAsync(
        Guid agentId, 
        IEnumerable<AgentStateEvent> events, 
        CancellationToken ct = default);

    /// <summary>
    /// Get events for an agent with optional version range
    /// </summary>
    Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        Guid agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get the latest version for an agent
    /// </summary>
    Task<long> GetLatestVersionAsync(
        Guid agentId, 
        CancellationToken ct = default);

    /// <summary>
    /// Delete events older than a specific version (for cleanup after snapshot)
    /// </summary>
    Task DeleteEventsBeforeVersionAsync(
        Guid agentId, 
        long version, 
        CancellationToken ct = default);
}

