using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    Task<IAevatarLLMProvider> GetProviderAsync(string providerName, CancellationToken cancellationToken = default);
    Task<IAevatarLLMProvider> GetDefaultProviderAsync(CancellationToken cancellationToken = default);
}