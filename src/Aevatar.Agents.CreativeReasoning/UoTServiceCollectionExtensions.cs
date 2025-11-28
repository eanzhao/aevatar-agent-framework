using Aevatar.Agents.Abstractions;
using Aevatar.Agents.CreativeReasoning.Execution;
using Aevatar.Agents.CreativeReasoning.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.CreativeReasoning;

/// <summary>
/// Extension methods for registering UoT services.
/// </summary>
public static class UoTServiceCollectionExtensions
{
    /// <summary>
    /// Add Universe of Thoughts (UoT) creative reasoning services.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="defaultProviderName">Default LLM provider name (e.g., "deepseek")</param>
    public static IServiceCollection AddUoTCreativeReasoning(
        this IServiceCollection services,
        string? defaultProviderName = null)
    {
        // Register strategies as singletons
        services.AddSingleton<IAnalogyStrategy, DefaultAnalogyStrategy>();
        services.AddSingleton<IThoughtDecompositionStrategy, DefaultThoughtDecompositionStrategy>();
        services.AddSingleton<IHostSelectionStrategy, DefaultHostSelectionStrategy>();
        services.AddSingleton<IDonorSelectionStrategy, DefaultDonorSelectionStrategy>();
        services.AddSingleton<ISynthesisStrategy, DefaultSynthesisStrategy>();
        services.AddSingleton<IEvaluationStrategy, DefaultEvaluationStrategy>();

        // Register executor with IGAgentActorFactory
        services.AddTransient<IUoTExecutor>(sp =>
        {
            var actorFactory = sp.GetRequiredService<IGAgentActorFactory>();
            var logger = sp.GetRequiredService<ILogger<UoTExecutor>>();
            return new UoTExecutor(actorFactory, logger, defaultProviderName);
        });

        return services;
    }
}

