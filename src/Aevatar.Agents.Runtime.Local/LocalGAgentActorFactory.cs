using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Local 运行时的 Agent Actor 工厂
/// </summary>
public class LocalGAgentActorFactory : IGAgentActorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LocalGAgentActorFactory> _logger;
    private readonly LocalMessageStreamRegistry _streamRegistry;
    private readonly IGAgentActorFactoryProvider? _factoryProvider;

    public LocalGAgentActorFactory(
        IServiceProvider serviceProvider,
        ILogger<LocalGAgentActorFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _streamRegistry = new LocalMessageStreamRegistry();
        _factoryProvider = serviceProvider.GetService<IGAgentActorFactoryProvider>();
    }

    public async Task<IGAgentActor> CreateGAgentActorAsync<TAgent>(Guid id, CancellationToken ct = default)
        where TAgent : IGAgent
    {
        if (_factoryProvider == null)
        {
            throw new InvalidOperationException(
                $"No IGAgentActorFactoryProvider registered. Please register an agent factory for type {typeof(TAgent).Name}");
        }

        var agentType = typeof(TAgent);
        var factory = _factoryProvider.GetFactory(agentType);

        if (factory == null)
        {
            throw new InvalidOperationException(
                $"No factory found for agent type {agentType.Name}. Please ensure the agent is properly registered.");
        }

        _logger.LogDebug("Creating agent actor using factory for type {AgentType} with id {Id}",
            agentType.Name, id);

        return await factory(this, id, ct);
    }

    /// <summary>
    /// 为已存在的 Agent 实例创建 Actor（内部方法，供自动发现使用）
    /// </summary>
    public async Task<IGAgentActor> CreateActorForAgentAsync(IGAgent agent, Guid id, CancellationToken ct = default)
    {
        // 检查是否已存在
        if (_streamRegistry.StreamExists(id))
        {
            throw new InvalidOperationException($"Agent with id {id} already exists");
        }

        _logger.LogDebug("[Factory] Creating Actor for Agent - Type: {AgentType}, Id: {Id}",
            agent.GetType().Name, id);

        // 自动注入 Logger
        AgentLoggerInjector.InjectLogger(agent, _serviceProvider);

        // 自动注入 StateStore
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        // 自动注入 ConfigurationStore
        AgentConfigurationInjector.InjectConfigurationStore(agent, _serviceProvider);

        // 创建 Actor（使用 Stream）
        var actor = new LocalGAgentActor(
            agent,
            _streamRegistry,
            _serviceProvider.GetService<ILogger<LocalGAgentActor>>()
        );

        // 自动注入 Actor 的 Logger
        AgentLoggerInjector.InjectLogger(actor, _serviceProvider);

        // 激活（会订阅 Stream）
        await actor.ActivateAsync(ct);

        _logger.LogInformation("Created and activated agent actor {Id}", id);

        return actor;
    }
}