using System;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Runtime.Orleans.Subscription;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Extension methods for configuring the Orleans agent runtime in dependency injection.
/// 用于在依赖注入中配置Orleans运行时的扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Orleans agent runtime core services to the service collection.
    /// 将Orleans运行时核心服务添加到服务集合
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure the Orleans silo.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOrleansAgentRuntime(
        this IServiceCollection services,
        Action<ISiloBuilder>? configure = null)
    {
        // Register Orleans dependencies
        services.AddSingleton<OrleansGAgentActorFactory>();
        services.AddSingleton<IGAgentActorFactory>(provider => 
            provider.GetRequiredService<OrleansGAgentActorFactory>());
        services.AddSingleton<OrleansGAgentActorManager>();
        services.AddSingleton<OrleansMessageStreamProvider>();
        
        // Register the factory provider for auto-discovery
        services.TryAddSingleton<IGAgentActorFactoryProvider, AutoDiscoveryGAgentActorFactoryProvider>();
        
        // Add Orleans host configuration if provided
        if (configure != null)
        {
            services.AddOrleans(configure);
        }

        return services;
    }

    /// <summary>
    /// Adds Orleans agent runtime with clustering configuration.
    /// 添加Orleans运行时并配置集群
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

        return AddOrleansAgentRuntime(services);
    }
}

