using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core.Helpers;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// Proto.Actor 运行时的 Agent Actor 工厂
/// </summary>
public class ProtoActorGAgentActorFactory : GAgentActorFactoryBase
{
    private readonly ActorSystem _actorSystem;
    private readonly ProtoActorMessageStreamRegistry _streamRegistry;

    public ProtoActorGAgentActorFactory(
        IServiceProvider serviceProvider,
        ActorSystem actorSystem,
        ILogger<ProtoActorGAgentActorFactory> logger)
        : base(serviceProvider, logger)
    {
        _actorSystem = actorSystem;
        _streamRegistry = new ProtoActorMessageStreamRegistry(actorSystem.Root);
    }

    /// <summary>
    /// 为已存在的 Agent 实例创建 Actor（内部方法，供自动发现使用）
    /// </summary>
    protected override async Task<IGAgentActor> CreateActorForAgentAsync(IGAgent agent, Guid id,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[Factory] Creating ProtoActor Actor for Agent - Type: {AgentType}, Id: {Id}",
            agent.GetType().Name, id);

        // Agent 应该已经通过 IGAgentFactory 完成了所有依赖注入
        // 如果 Agent 不是通过 IGAgentFactory 创建的（旧代码路径），才需要手动注入
        // 检查是否已有依赖（通过检查其中一个属性）
        var loggerProperty = agent.GetType().GetProperty("Logger", 
            System.Reflection.BindingFlags.Instance | 
            System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Public);
        
        if (loggerProperty != null)
        {
            var currentLogger = loggerProperty.GetValue(agent);
            if (currentLogger == null)
            {
                // 仅在依赖未注入时才手动注入（向后兼容旧代码）
                _logger.LogDebug("Agent dependencies not injected, injecting manually");
                AgentLoggerInjector.InjectLogger(agent, _serviceProvider);
                AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
                AgentConfigStoreInjector.InjectConfigStore(agent, _serviceProvider);
                
                if (AIAgentLLMProviderFactoryInjector.HasLLMProviderFactory(agent))
                {
                    AIAgentLLMProviderFactoryInjector.InjectLLMProviderFactory(agent, _serviceProvider);
                }
                
                // EventStore 注入（向后兼容）
                if (AgentEventStoreInjector.HasEventStore(agent))
                {
                    AgentEventStoreInjector.InjectEventStore(agent, _serviceProvider);
                }
            }
        }

        // 创建 ProtoActor Actor
        var props = Props.FromProducer(() => new AgentActor());
        var actorPid = _actorSystem.Root.Spawn(props);

        var actor = new ProtoActorGAgentActor(
            agent,
            _actorSystem.Root,
            actorPid,
            _streamRegistry);

        // 激活
        await actor.ActivateAsync(ct);

        _logger.LogInformation("Created and activated ProtoActor agent actor {Id}", id);

        return actor;
    }
}