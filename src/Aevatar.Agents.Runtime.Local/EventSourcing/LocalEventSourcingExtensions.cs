using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core.EventSourcing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Runtime.Local.EventSourcing;

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
        
        // 如果 Agent 支持 EventSourcing，配置并重放事件
        if (agent is GAgentBaseWithEventSourcing<object> esAgent)
        {
            // 注入 EventStore（如果还没有）
            if (eventStore != null && esAgent.GetEventStore() == null)
            {
                esAgent.SetEventStore(eventStore);
            }
            
            var logger = serviceProvider?.GetService<ILogger<LocalGAgentActor>>();
            logger?.LogInformation("Enabling EventSourcing for Local Agent {AgentId}", agent.Id);
            
            // 激活时重放事件
            await esAgent.OnActivateAsync();
            
            logger?.LogInformation("EventSourcing enabled, replayed to version {Version}", 
                esAgent.GetCurrentVersion());
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
        where TState : class, new()
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
/// EventStore 访问扩展（内部使用）
/// </summary>
internal static class EventSourcingInternals
{
    private static readonly System.Reflection.FieldInfo? EventStoreField =
        typeof(GAgentBaseWithEventSourcing<>)
            .GetField("_eventStore",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    public static IEventStore? GetEventStore<TState>(this GAgentBaseWithEventSourcing<TState> agent)
        where TState : class, new()
    {
        return EventStoreField?.GetValue(agent) as IEventStore;
    }

    public static void SetEventStore<TState>(this GAgentBaseWithEventSourcing<TState> agent, IEventStore eventStore)
        where TState : class, new()
    {
        EventStoreField?.SetValue(agent, eventStore);
    }
}