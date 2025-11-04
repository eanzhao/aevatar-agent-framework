using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Local;

/// <summary>
/// Local 运行时的 Agent Actor 管理器
/// </summary>
public class LocalGAgentActorManager : IGAgentActorManager
{
    private readonly IGAgentActorFactory _factory;
    private readonly ILogger<LocalGAgentActorManager> _logger;
    private readonly Dictionary<Guid, IGAgentActor> _actors = new();
    private readonly Dictionary<Guid, DateTimeOffset> _lastActivityTime = new();
    private readonly Lock _lock = new();

    public LocalGAgentActorManager(
        IGAgentActorFactory factory,
        ILogger<LocalGAgentActorManager> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    #region 生命周期管理

    public async Task<IGAgentActor> CreateAndRegisterAsync<TAgent, TState>(
        Guid id,
        CancellationToken ct = default)
        where TAgent : IStateGAgent<TState>
        where TState : class, new()
    {
        _logger.LogDebug("Creating and registering agent {AgentType} with id {Id}",
            typeof(TAgent).Name, id);

        // 创建 Actor
        var actor = await _factory.CreateGAgentActorAsync<TAgent, TState>(id, ct);

        // 注册
        lock (_lock)
        {
            _actors[id] = actor;
            _lastActivityTime[id] = DateTimeOffset.UtcNow;
        }

        _logger.LogInformation("Agent actor {Id} created and registered", id);
        return actor;
    }

    public async Task<IReadOnlyList<IGAgentActor>> CreateBatchAsync<TAgent, TState>(
        IEnumerable<Guid> ids,
        CancellationToken ct = default)
        where TAgent : IStateGAgent<TState>
        where TState : class, new()
    {
        var idList = ids.ToList();
        _logger.LogDebug("Batch creating {Count} agents of type {AgentType}",
            idList.Count, typeof(TAgent).Name);

        var tasks = idList.Select(id => CreateAndRegisterAsync<TAgent, TState>(id, ct));
        var actors = await Task.WhenAll(tasks);

        return actors;
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
            _lastActivityTime.Remove(id);
        }

        _logger.LogInformation("Deactivating and unregistering actor {Id}", id);
        await actor.DeactivateAsync(ct);
    }

    public async Task DeactivateBatchAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        _logger.LogDebug("Batch deactivating {Count} actors", idList.Count);

        var tasks = idList.Select(id => DeactivateAndUnregisterAsync(id, ct));
        await Task.WhenAll(tasks);
    }

    public async Task DeactivateAllAsync(CancellationToken ct = default)
    {
        List<IGAgentActor> actorsToDeactivate;

        lock (_lock)
        {
            actorsToDeactivate = _actors.Values.ToList();
            _actors.Clear();
            _lastActivityTime.Clear();
        }

        _logger.LogInformation("Deactivating all {Count} actors", actorsToDeactivate.Count);

        // 并发停用所有 Actor
        await Task.WhenAll(actorsToDeactivate.Select(a => a.DeactivateAsync(ct)));
    }

    #endregion

    #region 查询和获取

    public Task<IGAgentActor?> GetActorAsync(Guid id)
    {
        lock (_lock)
        {
            if (_actors.TryGetValue(id, out var actor))
            {
                _lastActivityTime[id] = DateTimeOffset.UtcNow;
            }

            return Task.FromResult(actor);
        }
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
                    _lastActivityTime[id] = DateTimeOffset.UtcNow;
                }
            }
        }

        return Task.FromResult<IReadOnlyList<IGAgentActor>>(actors);
    }

    public Task<IReadOnlyList<IGAgentActor>> GetAllActorsAsync()
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<IGAgentActor>>(_actors.Values.ToList());
        }
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

    public Task<int> GetCountByTypeAsync<TAgent>()
        where TAgent : IGAgent
    {
        lock (_lock)
        {
            var count = _actors.Values.Count(a => a.GetAgent() is TAgent);
            return Task.FromResult(count);
        }
    }

    #endregion

    #region 监控和诊断

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

            _lastActivityTime.TryGetValue(id, out var lastActivity);

            return Task.FromResult(new ActorHealthStatus
            {
                Id = id,
                IsHealthy = true,
                LastActivityTime = lastActivity
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
                ActiveActors = _actors.Count, // 在 Local 运行时，所有 Actor 都是活跃的
                ActorsByType = actorsByType
            });
        }
    }

    #endregion
}
