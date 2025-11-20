using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Runtime.Local.Subscription;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Extension methods for configuring the Local agent runtime in dependency injection.
/// 用于在依赖注入中配置Local运行时的扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Local agent runtime core services to the service collection.
    /// 将Local运行时核心服务添加到服务集合
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAevatarLocalRuntime(this IServiceCollection services)
    {
        services.AddSingleton<LocalGAgentActorFactory>();
        services.AddSingleton<IGAgentActorFactory>(provider =>
            provider.GetRequiredService<LocalGAgentActorFactory>());
        services.AddSingleton<IGAgentActorManager, LocalGAgentActorManager>();
        services.AddSingleton<LocalMessageStreamRegistry>();
        services.AddSingleton<LocalSubscriptionManager>();

        services.AddSingleton<IGAgentActorFactoryProvider, DefaultGAgentActorFactoryProvider>();
        services.AddSingleton<IGAgentFactory, AIGAgentFactory>();

        return services;
    }
}