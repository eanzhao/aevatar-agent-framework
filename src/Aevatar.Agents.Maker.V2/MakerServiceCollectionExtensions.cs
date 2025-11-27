using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Maker.V2;

// ============================================================
//  DI Extensions - Simple Registration
//  Directly uses Aevatar's ILLMProviderFactory
// ============================================================

/// <summary>
/// Extension methods for registering MAKER services.
/// </summary>
public static class MakerServiceCollectionExtensions
{
    /// <summary>
    /// Add MAKER V2 services to the DI container.
    /// Requires ILLMProviderFactory to be registered (e.g. via AddMEAI()).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="poolSize">Number of LLM instances for decorrelation.</param>
    /// <param name="temperatureVariance">Temperature variance for decorrelation.</param>
    public static IServiceCollection AddMakerV2(
        this IServiceCollection services,
        int poolSize = 3,
        float temperatureVariance = 0.1f)
    {
        // Register strategies with defaults
        services.AddSingleton<IDecompositionStrategy, DefaultDecomposer>();
        services.AddSingleton<ISolutionStrategy, DefaultSolver>();
        services.AddSingleton<ICompositionStrategy, DefaultComposer>();
        services.AddSingleton<IRedFlagHandler, DefaultRedFlagHandler>();

        // Register pool - directly uses ILLMProviderFactory
        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<ILLMProviderFactory>();
            return new ExecutionPool(factory, poolSize, temperatureVariance);
        });

        // Register executor
        services.AddTransient<IMakerExecutor>(sp =>
        {
            var pool = sp.GetRequiredService<ExecutionPool>();
            var redFlagHandler = sp.GetService<IRedFlagHandler>();
            return new MakerExecutor(pool, redFlagHandler);
        });

        return services;
    }

    /// <summary>
    /// Add MAKER V2 with custom pool configuration.
    /// </summary>
    public static IServiceCollection AddMakerV2(
        this IServiceCollection services,
        Func<IServiceProvider, ExecutionPool> poolFactory)
    {
        // Register strategies with defaults
        services.AddSingleton<IDecompositionStrategy, DefaultDecomposer>();
        services.AddSingleton<ISolutionStrategy, DefaultSolver>();
        services.AddSingleton<ICompositionStrategy, DefaultComposer>();
        services.AddSingleton<IRedFlagHandler, DefaultRedFlagHandler>();

        // Register custom pool
        services.AddSingleton(poolFactory);

        // Register executor
        services.AddTransient<IMakerExecutor>(sp =>
        {
            var pool = sp.GetRequiredService<ExecutionPool>();
            var redFlagHandler = sp.GetService<IRedFlagHandler>();
            return new MakerExecutor(pool, redFlagHandler);
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
