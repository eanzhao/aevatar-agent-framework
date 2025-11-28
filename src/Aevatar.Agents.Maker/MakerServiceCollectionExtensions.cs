using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Maker.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker;

// ============================================================
//  DI Extensions - Agent-based MAKER System
// ============================================================

/// <summary>
/// Extension methods for registering MAKER services.
/// Uses Aevatar Agent Framework for distributed multi-agent execution.
/// </summary>
public static class MakerServiceCollectionExtensions
{
    /// <summary>
    /// Add MAKER services using Aevatar Agent Framework.
    /// Each execution creates a Coordinator Agent with Worker Agents.
    /// Requires IGAgentActorFactory from the runtime (Local, Orleans, ProtoActor).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="defaultProviderName">Default LLM provider name from configuration.</param>
    public static IServiceCollection AddMakerSystem(
        this IServiceCollection services,
        string? defaultProviderName = null)
    {
        // Register strategies with defaults
        services.TryAddSingleton<IDecompositionStrategy, DefaultDecomposer>();
        services.TryAddSingleton<ISolutionStrategy, DefaultSolver>();
        services.TryAddSingleton<ICompositionStrategy, DefaultComposer>();
        services.TryAddSingleton<IRedFlagHandler, DefaultRedFlagHandler>();

        // Register Agent-based executor
        // Requires IGAgentActorFactory from the runtime (Local, Orleans, ProtoActor)
        services.AddTransient<IMakerExecutor>(sp =>
        {
            var actorFactory = sp.GetRequiredService<IGAgentActorFactory>();
            var logger = sp.GetRequiredService<ILogger<AgentMakerExecutor>>();
            var decomposer = sp.GetService<IDecompositionStrategy>();
            var solver = sp.GetService<ISolutionStrategy>();
            var composer = sp.GetService<ICompositionStrategy>();
            
            return new AgentMakerExecutor(
                actorFactory,
                logger,
                defaultProviderName,
                decomposer,
                solver,
                composer);
        });

        return services;
    }

    /// <summary>
    /// Add a custom decomposition strategy.
    /// </summary>
    public static IServiceCollection AddMakerDecomposer<T>(this IServiceCollection services)
        where T : class, IDecompositionStrategy
    {
        return services.AddSingleton<IDecompositionStrategy, T>();
    }

    /// <summary>
    /// Add a custom solution strategy.
    /// </summary>
    public static IServiceCollection AddMakerSolver<T>(this IServiceCollection services)
        where T : class, ISolutionStrategy
    {
        return services.AddSingleton<ISolutionStrategy, T>();
    }

    /// <summary>
    /// Add a custom composition strategy.
    /// </summary>
    public static IServiceCollection AddMakerComposer<T>(this IServiceCollection services)
        where T : class, ICompositionStrategy
    {
        return services.AddSingleton<ICompositionStrategy, T>();
    }
}
