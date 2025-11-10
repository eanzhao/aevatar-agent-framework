using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Runtime.Local.Subscription;
using Aevatar.Agents.Runtime.Orleans.Subscription;
using Aevatar.Agents.Runtime.ProtoActor.Subscription;
using Aevatar.Agents.Core.EventDeduplication;
using Aevatar.Agents.Runtime.Orleans;
using Microsoft.Extensions.Logging;

namespace Demo.Api;

public static class AgentRuntimeExtensions
{
    public static IServiceCollection AddAgentRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var runtimeOptions = configuration
            .GetSection(AgentRuntimeOptions.SectionName)
            .Get<AgentRuntimeOptions>() ?? new AgentRuntimeOptions();

        // 注册Event Store（用于Event Sourcing）
        services.AddSingleton<IEventStore, InMemoryEventStore>();
        
        // 注册Event Deduplicator（共享组件）
        services.AddSingleton<IEventDeduplicator>(sp => 
            new MemoryCacheEventDeduplicator(new DeduplicationOptions
            {
                EventExpiration = TimeSpan.FromMinutes(5),
                MaxCachedEvents = 10_000,
                EnableAutoCleanup = true
            }));
        
        switch (runtimeOptions.RuntimeType)
        {
            case AgentRuntimeType.Local:
                // 使用 Local 运行时
                services.AddSingleton<IGAgentActorFactory, LocalGAgentActorFactory>();
                services.AddSingleton<LocalMessageStreamRegistry>();
                services.AddSingleton<ISubscriptionManager>(sp => 
                    new LocalSubscriptionManager(
                        sp.GetRequiredService<LocalMessageStreamRegistry>(),
                        sp.GetRequiredService<ILogger<LocalSubscriptionManager>>()));
                Console.WriteLine("✅ 使用 Local 运行时");
                break;

            case AgentRuntimeType.ProtoActor:
                // ProtoActor 运行时
                var actorSystem = new Proto.ActorSystem();
                services.AddSingleton(actorSystem);
                services.AddSingleton(actorSystem.Root);
                services.AddSingleton<Aevatar.Agents.Runtime.ProtoActor.ProtoActorMessageStreamRegistry>();
                services.AddSingleton<Aevatar.Agents.Runtime.ProtoActor.ProtoActorGAgentActorManager>();
                services.AddSingleton<IGAgentActorFactory, Aevatar.Agents.Runtime.ProtoActor.ProtoActorGAgentActorFactory>();
                services.AddSingleton<ISubscriptionManager>(sp =>
                    new ProtoActorSubscriptionManager(
                        sp.GetRequiredService<Proto.IRootContext>(),
                        sp.GetRequiredService<Aevatar.Agents.Runtime.ProtoActor.ProtoActorMessageStreamRegistry>(),
                        sp.GetRequiredService<Aevatar.Agents.Runtime.ProtoActor.ProtoActorGAgentActorManager>(),
                        sp.GetRequiredService<ILogger<ProtoActorSubscriptionManager>>()));
                Console.WriteLine("✅ 使用 ProtoActor 运行时");
                break;

            case AgentRuntimeType.Orleans:
                // Orleans 运行时（需要通过 Orleans Host 配置 Silo）
                // 配置 Orleans 工厂选项
                services.Configure<Aevatar.Agents.Runtime.Orleans.OrleansGAgentActorFactoryOptions>(options =>
                {
                    options.UseEventSourcing = false; // 默认使用标准 Grain
                    options.DefaultGrainType = Aevatar.Agents.Runtime.Orleans.GrainType.Standard;
                });
                services.AddSingleton<IGAgentActorFactory, Aevatar.Agents.Runtime.Orleans.OrleansGAgentActorFactory>();
                
                // Orleans的SubscriptionManager需要IStreamProvider
                // 注意：Orleans的流提供者需要在Silo配置中设置，这里只是注册Manager
                services.AddSingleton<ISubscriptionManager>(sp =>
                {
                    // 获取Orleans的流提供者
                    var client = sp.GetRequiredService<Orleans.IClusterClient>();
                    var streamProvider = client.GetStreamProvider("DefaultStreamProvider");
                    return new OrleansSubscriptionManager(
                        streamProvider,
                        AevatarAgentsOrleansConstants.StreamNamespace,
                        sp.GetRequiredService<ILogger<OrleansSubscriptionManager>>());
                });
                Console.WriteLine("✅ 使用 Orleans 运行时");
                break;

            default:
                throw new InvalidOperationException($"Unknown runtime type: {runtimeOptions.RuntimeType}");
        }

        return services;
    }
}
