using System.ClientModel;
using System.ClientModel.Primitives;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

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

    public override IAevatarLLMProvider CreateProvider(LLMProviderConfig providerConfig,
        CancellationToken cancellationToken = default)
    {
        var chatClient = CreateChatClient(providerConfig);
        var logger = _serviceProvider.GetRequiredService<ILogger<MEAILLMProvider>>();
        return new MEAILLMProvider(chatClient, providerConfig, logger);
    }

    private IChatClient CreateChatClient(LLMProviderConfig config)
    {
        return config.ProviderType.ToLowerInvariant() switch
        {
            "azureopenai" or "azure_openai" => CreateAzureOpenAIChatClient(config),
            _ => CreateOpenAIChatClient(config)
        };
    }

    private IChatClient CreateOpenAIChatClient(LLMProviderConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
            throw new InvalidOperationException("OpenAI API key is required");

        var clientOptions = new OpenAIClientOptions
        {
            ClientLoggingOptions = CreateClientLoggingOptions()
        };

        if (!string.IsNullOrWhiteSpace(config.Endpoint))
            clientOptions.Endpoint = new Uri(config.Endpoint);

        return new ChatClient(config.Model, new ApiKeyCredential(config.ApiKey), clientOptions).AsIChatClient();
    }

    private IChatClient CreateAzureOpenAIChatClient(LLMProviderConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
            throw new InvalidOperationException("Azure OpenAI API key is required");

        if (string.IsNullOrEmpty(config.Endpoint))
            throw new InvalidOperationException("Azure OpenAI endpoint is required");

        var clientOptions = new AzureOpenAIClientOptions
        {
            ClientLoggingOptions = CreateClientLoggingOptions()
        };

        var azureClient = new AzureOpenAIClient(
            new Uri(config.Endpoint),
            new AzureKeyCredential(config.ApiKey),
            clientOptions);

        return azureClient.GetChatClient(config.DeploymentName ?? config.Model).AsIChatClient();
    }

    private ClientLoggingOptions CreateClientLoggingOptions()
    {
        var loggingOptions = new ClientLoggingOptions
        {
            LoggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>(),
            EnableLogging = true,
            EnableMessageLogging = true,
            EnableMessageContentLogging = true,
            MessageContentSizeLimit = 64 * 1024
        };

        loggingOptions.AllowedHeaderNames.Add("Content-Type");
        loggingOptions.AllowedHeaderNames.Add("Accept");
        loggingOptions.AllowedHeaderNames.Add("Content-Length");
        loggingOptions.AllowedHeaderNames.Add("x-ms-request-id");
        loggingOptions.AllowedQueryParameters.Add("api-version");

        return loggingOptions;
    }
}