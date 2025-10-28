using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Local;
using Aevatar.Agents.Orleans;
using Aevatar.Agents.ProtoActor;
using Aevatar.Agents.Serialization;
using Demo.Agents;

namespace Demo.Api;

/// <summary>
/// Agent运行时依赖注入扩展
/// </summary>
public static class AgentRuntimeExtensions
{
    /// <summary>
    /// 添加Agent运行时支持（基于配置自动选择）
    /// </summary>
    public static IServiceCollection AddAgentRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 读取配置
        var runtimeOptions = configuration
            .GetSection(AgentRuntimeOptions.SectionName)
            .Get<AgentRuntimeOptions>() ?? new AgentRuntimeOptions();

        services.Configure<AgentRuntimeOptions>(
            configuration.GetSection(AgentRuntimeOptions.SectionName));

        // 注册序列化器
        services.AddSingleton<IMessageSerializer, ProtobufSerializer>();

        // 注册业务Agent
        services.AddTransient<WeatherAgent>();
        services.AddTransient<CalculatorAgent>();

        // 根据配置选择运行时
        switch (runtimeOptions.RuntimeType)
        {
            case AgentRuntimeType.Local:
                services.AddLocalRuntime();
                break;

            case AgentRuntimeType.Orleans:
                services.AddOrleansRuntime(runtimeOptions.Orleans);
                break;

            case AgentRuntimeType.ProtoActor:
                services.AddProtoActorRuntime();
                break;

            default:
                throw new InvalidOperationException(
                    $"不支持的运行时类型: {runtimeOptions.RuntimeType}");
        }

        return services;
    }

    /// <summary>
    /// 添加Local运行时
    /// </summary>
    private static IServiceCollection AddLocalRuntime(this IServiceCollection services)
    {
        services.AddSingleton<IGAgentFactory, LocalGAgentFactory>();
        return services;
    }

    /// <summary>
    /// 添加Orleans运行时
    /// </summary>
    private static IServiceCollection AddOrleansRuntime(
        this IServiceCollection services,
        OrleansOptions options)
    {
        // 注册 Orleans Agent 工厂
        // IGrainFactory 由 UseOrleans() 在 Host 层提供
        services.AddOrleansAgents();
        
        Console.WriteLine("✅ Orleans 运行时已配置");
        return services;
    }

    /// <summary>
    /// 添加Proto.Actor运行时
    /// </summary>
    private static IServiceCollection AddProtoActorRuntime(this IServiceCollection services)
    {
        services.AddProtoActorAgents();
        return services;
    }
}

