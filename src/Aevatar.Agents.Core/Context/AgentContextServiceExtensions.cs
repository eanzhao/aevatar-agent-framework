using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Core.Context;

/// <summary>
/// Extension methods for registering agent context services.
/// </summary>
public static class AgentContextServiceExtensions
{
    /// <summary>
    /// Property key for storing runtime type in builder properties.
    /// </summary>
    public const string RuntimeTypeKey = "AgentRuntimeType";

    /// <summary>
    /// Adds agent context services using the default AsyncLocal implementation.
    /// Use this for Local runtime or as a fallback.
    /// </summary>
    /// <param name="builder">The Aevatar builder</param>
    /// <returns>The builder for chaining</returns>
    public static IAevatarBuilder AddAgentContext(this IAevatarBuilder builder)
    {
        builder.Services.TryAddSingleton<IAgentContextAccessor, AsyncLocalAgentContextAccessor>();
        builder.Services.TryAddSingleton<AgentContextPropagator>();
        return builder;
    }

    /// <summary>
    /// Adds agent context services with a specific accessor implementation.
    /// </summary>
    /// <typeparam name="TAccessor">The accessor implementation type</typeparam>
    /// <param name="builder">The Aevatar builder</param>
    /// <returns>The builder for chaining</returns>
    public static IAevatarBuilder AddAgentContext<TAccessor>(this IAevatarBuilder builder)
        where TAccessor : class, IAgentContextAccessor
    {
        builder.Services.Replace(ServiceDescriptor.Singleton<IAgentContextAccessor, TAccessor>());
        builder.Services.TryAddSingleton<AgentContextPropagator>();
        return builder;
    }

    /// <summary>
    /// Adds agent context services with a specific accessor instance.
    /// </summary>
    /// <param name="builder">The Aevatar builder</param>
    /// <param name="accessor">The accessor instance to use</param>
    /// <returns>The builder for chaining</returns>
    public static IAevatarBuilder AddAgentContext(this IAevatarBuilder builder, IAgentContextAccessor accessor)
    {
        builder.Services.Replace(ServiceDescriptor.Singleton(accessor));
        builder.Services.TryAddSingleton<AgentContextPropagator>();
        return builder;
    }
}

/// <summary>
/// Extension methods for IServiceCollection to add agent context services directly.
/// </summary>
public static class AgentContextServiceCollectionExtensions
{
    /// <summary>
    /// Adds agent context services using the default AsyncLocal implementation.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddAgentContext(this IServiceCollection services)
    {
        services.TryAddSingleton<IAgentContextAccessor, AsyncLocalAgentContextAccessor>();
        services.TryAddSingleton<AgentContextPropagator>();
        return services;
    }

    /// <summary>
    /// Adds agent context services with a specific accessor implementation.
    /// </summary>
    /// <typeparam name="TAccessor">The accessor implementation type</typeparam>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddAgentContext<TAccessor>(this IServiceCollection services)
        where TAccessor : class, IAgentContextAccessor
    {
        services.Replace(ServiceDescriptor.Singleton<IAgentContextAccessor, TAccessor>());
        services.TryAddSingleton<AgentContextPropagator>();
        return services;
    }
}

