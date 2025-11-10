using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core.EventSourcing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.ProtoActor.EventSourcing;

/// <summary>
/// ProtoActor 运行时的 EventSourcing 扩展
/// </summary>
public static class ProtoActorEventSourcingExtensions
{
    /// <summary>
    /// 为 ProtoActorGAgentActor 启用 EventSourcing
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
                var setEventStoreMethod = baseType.GetMethod("SetEventStore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var getCurrentVersionMethod = baseType.GetMethod("GetCurrentVersion", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var onActivateMethod = baseType.GetMethod("OnActivateAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                
                if (setEventStoreMethod != null && eventStore != null)
                {
                    setEventStoreMethod.Invoke(agent, new object[] { eventStore });
                }
                
                var logger = serviceProvider?.GetService<ILogger<ProtoActorGAgentActor>>();
                logger?.LogInformation("Enabling EventSourcing for ProtoActor Agent {AgentId}", agent.Id);
                
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
                logger?.LogInformation("EventSourcing enabled for ProtoActor, replayed to version {Version}", version);
                
                break;
            }
            
            baseType = baseType.BaseType;
        }
        
        return actor;
    }
    
    /// <summary>
    /// 创建支持 EventSourcing 的 ProtoActorGAgentActor
    /// </summary>
    public static async Task<IGAgentActor> CreateWithEventSourcingAsync<TAgent, TState>(
        this ProtoActorGAgentActorFactory factory,
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

/// <summary>
/// ProtoActor Persistence 集成点（未来扩展）
/// </summary>
public static class ProtoActorPersistenceExtensions
{
    /// <summary>
    /// 配置 Proto.Persistence（未来实现）
    /// </summary>
    /// <remarks>
    /// 未来可以集成：
    /// - Proto.Persistence.MongoDB
    /// - Proto.Persistence.SqlServer
    /// - Proto.Persistence.Sqlite
    /// - Proto.Persistence.EventStore
    /// 
    /// 示例：
    /// var props = Props.FromProducer(() => new MyActor())
    ///     .WithPersistence(mongoDbProvider, "events");
    /// </remarks>
    public static void ConfigureProtoPersistence(this ActorSystem system, string connectionString)
    {
        // TODO: 实现 Proto.Persistence 集成
        // 1. 安装 Proto.Persistence.* 包
        // 2. 配置 Provider
        // 3. 实现 IEventSourced 接口
    }
}