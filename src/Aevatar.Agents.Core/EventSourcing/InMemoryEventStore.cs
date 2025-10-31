using Aevatar.Agents.Abstractions.EventSourcing;

namespace Aevatar.Agents.Core.EventSourcing;

/// <summary>
/// 内存事件存储实现（用于测试和 Local 运行时）
/// </summary>
public class InMemoryEventStore : IEventStore
{
    private readonly Dictionary<Guid, List<StateLogEvent>> _events = new();
    private readonly object _lock = new();
    
    public Task SaveEventAsync(Guid agentId, StateLogEvent logEvent, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_events.ContainsKey(agentId))
            {
                _events[agentId] = new List<StateLogEvent>();
            }
            
            _events[agentId].Add(logEvent);
        }
        
        return Task.CompletedTask;
    }
    
    public Task SaveEventsAsync(Guid agentId, IEnumerable<StateLogEvent> logEvents, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_events.ContainsKey(agentId))
            {
                _events[agentId] = new List<StateLogEvent>();
            }
            
            _events[agentId].AddRange(logEvents);
        }
        
        return Task.CompletedTask;
    }
    
    public Task<IReadOnlyList<StateLogEvent>> GetEventsAsync(Guid agentId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_events.TryGetValue(agentId, out var events))
            {
                return Task.FromResult<IReadOnlyList<StateLogEvent>>(events.ToList());
            }
            
            return Task.FromResult<IReadOnlyList<StateLogEvent>>(Array.Empty<StateLogEvent>());
        }
    }
    
    public Task<IReadOnlyList<StateLogEvent>> GetEventsAsync(
        Guid agentId,
        long fromVersion,
        long toVersion,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_events.TryGetValue(agentId, out var events))
            {
                var filtered = events
                    .Where(e => e.Version >= fromVersion && e.Version <= toVersion)
                    .ToList();
                
                return Task.FromResult<IReadOnlyList<StateLogEvent>>(filtered);
            }
            
            return Task.FromResult<IReadOnlyList<StateLogEvent>>(Array.Empty<StateLogEvent>());
        }
    }
    
    public Task<long> GetLatestVersionAsync(Guid agentId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_events.TryGetValue(agentId, out var events) && events.Count > 0)
            {
                return Task.FromResult(events.Max(e => e.Version));
            }
            
            return Task.FromResult(0L);
        }
    }
    
    public Task ClearEventsAsync(Guid agentId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _events.Remove(agentId);
        }
        
        return Task.CompletedTask;
    }
}

