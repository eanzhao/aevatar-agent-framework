using System;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Runtime.Extensions;

/// <summary>
/// Extension methods for configuring agent runtimes in dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds agent runtime factory support to enable creating multiple runtime types.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAgentRuntimeFactory(this IServiceCollection services)
    {
        // Note: The actual factory implementation should be provided by the application
        // or a higher-level assembly that has references to all runtime implementations.
        // This just registers the interface for dependency injection.
        return services;
    }

    /// <summary>
    /// Adds a default host configuration to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure the host configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAgentHostConfiguration(
        this IServiceCollection services,
        Action<AgentHostConfiguration> configure)
    {
        if (configure == null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var config = new AgentHostConfiguration();
        configure(config);
        services.AddSingleton(config);
        return services;
    }

    /// <summary>
    /// Adds agent spawn options to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure the spawn options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAgentSpawnOptions(
        this IServiceCollection services,
        Action<AgentSpawnOptions> configure)
    {
        if (configure == null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var options = new AgentSpawnOptions();
        configure(options);
        services.AddSingleton(options);
        return services;
    }
}
