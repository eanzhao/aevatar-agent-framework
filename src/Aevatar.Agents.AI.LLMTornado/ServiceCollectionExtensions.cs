using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.LLMTornadoExtension;
using Aevatar.Agents.AI.Abstractions.Providers;
using LlmTornado;
using LlmTornado.Code;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.LLMTornado;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAevatarLLMTornado(this IServiceCollection services)
    {
        // Register the factory
        services.AddSingleton<ILLMProviderFactory, LLMTornadoProviderFactory>();
        return services;
    }

    public static IServiceCollection AddAevatarLLMTornado(this IServiceCollection services, Action<LlmTornadoConfig> configure)
    {
        var config = new LlmTornadoConfig();
        configure(config);

        services.AddSingleton(config);
        
        // Register the factory
        services.AddSingleton<ILLMProviderFactory, LLMTornadoProviderFactory>();
        
        // Also register the provider directly for simple use cases
        services.AddSingleton<TornadoApi>(sp =>
        {
            // LlmTornado 3.0.5 constructor: public TornadoApi(string apiKey, LLmProviders provider = LLmProviders.OpenAi)
            return new TornadoApi(config.ApiKey, config.Provider);
        });

        services.AddSingleton<IAevatarLLMProvider, LLMTornadoProvider>();

        return services;
    }
}
