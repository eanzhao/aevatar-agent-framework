using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.Context;
using Aevatar.Agents.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Runtime.Orleans.Context;

/// <summary>
/// Extension methods for registering Orleans-specific agent context services.
/// </summary>
public static class OrleansAgentContextExtensions
{
    /// <summary>
    /// Adds agent context services using the Orleans RequestContext bridge.
    /// Use this when running in Orleans runtime.
    /// </summary>
    /// <param name="builder">The Aevatar builder</param>
    /// <returns>The builder for chaining</returns>
    public static IAevatarBuilder AddOrleansAgentContext(this IAevatarBuilder builder)
    {
        builder.Services.Replace(ServiceDescriptor.Singleton<IAgentContextAccessor, OrleansAgentContextAccessor>());
        builder.Services.TryAddSingleton<AgentContextPropagator>();
        return builder;
    }

    /// <summary>
    /// Adds agent context services using the Orleans RequestContext bridge.
    /// Use this when running in Orleans runtime.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddOrleansAgentContext(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IAgentContextAccessor, OrleansAgentContextAccessor>());
        services.TryAddSingleton<AgentContextPropagator>();
        return services;
    }
}

