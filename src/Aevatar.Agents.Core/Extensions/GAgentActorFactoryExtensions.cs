using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Factory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Core.Extensions;

/// <summary>
/// Agent 工厂相关的扩展方法
/// </summary>
public static class GAgentActorFactoryExtensions
{
    /// <summary>
    /// 添加自动发现的 Agent 工厂支持（无需手动注册）
    /// </summary>
    public static IServiceCollection AddGAgentActorFactoryProvider(this IServiceCollection services)
    {
        services.TryAddSingleton<IGAgentActorFactoryProvider, AutoDiscoveryGAgentActorFactoryProvider>();
        return services;
    }
}