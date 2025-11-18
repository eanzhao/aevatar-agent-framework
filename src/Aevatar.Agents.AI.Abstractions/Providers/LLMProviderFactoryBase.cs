using Aevatar.Agents.AI.Abstractions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.AI.Abstractions.Providers;

/// <summary>
/// LLM Provider Factory 抽象基类，提供公共实现
/// </summary>
public abstract class LLMProviderFactoryBase : ILLMProviderFactory
{
    protected readonly LLMProvidersConfig Config;
    protected readonly ILogger Logger;
    protected readonly Dictionary<string, Lazy<IAevatarLLMProvider>> Providers = new();
    protected readonly Dictionary<string, LLMProviderConfig> ProviderConfigs;

    protected LLMProviderFactoryBase(IOptions<LLMProvidersConfig> config, ILogger logger)
    {
        if (config?.Value == null)
            throw new ArgumentNullException(nameof(config));

        Config = config.Value;
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ProviderConfigs = new Dictionary<string, LLMProviderConfig>(Config.Providers);
        RegisterProviders();
    }

    public IAevatarLLMProvider GetProvider(string providerName)
    {
        if (string.IsNullOrEmpty(providerName))
            throw new ArgumentNullException(nameof(providerName));

        if (Providers.TryGetValue(providerName, out var provider))
            return provider.Value;

        throw new KeyNotFoundException(
            $"Provider '{providerName}' not found. Available providers: {string.Join(", ", GetAvailableProviderNames())}");
    }

    public IAevatarLLMProvider GetDefaultProvider()
    {
        return GetProvider(Config.Default);
    }

    public IReadOnlyList<string> GetAvailableProviderNames()
    {
        return new List<string>(Providers.Keys).AsReadOnly();
    }

    public bool HasProvider(string providerName)
    {
        if (string.IsNullOrEmpty(providerName))
            return false;

        return Providers.ContainsKey(providerName);
    }

    public abstract IAevatarLLMProvider CreateProvider(LLMProviderConfig providerConfig,
        CancellationToken cancellationToken = default);

    public async Task<IAevatarLLMProvider> GetProviderAsync(string providerName,
        CancellationToken cancellationToken = default)
    {
        // 异步版本可以在这里添加异步初始化逻辑
        return await Task.FromResult(GetProvider(providerName));
    }

    public async Task<IAevatarLLMProvider> GetDefaultProviderAsync(CancellationToken cancellationToken = default)
    {
        return await GetProviderAsync(Config.Default, cancellationToken);
    }

    protected virtual void RegisterProviders()
    {
        foreach (var config in ProviderConfigs)
        {
            Providers[config.Key] = new Lazy<IAevatarLLMProvider>(() => CreateProvider(config.Value));
        }
    }
}