using System;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Runtime.Local.Extensions;

/// <summary>
/// Extension methods for configuring the Local agent runtime in dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Local agent runtime to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure the default host configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLocalAgentRuntime(
        this IServiceCollection services,
        Action<AgentHostConfiguration>? configure = null)
    {
        // Register the Local runtime dependencies
        services.AddSingleton<LocalGAgentActorFactory>();
        services.AddSingleton<IGAgentActorFactory>(provider => provider.GetRequiredService<LocalGAgentActorFactory>());
        services.AddSingleton<LocalGAgentActorManager>();
        services.AddSingleton<LocalMessageStreamRegistry>();
        services.AddSingleton<Subscription.LocalSubscriptionManager>();
        
        // Register the factory provider for auto-discovery
        services.TryAddSingleton<IGAgentActorFactoryProvider, Aevatar.Agents.Core.Factory.AutoDiscoveryGAgentActorFactoryProvider>();
        
        // Register the Local runtime
        services.AddSingleton<IAgentRuntime>(provider =>
        {
            var logger = provider.GetService<ILogger<LocalAgentRuntime>>();
            return new LocalAgentRuntime(provider, logger);
        });

        // Add default host configuration if provided
        if (configure != null)
        {
            var defaultConfig = new AgentHostConfiguration
            {
                HostName = "DefaultLocalHost"
            };
            configure(defaultConfig);
            services.AddSingleton(defaultConfig);
        }

        return services;
    }
}
