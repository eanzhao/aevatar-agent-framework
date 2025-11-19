using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.AI.Abstractions.Tests.LLMProvider;

/// <summary>
/// Test implementation of ILLMProviderFactory using DI
/// </summary>
public class MockLLMProviderFactory : LLMProviderFactoryBase
{
    public MockLLMProviderFactory(
        IOptions<LLMProvidersConfig> config,
        ILogger<MockLLMProviderFactory> logger)
        : base(config, logger)
    {
    }

    public override IAevatarLLMProvider CreateProvider(
        LLMProviderConfig providerConfig,
        CancellationToken cancellationToken = default)
    {
        // Create mock providers for testing
        var mockProvider = new MockLLMProvider(
            new AevatarLLMResponse
            {
                Content = $"Test response from {providerConfig.Name}",
                AevatarStopReason = AevatarStopReason.Complete
            });

        // Configure based on provider config
        if (providerConfig.Model != null)
        {
            mockProvider.SetModelInfo(new AevatarModelInfo
            {
                Name = providerConfig.Model,
                MaxTokens = providerConfig.MaxTokens,
                SupportsStreaming = true,
                SupportsFunctions = providerConfig.ProviderType == "openai"
            });
        }

        return mockProvider;
    }
}