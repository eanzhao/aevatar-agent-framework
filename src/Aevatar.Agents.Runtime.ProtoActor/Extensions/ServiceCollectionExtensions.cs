using System;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.DependencyInjection;

namespace Aevatar.Agents.Runtime.ProtoActor.Extensions;

/// <summary>
/// Extension methods for configuring the ProtoActor agent runtime in dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the ProtoActor agent runtime to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure the ProtoActor system.</param>
    /// <param name="configureHostConfig">Optional action to configure the default host configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddProtoActorAgentRuntime(
        this IServiceCollection services,
        Action<ActorSystemConfig>? configure = null,
        Action<AgentHostConfiguration>? configureHostConfig = null)
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
        services.AddSingleton<IGAgentActorFactory>(provider => provider.GetRequiredService<ProtoActorGAgentActorFactory>());
        services.AddSingleton<ProtoActorGAgentActorManager>();
        services.AddSingleton<ProtoActorMessageStreamRegistry>();
        
        // Register the factory provider for auto-discovery
        services.TryAddSingleton<IGAgentActorFactoryProvider, Aevatar.Agents.Core.Factory.AutoDiscoveryGAgentActorFactoryProvider>();
        
        // Register the ProtoActor runtime
        services.AddSingleton<IAgentRuntime>(provider =>
        {
            var logger = provider.GetService<ILogger<ProtoActorAgentRuntime>>();
            return new ProtoActorAgentRuntime(provider, logger);
        });

        // Add default host configuration if provided
        if (configureHostConfig != null)
        {
            var defaultConfig = new AgentHostConfiguration
            {
                HostName = "DefaultProtoActorHost"
            };
            configureHostConfig(defaultConfig);
            services.AddSingleton(defaultConfig);
        }

        return services;
    }
}
