using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.AI.MEAI.DependencyInjection;

// ReSharper disable InconsistentNaming
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMEAI(this IServiceCollection services)
    {
        services.AddSingleton<ILLMProviderFactory, MEAILLMProviderFactory>();
        return services;
    }
}