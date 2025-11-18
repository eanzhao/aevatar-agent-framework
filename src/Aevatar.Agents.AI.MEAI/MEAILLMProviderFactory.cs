using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Aevatar.Agents.AI.MEAI;

// ReSharper disable InconsistentNaming
/// <summary>
/// Microsoft.Extensions.AI 实现的 LLM 提供商工厂
/// </summary>
public sealed class MEAILLMProviderFactory : LLMProviderFactoryBase
{
    private readonly IServiceProvider _serviceProvider;

    public MEAILLMProviderFactory(IOptions<LLMProvidersConfig> configuration, ILogger<MEAILLMProviderFactory> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger)
    {
        _serviceProvider = serviceProvider;
        RegisterProviders();
    }

    public override IAevatarLLMProvider CreateProvider(LLMProviderConfig providerConfig, CancellationToken cancellationToken = default)
    {
        var chatClient = CreateChatClient(providerConfig);
        var logger = _serviceProvider.GetRequiredService<ILogger<MEAILLMProvider>>();
        return new MEAILLMProvider(chatClient, providerConfig, logger);
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