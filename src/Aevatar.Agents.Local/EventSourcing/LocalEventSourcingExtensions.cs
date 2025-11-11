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
    /// Uses shared EventSourcingHelper for optimal performance
    /// </summary>
    public static async Task<IGAgentActor> WithEventSourcingAsync(
        this Task<IGAgentActor> actorTask,
        IEventStore? eventStore = null,
        IServiceProvider? serviceProvider = null)
    {
        var actor = await actorTask;
            var logger = serviceProvider?.GetService<ILogger<LocalGAgentActor>>();
        
        // Use shared helper with MethodInfo caching (5-10x faster after first call)
        return await EventSourcingHelper.EnableEventSourcingAsync(actor, eventStore, logger);
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