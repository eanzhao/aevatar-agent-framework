using Aevatar.Agents.AI.Abstractions.Configuration;

namespace Aevatar.Agents.AI.Abstractions.Providers;

/// <summary>
/// LLM提供商工厂接口
/// </summary>
public interface ILLMProviderFactory
{
    IAevatarLLMProvider GetProvider(string providerName);
    IAevatarLLMProvider GetDefaultProvider();
    IReadOnlyList<string> GetAvailableProviderNames();
    bool HasProvider(string providerName);
    IAevatarLLMProvider CreateProvider(LLMProviderConfig providerConfig, CancellationToken cancellationToken = default);
    Task<IAevatarLLMProvider> GetProviderAsync(string providerName, CancellationToken cancellationToken = default);
    Task<IAevatarLLMProvider> GetDefaultProviderAsync(CancellationToken cancellationToken = default);
}