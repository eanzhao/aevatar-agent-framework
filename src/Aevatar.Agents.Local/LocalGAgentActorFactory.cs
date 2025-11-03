using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Helpers;
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
    private readonly LocalMessageStreamRegistry _streamRegistry;

    public LocalGAgentActorFactory(
        IServiceProvider serviceProvider,
        ILogger<LocalGAgentActorFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _streamRegistry = new LocalMessageStreamRegistry();
    }

    public async Task<IGAgentActor> CreateAgentAsync<TAgent>(Guid id, CancellationToken ct = default)
        where TAgent : IGAgent
    {
        // 提取状态类型
        var agentType = typeof(TAgent);
        var stateType = AgentTypeHelper.ExtractStateType(agentType);

        _logger.LogDebug("Creating agent actor for type {AgentType} with state {StateType} and id {Id}",
            agentType.Name, stateType.Name, id);

        // 调用双参数版本
        return await AgentTypeHelper.InvokeCreateAgentAsync(this, agentType, stateType, id, ct);
    }

    public async Task<IGAgentActor> CreateAgentAsync<TAgent, TState>(Guid id, CancellationToken ct = default)
        where TAgent : IGAgent<TState>
        where TState : class, new()
    {
        _logger.LogDebug("Creating agent actor for type {AgentType} with id {Id}",
            typeof(TAgent).Name, id);

        // 检查是否已存在
        if (_streamRegistry.StreamExists(id))
        {
            throw new InvalidOperationException($"Agent with id {id} already exists");
        }

        // 创建 Agent 实例
        var agent = ActivatorUtilities.CreateInstance<TAgent>(_serviceProvider, id);

        // 创建 Actor（使用 Stream）
        var actor = new LocalGAgentActor(
            agent,
            _streamRegistry,
            _serviceProvider.GetService<ILogger<LocalGAgentActor>>()
        );

        // 激活（会订阅 Stream）
        await actor.ActivateAsync(ct);

        _logger.LogInformation("Created and activated agent actor {Id}", id);

        return actor;
    }
}