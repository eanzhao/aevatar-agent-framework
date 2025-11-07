using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;

namespace Aevatar.Agents.Core.EventSourcing;

/// <summary>
/// In-memory event store implementation (for testing and Local runtime)
/// Thread-safe with optimistic concurrency control
/// </summary>
public class InMemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<Guid, List<AgentStateEvent>> _events = new();
    private readonly ConcurrentDictionary<Guid, AgentSnapshot> _snapshots = new();
    private readonly object _lock = new();

    // ========== Event Operations ==========

    public Task<long> AppendEventsAsync(
        Guid agentId,
        IEnumerable<AgentStateEvent> events,
        long expectedVersion,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            var eventList = _events.GetOrAdd(agentId, _ => new List<AgentStateEvent>());

            // Optimistic concurrency check
            var currentVersion = eventList.Any() ? eventList.Max(e => e.Version) : 0;
            if (currentVersion != expectedVersion)
            {
                throw new InvalidOperationException(
                    $"Concurrency conflict: expected version {expectedVersion}, got {currentVersion}");
            }

            // Append events with incremented versions
            var newVersion = currentVersion;
            foreach (var evt in events)
            {
                evt.Version = ++newVersion;
                eventList.Add(evt);
            }

            return Task.FromResult(newVersion);
        }
    }

    public Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        Guid agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_events.TryGetValue(agentId, out var eventList))
            {
                return Task.FromResult<IReadOnlyList<AgentStateEvent>>(Array.Empty<AgentStateEvent>());
            }

            var query = eventList.AsEnumerable();

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

            return Task.FromResult<IReadOnlyList<AgentStateEvent>>(query.ToList());
        }
    }

    public Task<long> GetLatestVersionAsync(Guid agentId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_events.TryGetValue(agentId, out var eventList) || !eventList.Any())
            {
                return Task.FromResult(0L);
            }

            return Task.FromResult(eventList.Max(e => e.Version));
        }
    }

    // ========== Snapshot Operations ==========

    public Task SaveSnapshotAsync(Guid agentId, AgentSnapshot snapshot, CancellationToken ct = default)
    {
        _snapshots[agentId] = snapshot;
        return Task.CompletedTask;
    }

    public Task<AgentSnapshot?> GetLatestSnapshotAsync(Guid agentId, CancellationToken ct = default)
    {
        _snapshots.TryGetValue(agentId, out var snapshot);
        return Task.FromResult(snapshot);
    }
}
