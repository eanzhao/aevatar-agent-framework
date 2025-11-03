using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Local;
using Aevatar.Agents.Core.EventSourcing;

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
        
        switch (runtimeOptions.RuntimeType)
        {
            case AgentRuntimeType.Local:
                // 使用 Local 运行时
                services.AddSingleton<IGAgentActorFactory, LocalGAgentActorFactory>();
                Console.WriteLine("✅ 使用 Local 运行时");
                break;

            case AgentRuntimeType.ProtoActor:
                // ProtoActor 运行时
                var actorSystem = new Proto.ActorSystem();
                services.AddSingleton(actorSystem);
                services.AddSingleton<Aevatar.Agents.ProtoActor.ProtoActorMessageStreamRegistry>();
                services.AddSingleton<IGAgentActorFactory, Aevatar.Agents.ProtoActor.ProtoActorGAgentActorFactory>();
                Console.WriteLine("✅ 使用 ProtoActor 运行时");
                break;

            case AgentRuntimeType.Orleans:
                // Orleans 运行时（需要通过 Orleans Host 配置 Silo）
                // 配置 Orleans 工厂选项
                services.Configure<Aevatar.Agents.Orleans.OrleansGAgentActorFactoryOptions>(options =>
                {
                    options.UseEventSourcing = false; // 默认使用标准 Grain
                    options.DefaultGrainType = Aevatar.Agents.Orleans.GrainType.Standard;
                });
                services.AddSingleton<IGAgentActorFactory, Aevatar.Agents.Orleans.OrleansGAgentActorFactory>();
                Console.WriteLine("✅ 使用 Orleans 运行时");
                break;

            default:
                throw new InvalidOperationException($"Unknown runtime type: {runtimeOptions.RuntimeType}");
        }

        return services;
    }
}
