using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Aevatar.Agents.AI.MEAI;

// ReSharper disable InconsistentNaming
/// <summary>
/// Microsoft.Extensions.AI 实现的 LLM 提供商工厂
/// </summary>
public sealed class MEAILLMProviderFactory : LLMProviderFactoryBase
{
    private readonly IServiceProvider _serviceProvider;

    public MEAILLMProviderFactory(LLMProvidersConfig configuration, ILogger<MEAILLMProviderFactory> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger)
    {
        _serviceProvider = serviceProvider;
        RegisterProviders();
    }

    protected override void RegisterProviders()
    {
        foreach (var config in ProviderConfigs)
        {
            Providers[config.Key] = new Lazy<IAevatarLLMProvider>(() =>
            {
                Logger.LogInformation("Creating MEAI LLM provider: {ProviderName} (Type: {ProviderType})", config.Key,
                    config.Value.ProviderType);

                var chatClient = CreateChatClient(config.Value);
                var logger = _serviceProvider.GetRequiredService<ILogger<MEAILLMProvider>>();

                return new MEAILLMProvider(chatClient, config.Value, logger);
            });
        }
    }

    private IChatClient CreateChatClient(LLMProviderConfig config)
    {
        return config.ProviderType.ToLowerInvariant() switch
        {
            "openai" => CreateOpenAIChatClient(config),
            "azureopenai" or "azure_openai" => CreateAzureOpenAIChatClient(config),
            _ => throw new NotSupportedException($"Unsupported LLM provider type: {config.ProviderType}")
        };
    }

    private IChatClient CreateOpenAIChatClient(LLMProviderConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
            throw new InvalidOperationException("OpenAI API key is required");

        var openAIClient = new OpenAIClient(config.ApiKey);
        return openAIClient.GetChatClient(config.Model).AsIChatClient();
    }

    private IChatClient CreateAzureOpenAIChatClient(LLMProviderConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
            throw new InvalidOperationException("Azure OpenAI API key is required");

        if (string.IsNullOrEmpty(config.Endpoint))
            throw new InvalidOperationException("Azure OpenAI endpoint is required");

        var azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(
            new Uri(config.Endpoint),
            new Azure.AzureKeyCredential(config.ApiKey));

        return azureClient.GetChatClient(config.DeploymentName ?? config.Model).AsIChatClient();
    }
}