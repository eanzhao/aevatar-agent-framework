using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Local;

/// <summary>
/// Local 运行时的 Agent Actor 工厂
/// </summary>
public class LocalGAgentActorFactory : IGAgentActorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LocalGAgentActorFactory> _logger;

    // 全局 Actor 注册表（所有 LocalGAgentActor 共享）
    private readonly Dictionary<Guid, LocalGAgentActor> _actorRegistry = new();

    public LocalGAgentActorFactory(IServiceProvider serviceProvider, ILogger<LocalGAgentActorFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<IGAgentActor> CreateAgentAsync<TAgent, TState>(Guid id, CancellationToken ct = default)
        where TAgent : IGAgent<TState>
        where TState : class, new()
    {
        _logger.LogDebug("Creating agent actor for type {AgentType} with id {Id}",
            typeof(TAgent).Name, id);

        // 检查是否已存在
        if (_actorRegistry.ContainsKey(id))
        {
            throw new InvalidOperationException($"Agent with id {id} already exists");
        }

        // 创建 Agent 实例
        var agent = ActivatorUtilities.CreateInstance<TAgent>(_serviceProvider, id);

        // 创建 Actor
        var actor = new LocalGAgentActor(
            agent,
            this,
            _actorRegistry,
            _serviceProvider.GetService<ILogger<LocalGAgentActor>>()
        );

        // 激活
        await actor.ActivateAsync(ct);

        _logger.LogInformation("Created and activated agent actor {Id}", id);

        return actor;
    }
}