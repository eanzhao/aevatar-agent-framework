using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.Core.Tests.Agents.AI;

namespace Aevatar.Agents.AI.Abstractions.Tests;

/// <summary>
/// Mock implementation of ILLMProviderFactory for testing
/// </summary>
public class MockLLMProviderFactory : ILLMProviderFactory
{
    private readonly Dictionary<string, IAevatarLLMProvider> _providers = new();
    private IAevatarLLMProvider? _defaultProvider;
    private readonly List<(LLMProviderConfig config, IAevatarLLMProvider provider)> _createdProviders = new();

    public bool ConfigureToThrowOnCreate { get; set; }
    public int DelayMilliseconds { get; set; }

    public IReadOnlyList<(LLMProviderConfig config, IAevatarLLMProvider provider)> CreatedProviders =>
        _createdProviders;

    public void RegisterProvider(string name, IAevatarLLMProvider provider)
    {
        _providers[name] = provider;
    }

    public void SetDefaultProvider(IAevatarLLMProvider provider)
    {
        _defaultProvider = provider;
    }

    public async Task<IAevatarLLMProvider> GetProviderAsync(string providerName,
        CancellationToken cancellationToken = default)
    {
        if (DelayMilliseconds > 0)
        {
            await Task.Delay(DelayMilliseconds, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!_providers.ContainsKey(providerName))
        {
            throw new InvalidOperationException($"Provider '{providerName}' not found");
        }

        return _providers[providerName];
    }

    public IAevatarLLMProvider CreateProvider(LLMProviderConfig config, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ConfigureToThrowOnCreate || string.IsNullOrEmpty(config.ProviderType))
        {
            throw new ArgumentException("Invalid configuration");
        }

        var provider = new MockLLMProvider();
        _createdProviders.Add((config, provider));
        return provider;
    }

    public Task<IAevatarLLMProvider> GetDefaultProviderAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_defaultProvider == null)
        {
            throw new InvalidOperationException("No default provider configured");
        }

        return Task.FromResult(_defaultProvider);
    }

    public IReadOnlyList<string> GetAvailableProviderNames()
    {
        return _providers.Keys.ToList();
    }

    public IAevatarLLMProvider GetProvider(string providerName)
    {
        if (!_providers.ContainsKey(providerName))
        {
            throw new InvalidOperationException($"Provider '{providerName}' not found");
        }

        return _providers[providerName];
    }

    public IAevatarLLMProvider GetDefaultProvider()
    {
        if (_defaultProvider == null)
        {
            throw new InvalidOperationException("No default provider configured");
        }

        return _defaultProvider;
    }

    public bool HasProvider(string providerName)
    {
        return _providers.ContainsKey(providerName);
    }
}