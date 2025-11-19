using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core.Helpers;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Factory;

/// <summary>
/// GAgent Actor 工厂的抽象基类，包含通用逻辑
/// </summary>
public abstract class GAgentActorFactoryBase : IGAgentActorFactory
{
    protected readonly IServiceProvider _serviceProvider;
    protected readonly ILogger _logger;
    protected readonly IGAgentActorFactoryProvider? _factoryProvider;
    protected readonly IGAgentFactory? _agentFactory;

    protected GAgentActorFactoryBase(
        IServiceProvider serviceProvider,
        ILogger logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _factoryProvider = serviceProvider.GetService<IGAgentActorFactoryProvider>();
        _agentFactory = serviceProvider.GetService<IGAgentFactory>();
    }

    public async Task<IGAgentActor> CreateGAgentActorAsync<TAgent>(Guid id, CancellationToken ct = default)
        where TAgent : IGAgent
    {
        var agentType = typeof(TAgent);
        
        // 尝试获取自定义工厂（如果有 provider）
        if (_factoryProvider != null)
        {
            var customFactory = _factoryProvider.GetFactory(agentType);
            if (customFactory != null)
            {
                _logger.LogDebug("Using custom factory for type {AgentType} with id {Id}",
                    agentType.Name, id);
                return await customFactory(this, id, ct);
            }
        }
        
        // 默认流程：创建 Agent + 包装成 Actor
        _logger.LogDebug("Using default creation process for type {AgentType} with id {Id}",
            agentType.Name, id);
            
        if (_agentFactory == null)
        {
            throw new InvalidOperationException(
                $"No IGAgentFactory registered. Cannot create agent of type {agentType.Name}");
        }
        
        // 1. 创建 Agent 实例（由 IGAgentFactory 负责）
        var agent = _agentFactory.CreateGAgent(id, agentType, ct);
        
        // 2. 包装成 Actor（由子类实现具体逻辑）
        return await CreateActorForAgentAsync(agent, id, ct);
    }

    /// <summary>
    /// 为已存在的 Agent 实例创建 Actor 包装器
    /// 由子类实现具体的包装逻辑
    /// </summary>
    protected abstract Task<IGAgentActor> CreateActorForAgentAsync(IGAgent agent, Guid id, CancellationToken ct = default);
}