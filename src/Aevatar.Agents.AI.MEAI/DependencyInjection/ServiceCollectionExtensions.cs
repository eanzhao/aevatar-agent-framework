using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core.Embeddings;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.AI.MEAI.DependencyInjection;

// ReSharper disable InconsistentNaming
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMEAI(this IServiceCollection services)
    {
        services.AddSingleton<ILLMProviderFactory, MEAILLMProviderFactory>();
        services.AddSingleton<IAIAgentEmbeddingFactory, MEAIEmbeddingFactory>();
        return services;
    }
}