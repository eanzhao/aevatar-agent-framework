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
using Proto.Remote;
using Proto.Remote.GrpcNet;

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
    public static IServiceCollection AddAevatarProtoActorRuntime(
        this IServiceCollection services,
        Action<ActorSystemConfig>? configure = null)
    {
        // Create ProtoActor system configuration
        var systemConfig = ActorSystemConfig.Setup();
        configure?.Invoke(systemConfig);

        if (services.All(x => x.ServiceType != typeof(ActorSystem)))
        {
            services.AddSingleton(sp =>
            {
                var system = new ActorSystem();
                // Use standard RemoteConfig from Proto.Remote
                var remoteConfig = RemoteConfig.BindToLocalhost(8090);
                var remote = new GrpcNetRemote(system, remoteConfig);
                remote.StartAsync().Wait();
                return system;
            });
        }

        if (services.All(x => x.ServiceType != typeof(IRootContext)))
        {
            services.AddSingleton<IRootContext>(sp => sp.GetRequiredService<ActorSystem>().Root);
        }

        services.AddSingleton<IGAgentActorFactory, ProtoActorGAgentActorFactory>();
        services.AddSingleton<IGAgentActorManager, ProtoActorGAgentActorManager>();
        services.AddSingleton<ProtoActorMessageStreamRegistry>();
        services.AddSingleton<ProtoActorSubscriptionManager>();
        services.AddSingleton<IGAgentActorFactoryProvider, DefaultGAgentActorFactoryProvider>();
        services.AddSingleton<IGAgentFactory, AIGAgentFactory>();

        return services;
    }
}

