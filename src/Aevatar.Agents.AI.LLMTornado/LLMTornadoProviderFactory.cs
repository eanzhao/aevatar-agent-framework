using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using LlmTornado;
using LlmTornado.Code;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.AI.LLMTornado;

/// <summary>
/// LlmTornado implementation of LLM Provider Factory.
/// Supports OpenAI, Anthropic, Google (Gemini), Azure, Groq, Cohere.
/// Also supports OpenAI-compatible APIs (DeepSeek, Moonshot, etc.) via custom endpoint.
/// </summary>
public sealed class LLMTornadoProviderFactory : LLMProviderFactoryBase
{
    private readonly IServiceProvider _serviceProvider;

    public LLMTornadoProviderFactory(IOptions<LLMProvidersConfig> configuration,
        ILogger<LLMTornadoProviderFactory> logger, IServiceProvider serviceProvider)
        : base(configuration, logger)
    {
        _serviceProvider = serviceProvider;
        RegisterProviders();
    }

    public override IAevatarLLMProvider CreateProvider(LLMProviderConfig providerConfig,
        CancellationToken cancellationToken = default)
    {
        var providerType = ParseProvider(providerConfig.ProviderType);
        var endpoint = providerConfig.Endpoint;
        var apiKey = providerConfig.ApiKey ?? throw new ArgumentException("ApiKey is required");
        var model = providerConfig.Model;

        // Create TornadoApi with correct endpoint handling
        TornadoApi api;
        
        if (!string.IsNullOrEmpty(endpoint))
        {
            // Custom endpoint (DeepSeek, Moonshot, vLLM, Ollama, etc.)
            // Use the constructor designed for self-hosted / custom providers
            api = new TornadoApi(new Uri(endpoint), apiKey, providerType);
        }
        else
        {
            // Native provider (OpenAI, Anthropic, Google, etc.)
            // Use ProviderAuthentication for built-in provider routing
            var auth = new ProviderAuthentication(providerType, apiKey);
            api = new TornadoApi([auth]);
        }

        var logger = _serviceProvider.GetRequiredService<ILogger<LLMTornadoProvider>>();
        return new LLMTornadoProvider(api, logger, providerType, model);
    }

    private static LLmProviders ParseProvider(string? providerType)
    {
        return (providerType?.ToLowerInvariant()) switch
        {
            "openai" => LLmProviders.OpenAi,
            "anthropic" or "claude" => LLmProviders.Anthropic,
            "azure" or "azureopenai" => LLmProviders.AzureOpenAi,
            "google" or "gemini" => LLmProviders.Google,
            "cohere" => LLmProviders.Cohere,
            "groq" => LLmProviders.Groq,
            // OpenAI-compatible APIs use OpenAI protocol
            "deepseek" or "moonshot" or "qwen" or "openai_compatible" => LLmProviders.OpenAi,
            _ => LLmProviders.OpenAi
        };
    }
}
