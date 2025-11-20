using System;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Runtime.ProtoActor.Subscription;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.DependencyInjection;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// Extension methods for configuring the ProtoActor agent runtime in dependency injection.
/// 用于在依赖注入中配置ProtoActor运行时的扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the ProtoActor agent runtime core services to the service collection.
    /// 将ProtoActor运行时核心服务添加到服务集合
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure the ProtoActor system.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddProtoActorAgentRuntime(
        this IServiceCollection services,
        Action<ActorSystemConfig>? configure = null)
    {
        // Create ProtoActor system configuration
        var systemConfig = ActorSystemConfig.Setup();
        configure?.Invoke(systemConfig);

        // Register ProtoActor system
        services.AddSingleton(provider =>
        {
            var loggerFactory = provider.GetService<ILoggerFactory>();
            return new ActorSystem(systemConfig)
                .WithServiceProvider(provider);
        });
        
        // Register IRootContext from ActorSystem
        services.AddSingleton<IRootContext>(provider =>
        {
            var actorSystem = provider.GetRequiredService<ActorSystem>();
            return actorSystem.Root;
        });

        // Register ProtoActor dependencies
        services.AddSingleton<ProtoActorGAgentActorFactory>();
        services.AddSingleton<IGAgentActorFactory>(provider => 
            provider.GetRequiredService<ProtoActorGAgentActorFactory>());
        services.AddSingleton<ProtoActorGAgentActorManager>();
        services.AddSingleton<ProtoActorMessageStreamRegistry>();
        
        // Register the factory provider for auto-discovery
        services.TryAddSingleton<IGAgentActorFactoryProvider, DefaultGAgentActorFactoryProvider>();
        services.AddSingleton<IGAgentFactory, AIGAgentFactory>();

        return services;
    }
}

