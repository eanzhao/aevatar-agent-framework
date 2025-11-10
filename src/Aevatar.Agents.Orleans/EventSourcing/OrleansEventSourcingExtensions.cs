using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core.EventSourcing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Orleans.EventSourcing;

/// <summary>
/// Orleans 运行时的 EventSourcing 扩展
/// </summary>
public static class OrleansEventSourcingExtensions
{
    /// <summary>
    /// 为 OrleansGAgentActor 启用 EventSourcing
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
            if (eventStore != null)
            {
                var field = typeof(GAgentBaseWithEventSourcing<>)
                    .MakeGenericType(esAgent.GetType().BaseType!.GetGenericArguments()[0])
                    .GetField("_eventStore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (field != null && field.GetValue(esAgent) == null)
                {
                    field.SetValue(esAgent, eventStore);
                }
            }
            
            var logger = serviceProvider?.GetService<ILogger<OrleansGAgentActor>>();
            logger?.LogInformation("Enabling EventSourcing for Orleans Agent {AgentId}", agent.Id);
            
            // 激活时重放事件
            await esAgent.OnActivateAsync();
            
            logger?.LogInformation("EventSourcing enabled for Orleans, replayed to version {Version}", 
                esAgent.GetCurrentVersion());
        }
        
        return actor;
    }
    
    /// <summary>
    /// 配置 Orleans Silo 以支持 EventSourcing
    /// </summary>
    public static ISiloBuilder AddAgentEventSourcing(
        this ISiloBuilder builder,
        Action<EventSourcingOptions>? configureOptions = null)
    {
        var options = new EventSourcingOptions();
        configureOptions?.Invoke(options);
        
        // 注册 EventStore
        builder.ConfigureServices(services =>
        {
            if (options.UseInMemoryStore)
            {
                services.AddSingleton<IEventStore, InMemoryEventStore>();
            }
            
            // 注册其他服务
            services.AddSingleton(options);
        });
        
        // 配置 Grain Storage（如果需要）
        if (!string.IsNullOrEmpty(options.StorageProvider))
        {
            // 未来可以配置不同的存储提供者
            // builder.AddAzureTableGrainStorage("EventStore", ...);
            // builder.AddAdoNetGrainStorage("EventStore", ...);
        }
        
        return builder;
    }
    
    /// <summary>
    /// 配置客户端以支持 EventSourcing
    /// </summary>
    public static IClientBuilder AddAgentEventSourcing(
        this IClientBuilder builder,
        Action<EventSourcingOptions>? configureOptions = null)
    {
        var options = new EventSourcingOptions();
        configureOptions?.Invoke(options);
        
        builder.ConfigureServices(services =>
        {
            if (options.UseInMemoryStore)
            {
                services.AddSingleton<IEventStore, InMemoryEventStore>();
            }
            
            services.AddSingleton(options);
        });
        
        return builder;
    }
}

/// <summary>
/// EventSourcing 配置选项
/// </summary>
public class EventSourcingOptions
{
    /// <summary>
    /// 使用内存存储（开发/测试）
    /// </summary>
    public bool UseInMemoryStore { get; set; } = true;
    
    /// <summary>
    /// 存储提供者名称
    /// </summary>
    public string? StorageProvider { get; set; }
    
    /// <summary>
    /// 快照间隔（默认每100个事件）
    /// </summary>
    public int SnapshotInterval { get; set; } = 100;
    
    /// <summary>
    /// 是否启用自动快照
    /// </summary>
    public bool EnableAutoSnapshot { get; set; } = true;
}

/// <summary>
/// JournaledGrain 集成
/// </summary>
public static class OrleansJournaledGrainExtensions
{
    /// <summary>
    /// 配置 JournaledGrain 支持
    /// </summary>
    public static ISiloBuilder AddJournaledGrainEventSourcing(
        this ISiloBuilder builder,
        Action<JournaledGrainOptions>? configureOptions = null)
    {
        var options = new JournaledGrainOptions();
        configureOptions?.Invoke(options);
        
        // 配置 LogConsistency Provider
        if (options.UseLogStorage)
        {
            builder.AddLogStorageBasedLogConsistencyProvider("LogStorage");
        }
        
        if (options.UseStateStorage)
        {
            builder.AddStateStorageBasedLogConsistencyProvider("StateStorage");
        }
        
        if (options.UseCustomStorage)
        {
            builder.AddCustomStorageBasedLogConsistencyProvider("CustomStorage");
        }
        
        // 配置默认存储
        if (options.UseMemoryStorage)
        {
            // 使用内存存储（需要 Microsoft.Orleans.Persistence.Memory 包）
            // builder.AddMemoryGrainStorage("Default");
        }
        
        return builder;
    }
    
    /// <summary>
    /// 创建使用 JournaledGrain 的 Actor
    /// </summary>
    public static async Task<IGAgentActor> CreateJournaledAgentAsync<TAgent, TState>(
        this OrleansGAgentActorFactory factory,
        Guid id,
        IClusterClient clusterClient,
        IServiceProvider? serviceProvider = null)
        where TAgent : GAgentBaseWithEventSourcing<TState>, new()
        where TState : class, Google.Protobuf.IMessage<TState>, new()
    {
        // 创建 Agent 实例
        var agent = new TAgent();
        
        // 获取 JournaledGrain
        var grain = clusterClient.GetGrain<IGAgentGrain>(id.ToString());
        
        // 创建 Actor wrapper
        var actor = new OrleansGAgentActor(grain, agent);
        
        return actor;
    }
}

/// <summary>
/// JournaledGrain 配置选项
/// </summary>
public class JournaledGrainOptions
{
    /// <summary>
    /// 使用内存存储（开发/测试）
    /// </summary>
    public bool UseMemoryStorage { get; set; } = true;
    
    /// <summary>
    /// 使用 LogStorage Provider
    /// </summary>
    public bool UseLogStorage { get; set; } = true;
    
    /// <summary>
    /// 使用 StateStorage Provider
    /// </summary>
    public bool UseStateStorage { get; set; } = false;
    
    /// <summary>
    /// 使用自定义存储
    /// </summary>
    public bool UseCustomStorage { get; set; } = false;
    
    /// <summary>
    /// 自定义存储工厂
    /// </summary>
    public Func<IServiceProvider, object>? CustomStorageFactory { get; set; }
}
