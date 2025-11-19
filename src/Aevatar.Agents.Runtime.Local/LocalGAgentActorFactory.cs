using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core.Helpers;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Local 运行时的 Agent Actor 工厂
/// </summary>
public class LocalGAgentActorFactory : GAgentActorFactoryBase
{
    private readonly LocalMessageStreamRegistry _streamRegistry;

    public LocalGAgentActorFactory(
        IServiceProvider serviceProvider,
        ILogger<LocalGAgentActorFactory> logger)
        : base(serviceProvider, logger)
    {
        _streamRegistry = new LocalMessageStreamRegistry();
    }

    public override async Task<IGAgentActor> CreateActorForAgentAsync(IGAgent agent, Guid id,
        CancellationToken ct = default)
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
        AgentConfigStoreInjector.InjectConfigStore(agent, _serviceProvider);

        if (AIAgentLLMProviderFactoryInjector.HasLLMProviderFactory(agent))
        {
            AIAgentLLMProviderFactoryInjector.InjectLLMProviderFactory(agent, _serviceProvider);
        }

        // 创建 Actor（使用 Stream）
        var actor = new LocalGAgentActor(
            agent,
            _streamRegistry);

        // 自动注入 Actor 的 Logger
        AgentLoggerInjector.InjectLogger(actor, _serviceProvider);

        // 激活（会订阅 Stream）
        await actor.ActivateAsync(ct);

        _logger.LogInformation("Created and activated agent actor {Id}", id);

        return actor;
    }
}