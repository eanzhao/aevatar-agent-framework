using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

namespace Aevatar.Agents.Orleans;

/// <summary>
/// Orleans 运行时的 Agent Actor 工厂
/// </summary>
public class OrleansGAgentActorFactory : IGAgentActorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<OrleansGAgentActorFactory> _logger;
    private readonly OrleansGAgentActorFactoryOptions _options;

    public OrleansGAgentActorFactory(
        IServiceProvider serviceProvider,
        IGrainFactory grainFactory,
        ILogger<OrleansGAgentActorFactory> logger,
        IOptions<OrleansGAgentActorFactoryOptions>? options = null)
    {
        _serviceProvider = serviceProvider;
        _grainFactory = grainFactory;
        _logger = logger;
        _options = options?.Value ?? new OrleansGAgentActorFactoryOptions();
    }

    public async Task<IGAgentActor> CreateGAgentActorAsync<TAgent>(Guid id, CancellationToken ct = default)
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

    public async Task<IGAgentActor> CreateGAgentActorAsync<TAgent, TState>(Guid id, CancellationToken ct = default)
        where TAgent : IStateGAgent<TState>
        where TState : class, new()
    {
        _logger.LogDebug("Creating agent actor for type {AgentType} with id {Id} using grain type {GrainType}",
            typeof(TAgent).Name, id, _options.DefaultGrainType);

        // 创建本地 Agent 实例
        var agent = ActivatorUtilities.CreateInstance<TAgent>(_serviceProvider, id);

        // 根据配置获取适当的 Grain
        IGAgentGrain grain;
        
        // 检查 Agent 是否支持事件溯源
        bool isEventSourcingAgent = agent is IEventSourcingAgent;
        
        if (isEventSourcingAgent || _options.UseEventSourcing)
        {
            if (_options.UseJournaledGrain)
            {
                // 使用 Journaled Grain
                grain = _grainFactory.GetGrain<IJournaledGAgentGrain>(id.ToString());
                _logger.LogDebug("Using Journaled Grain for agent {Id}", id);
            }
            else
            {
                // 使用标准事件溯源 Grain
                grain = _grainFactory.GetGrain<IEventSourcingGAgentGrain>(id.ToString());
                _logger.LogDebug("Using EventSourcing Grain for agent {Id}", id);
            }
        }
        else
        {
            // 使用标准 Grain - 明确指定 IStandardGAgentGrain 以避免歧义
            grain = _grainFactory.GetGrain<IStandardGAgentGrain>(id.ToString());
            _logger.LogDebug("Using Standard Grain for agent {Id}", id);
        }
        
        // 创建 Actor 包装器（持有本地 Agent 和远程 Grain）
        var actor = new OrleansGAgentActor(grain, agent);

        // 激活
        await actor.ActivateAsync(ct);

        _logger.LogInformation("Created and activated agent actor {Id} with grain type {GrainType}", 
            id, grain.GetType().Name);

        return actor;
    }
}

