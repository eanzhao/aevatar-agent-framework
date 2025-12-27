namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// LLM provider interface - simplified version
/// Supports multiple framework implementations (OpenAI, Azure, local models, etc.)
/// </summary>
public interface IAevatarLLMProvider
{
    /// <summary>
    /// Generate text response (core method)
    /// </summary>
    Task<AevatarLLMResponse> GenerateAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream generation (optional implementation)
    /// </summary>
    IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get model information (optional implementation)
    /// </summary>
    Task<AevatarModelInfo> GetModelInfoAsync(CancellationToken cancellationToken = default)
    {
        // Default implementation
        return Task.FromResult(new AevatarModelInfo
        {
            Name = "unknown",
            MaxTokens = 4096,
            SupportsStreaming = false,
            SupportsFunctions = false
        });
    }
}
