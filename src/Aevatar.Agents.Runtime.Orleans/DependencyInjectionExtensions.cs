using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Agent Framework 依赖注入扩展
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// 添加 Orleans Agent 支持
    /// </summary>
    public static IServiceCollection AddOrleansActorFactory(this IServiceCollection services)
    {
        services.AddSingleton<IGAgentActorFactory, OrleansGAgentActorFactory>();
        services.TryAddSingleton<IGAgentFactory, AIGAgentFactory>();
        return services;
    }
}
