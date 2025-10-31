using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Local;

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
                services.AddSingleton<IGAgentActorFactory, Aevatar.Agents.ProtoActor.ProtoActorGAgentActorFactory>();
                Console.WriteLine("✅ 使用 ProtoActor 运行时");
                break;

            case AgentRuntimeType.Orleans:
                // Orleans 运行时（需要通过 Orleans Host 配置 Silo）
                services.AddSingleton<IGAgentActorFactory, Aevatar.Agents.Orleans.OrleansGAgentActorFactory>();
                Console.WriteLine("✅ 使用 Orleans 运行时");
                break;

            default:
                throw new InvalidOperationException($"Unknown runtime type: {runtimeOptions.RuntimeType}");
        }

        return services;
    }
}
