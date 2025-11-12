using System;
using Aevatar.Agents.Runtime;
using Aevatar.Agents.Runtime.Local;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Runtime.Extensions;

/// <summary>
/// Extension methods for configuring agent runtimes in dependency injection.
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
        services.AddSingleton<Aevatar.Agents.Runtime.Local.LocalGAgentActorManager>();
        services.AddSingleton<Aevatar.Agents.Runtime.Local.LocalMessageStreamRegistry>();
        
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

    /// <summary>
    /// Adds multiple agent runtime support with a factory pattern.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAgentRuntimeFactory(this IServiceCollection services)
    {
        services.AddSingleton<IAgentRuntimeFactory, AgentRuntimeFactory>();
        return services;
    }

    /// <summary>
    /// Adds agent runtime with automatic selection based on configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="runtimeType">The type of runtime to use (Local, ProtoActor, Orleans).</param>
    /// <param name="configure">Optional configuration for the runtime.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAgentRuntime(
        this IServiceCollection services,
        string runtimeType,
        Action<AgentHostConfiguration>? configure = null)
    {
        switch (runtimeType?.ToLowerInvariant())
        {
            case "local":
                services.AddLocalAgentRuntime(configure);
                break;
                
            case "protoactor":
                // TODO: Add ProtoActor runtime when implemented
                throw new NotImplementedException("ProtoActor runtime is not yet implemented");
                
            case "orleans":
                // TODO: Add Orleans runtime when implemented
                throw new NotImplementedException("Orleans runtime is not yet implemented");
                
            default:
                throw new ArgumentException($"Unknown runtime type: {runtimeType}", nameof(runtimeType));
        }

        return services;
    }
}

/// <summary>
/// Factory interface for creating agent runtimes.
/// </summary>
public interface IAgentRuntimeFactory
{
    /// <summary>
    /// Creates an agent runtime of the specified type.
    /// </summary>
    /// <param name="runtimeType">The type of runtime to create.</param>
    /// <returns>The created agent runtime.</returns>
    IAgentRuntime CreateRuntime(string runtimeType);
    
    /// <summary>
    /// Gets an existing runtime or creates a new one if it doesn't exist.
    /// </summary>
    /// <param name="runtimeType">The type of runtime.</param>
    /// <returns>The agent runtime.</returns>
    IAgentRuntime GetOrCreateRuntime(string runtimeType);
}

/// <summary>
/// Default implementation of the agent runtime factory.
/// </summary>
public class AgentRuntimeFactory : IAgentRuntimeFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, IAgentRuntime> _runtimes = new();
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentRuntimeFactory"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    public AgentRuntimeFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public IAgentRuntime CreateRuntime(string runtimeType)
    {
        switch (runtimeType?.ToLowerInvariant())
        {
            case "local":
                return new LocalAgentRuntime(
                    _serviceProvider,
                    _serviceProvider.GetService<ILogger<LocalAgentRuntime>>()
                );
                
            case "protoactor":
                // TODO: Implement ProtoActor runtime
                throw new NotImplementedException("ProtoActor runtime is not yet implemented");
                
            case "orleans":
                // TODO: Implement Orleans runtime
                throw new NotImplementedException("Orleans runtime is not yet implemented");
                
            default:
                throw new ArgumentException($"Unknown runtime type: {runtimeType}", nameof(runtimeType));
        }
    }

    /// <inheritdoc />
    public IAgentRuntime GetOrCreateRuntime(string runtimeType)
    {
        lock (_lock)
        {
            if (_runtimes.TryGetValue(runtimeType, out var runtime))
            {
                return runtime;
            }

            runtime = CreateRuntime(runtimeType);
            _runtimes[runtimeType] = runtime;
            return runtime;
        }
    }
}
