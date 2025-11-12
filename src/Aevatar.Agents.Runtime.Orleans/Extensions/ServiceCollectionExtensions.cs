using System;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Storage;

namespace Aevatar.Agents.Runtime.Orleans.Extensions;

/// <summary>
/// Extension methods for configuring the Orleans agent runtime in dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Orleans agent runtime to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure the Orleans silo.</param>
    /// <param name="configureHostConfig">Optional action to configure the default host configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOrleansAgentRuntime(
        this IServiceCollection services,
        Action<ISiloBuilder>? configure = null,
        Action<AgentHostConfiguration>? configureHostConfig = null)
    {
        // Register Orleans dependencies
        services.AddSingleton<OrleansGAgentActorFactory>();
        services.AddSingleton<IGAgentActorFactory>(provider => provider.GetRequiredService<OrleansGAgentActorFactory>());
        services.AddSingleton<OrleansGAgentActorManager>();
        services.AddSingleton<OrleansMessageStreamProvider>();
        
        // Register the factory provider for auto-discovery
        services.TryAddSingleton<IGAgentActorFactoryProvider, Aevatar.Agents.Core.Factory.AutoDiscoveryGAgentActorFactoryProvider>();
        
        // Register the Orleans runtime
        services.AddSingleton<IAgentRuntime>(provider =>
        {
            var logger = provider.GetService<ILogger<OrleansAgentRuntime>>();
            return new OrleansAgentRuntime(provider, logger);
        });
        
        // Add Orleans host
        // Note: The actual Orleans configuration (storage, streams, clustering) should be done
        // by the application, not by this library. This just provides the hook.
        if (configure != null)
        {
            services.AddOrleans(configure);
        }

        // Add default host configuration if provided
        if (configureHostConfig != null)
        {
            var defaultConfig = new AgentHostConfiguration
            {
                HostName = "DefaultOrleansHost"
            };
            configureHostConfig(defaultConfig);
            services.AddSingleton(defaultConfig);
        }

        return services;
    }

    /// <summary>
    /// Adds Orleans agent runtime with clustering configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clusterOptions">Orleans cluster configuration options.</param>
    /// <param name="configure">Optional action for additional Orleans configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOrleansAgentRuntimeWithClustering(
        this IServiceCollection services,
        ClusterOptions clusterOptions,
        Action<ISiloBuilder>? configure = null)
    {
        services.AddOrleans(builder =>
        {
            builder.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = clusterOptions.ClusterId;
                options.ServiceId = clusterOptions.ServiceId;
            });

            // Apply custom configuration
            configure?.Invoke(builder);
        });

        return services;
    }
}
