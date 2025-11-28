using Aevatar.Agents.AI.Abstractions.Configuration;
using Microsoft.Extensions.AI;

namespace Aevatar.Agents.AI.Core.Embeddings;

/// <summary>
/// Factory responsible for creating Microsoft.Extensions.AI embedding generators for an agent.
/// </summary>
public interface IAIAgentEmbeddingFactory
{
    /// <summary>
    /// Create an embedding generator according to the provider configuration.
    /// </summary>
    Task<IEmbeddingGenerator<string, Embedding<float>>?> CreateAsync(
        LLMProviderConfig providerConfig,
        CancellationToken cancellationToken = default);
}

