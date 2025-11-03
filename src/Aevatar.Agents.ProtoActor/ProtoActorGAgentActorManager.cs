using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.ProtoActor;

/// <summary>
/// ProtoActor 运行时的 Agent Actor 管理器
/// </summary>
public class ProtoActorGAgentActorManager : IGAgentActorManager
{
    private readonly IGAgentActorFactory _factory;
    private readonly IRootContext _rootContext;
    private readonly ILogger<ProtoActorGAgentActorManager> _logger;
    private readonly Dictionary<Guid, IGAgentActor> _actors = new();
    private readonly object _lock = new();

    public ProtoActorGAgentActorManager(
        IGAgentActorFactory factory,
        IRootContext rootContext,
        ILogger<ProtoActorGAgentActorManager> logger)
    {
        _factory = factory;
        _rootContext = rootContext;
        _logger = logger;
    }

    public async Task<IGAgentActor> CreateAndRegisterAsync<TAgent, TState>(
        Guid id,
        CancellationToken ct = default)
        where TAgent : IGAgent<TState>
        where TState : class, new()
    {
        _logger.LogDebug("Creating and registering agent {AgentType} with id {Id}",
            typeof(TAgent).Name, id);

        var actor = await _factory.CreateAgentAsync<TAgent, TState>(id, ct);

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
}