using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.ProtoActor;

/// <summary>
/// 依赖注入扩展方法
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// 添加Proto.Actor实现的代理服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddProtoActorAgents(this IServiceCollection services)
    {
        // 注册工厂
        services.AddSingleton<IGAgentActorFactory, ProtoActorGAgentActorFactory>();

        return services;
    }
}