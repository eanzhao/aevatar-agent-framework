using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core.EventSourcing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Local.EventSourcing;

/// <summary>
/// Local 运行时的 EventSourcing 扩展
/// </summary>
public static class LocalEventSourcingExtensions
{
    /// <summary>
    /// 为 LocalGAgentActor 启用 EventSourcing
    /// </summary>
    public static async Task<IGAgentActor> WithEventSourcingAsync(
        this Task<IGAgentActor> actorTask,
        IEventStore? eventStore = null,
        IServiceProvider? serviceProvider = null)
    {
        var actor = await actorTask;
        
        // 获取 Agent 实例
        var agent = actor.GetAgent();
        
        // 使用反射检查是否支持 EventSourcing
        var agentType = agent.GetType();
        var baseType = agentType.BaseType;
        
        while (baseType != null)
        {
            if (baseType.IsGenericType && 
                baseType.GetGenericTypeDefinition() == typeof(GAgentBaseWithEventSourcing<>))
            {
                // 找到了 GAgentBaseWithEventSourcing 基类
                var setEventStoreMethod = baseType.GetMethod("SetEventStore");
                var getCurrentVersionMethod = baseType.GetMethod("GetCurrentVersion");
                var onActivateMethod = baseType.GetMethod("OnActivateAsync");
                
                if (setEventStoreMethod != null && eventStore != null)
                {
                    setEventStoreMethod.Invoke(agent, new object[] { eventStore });
                }
                
                var logger = serviceProvider?.GetService<ILogger<LocalGAgentActor>>();
                logger?.LogInformation("Enabling EventSourcing for Local Agent {AgentId}", agent.Id);
                
                // 激活时重放事件
                if (onActivateMethod != null)
                {
                    var task = onActivateMethod.Invoke(agent, new object[] { CancellationToken.None }) as Task;
                    if (task != null)
                    {
                        await task;
                    }
                }
                
                var version = getCurrentVersionMethod?.Invoke(agent, null);
                logger?.LogInformation("EventSourcing enabled, replayed to version {Version}", version);
                
                break;
            }
            
            baseType = baseType.BaseType;
        }
        
        return actor;
    }
    
    /// <summary>
    /// 创建支持 EventSourcing 的 LocalGAgentActor
    /// </summary>
    public static async Task<IGAgentActor> CreateWithEventSourcingAsync<TAgent, TState>(
        this LocalGAgentActorFactory factory,
        Guid id,
        IEventStore eventStore,
        IServiceProvider? serviceProvider = null)
        where TAgent : GAgentBaseWithEventSourcing<TState>, new()
        where TState : class, Google.Protobuf.IMessage<TState>, new()
    {
        // 创建 Agent 实例（需要通过构造函数设置 ID）
        var agent = Activator.CreateInstance(typeof(TAgent), id, eventStore, null) as TAgent
            ?? throw new InvalidOperationException($"Failed to create instance of {typeof(TAgent).Name}");
        
        // 创建 Actor (需要显式指定泛型参数)
        var actor = await factory.CreateGAgentActorAsync<TAgent>(agent.Id);
        
        // 激活并重放事件
        await agent.OnActivateAsync();
        
        return actor;
    }
}