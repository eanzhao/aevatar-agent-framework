using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Agent Framework 依赖注入扩展
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// 添加 Orleans Agent 支持
    /// </summary>
    public static IServiceCollection AddOrleansAgents(this IServiceCollection services)
    {
        services.AddSingleton<IGAgentActorFactory, OrleansGAgentActorFactory>();
        return services;
    }
}
