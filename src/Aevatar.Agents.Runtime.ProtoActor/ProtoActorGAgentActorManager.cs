using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// ProtoActor 运行时的 Agent Actor 管理器
/// </summary>
public class ProtoActorGAgentActorManager : IGAgentActorManager
{
    private readonly IGAgentActorFactory _factory;
    private readonly IRootContext _rootContext;
    private readonly ILogger<ProtoActorGAgentActorManager> _logger;
    private readonly Dictionary<Guid, IGAgentActor> _actors = new();
    private readonly Lock _lock = new();

    public ProtoActorGAgentActorManager(
        IGAgentActorFactory factory,
        IRootContext rootContext,
        ILogger<ProtoActorGAgentActorManager> logger)
    {
        _factory = factory;
        _rootContext = rootContext;
        _logger = logger;
    }

    public async Task<IGAgentActor> CreateAndRegisterAsync<TAgent>(
        Guid id,
        CancellationToken ct = default)
        where TAgent : IGAgent
    {
        _logger.LogDebug("Creating and registering agent {AgentType} with id {Id}",
            typeof(TAgent).Name, id);

        var actor = await _factory.CreateGAgentActorAsync<TAgent>(id, ct);

        lock (_lock)
        {
            _actors[id] = actor;
        }

        _logger.LogInformation("Agent actor {Id} created and registered", id);

        return actor;
    }

    public Task<IGAgentActor?> GetActorAsync(Guid id)
    {
        lock (_lock)
        {
            _actors.TryGetValue(id, out var actor);
            return Task.FromResult(actor);
        }
    }

    public Task<IReadOnlyList<IGAgentActor>> GetAllActorsAsync()
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<IGAgentActor>>(_actors.Values.ToList());
        }
    }

    public async Task DeactivateAndUnregisterAsync(Guid id, CancellationToken ct = default)
    {
        IGAgentActor? actor;

        lock (_lock)
        {
            if (!_actors.TryGetValue(id, out actor))
            {
                _logger.LogWarning("Actor {Id} not found for deactivation", id);
                return;
            }

            _actors.Remove(id);
        }

        _logger.LogInformation("Deactivating and unregistering actor {Id}", id);
        await actor.DeactivateAsync(ct);
    }

    public async Task DeactivateAllAsync(CancellationToken ct = default)
    {
        List<IGAgentActor> actorsToDeactivate;

        lock (_lock)
        {
            actorsToDeactivate = _actors.Values.ToList();
            _actors.Clear();
        }

        _logger.LogInformation("Deactivating all {Count} actors", actorsToDeactivate.Count);

        await Task.WhenAll(actorsToDeactivate.Select(a => a.DeactivateAsync(ct)));
    }

    public Task<bool> ExistsAsync(Guid id)
    {
        lock (_lock)
        {
            return Task.FromResult(_actors.ContainsKey(id));
        }
    }

    public Task<int> GetCountAsync()
    {
        lock (_lock)
        {
            return Task.FromResult(_actors.Count);
        }
    }

    #region 新增接口实现

    public async Task<IReadOnlyList<IGAgentActor>> CreateBatchAsync<TAgent>(
        IEnumerable<Guid> ids,
        CancellationToken ct = default)
        where TAgent : IGAgent
    {
        var tasks = ids.Select(id => CreateAndRegisterAsync<TAgent>(id, ct));
        var actors = await Task.WhenAll(tasks);
        return actors;
    }

    public async Task DeactivateBatchAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var tasks = ids.Select(id => DeactivateAndUnregisterAsync(id, ct));
        await Task.WhenAll(tasks);
    }

    public Task<IReadOnlyList<IGAgentActor>> GetActorsAsync(IEnumerable<Guid> ids)
    {
        var actors = new List<IGAgentActor>();
        
        lock (_lock)
        {
            foreach (var id in ids)
            {
                if (_actors.TryGetValue(id, out var actor))
                {
                    actors.Add(actor);
                }
            }
        }
        
        return Task.FromResult<IReadOnlyList<IGAgentActor>>(actors);
    }

    public Task<IReadOnlyList<IGAgentActor>> GetActorsByTypeAsync<TAgent>()
        where TAgent : IGAgent
    {
        lock (_lock)
        {
            var actors = _actors.Values
                .Where(a => a.GetAgent() is TAgent)
                .ToList();
            
            return Task.FromResult<IReadOnlyList<IGAgentActor>>(actors);
        }
    }

    public Task<IReadOnlyList<IGAgentActor>> GetActorsByTypeNameAsync(string typeName)
    {
        lock (_lock)
        {
            var actors = _actors.Values
                .Where(a => a.GetAgent().GetType().Name == typeName)
                .ToList();
            
            return Task.FromResult<IReadOnlyList<IGAgentActor>>(actors);
        }
    }

    public Task<int> GetCountByTypeAsync<TAgent>()
        where TAgent : IGAgent
    {
        lock (_lock)
        {
            var count = _actors.Values.Count(a => a.GetAgent() is TAgent);
            return Task.FromResult(count);
        }
    }

    public Task<ActorHealthStatus> GetHealthStatusAsync(Guid id)
    {
        lock (_lock)
        {
            if (!_actors.TryGetValue(id, out var actor))
            {
                return Task.FromResult(new ActorHealthStatus
                {
                    Id = id,
                    IsHealthy = false,
                    ErrorMessage = "Actor not found"
                });
            }

            return Task.FromResult(new ActorHealthStatus
            {
                Id = id,
                IsHealthy = true,
                LastActivityTime = DateTimeOffset.UtcNow
            });
        }
    }

    public Task<ActorManagerStatistics> GetStatisticsAsync()
    {
        lock (_lock)
        {
            var actorsByType = _actors.Values
                .GroupBy(a => a.GetAgent().GetType().Name)
                .ToDictionary(g => g.Key, g => g.Count());

            return Task.FromResult(new ActorManagerStatistics
            {
                TotalActors = _actors.Count,
                ActiveActors = _actors.Count,
                ActorsByType = actorsByType
            });
        }
    }

    #endregion
}