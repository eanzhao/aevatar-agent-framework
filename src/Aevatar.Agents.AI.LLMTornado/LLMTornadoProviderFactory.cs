using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using LlmTornado;
using LlmTornado.Code;

namespace Aevatar.Agents.AI.LLMTornadoExtension;

/// <summary>
/// LlmTornado implementation of LLM Provider Factory
/// </summary>
public sealed class LLMTornadoProviderFactory : LLMProviderFactoryBase
{
    private readonly IServiceProvider _serviceProvider;

    public LLMTornadoProviderFactory(IOptions<LLMProvidersConfig> configuration, ILogger<LLMTornadoProviderFactory> logger, IServiceProvider serviceProvider)
        : base(configuration, logger)
    {
        _serviceProvider = serviceProvider;
        RegisterProviders();
    }

    public override IAevatarLLMProvider CreateProvider(LLMProviderConfig providerConfig, CancellationToken cancellationToken = default)
    {
        var config = new LlmTornadoConfig
        {
            ApiKey = providerConfig.ApiKey,
            Provider = ParseProvider(providerConfig.ProviderType)
        };
        
        var api = new TornadoApi(new List<ProviderAuthentication>
        {
            new ProviderAuthentication(config.Provider, config.ApiKey)
        });

        var logger = _serviceProvider.GetRequiredService<ILogger<LLMTornadoProvider>>();
        return new LLMTornadoProvider(api, config, logger);
    }

    private LLmProviders ParseProvider(string providerType)
    {
        return providerType.ToLowerInvariant() switch
        {
            "openai" => LLmProviders.OpenAi,
            "anthropic" => LLmProviders.Anthropic,
            "azure" or "azureopenai" => LLmProviders.AzureOpenAi,
            "google" or "gemini" => LLmProviders.Google,
            "cohere" => LLmProviders.Cohere,
            "groq" => LLmProviders.Groq,
            _ => LLmProviders.OpenAi // Default to OpenAI
        };
    }
}
