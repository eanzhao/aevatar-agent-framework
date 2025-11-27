namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// Default values for AI configuration.
/// Centralizes magic numbers and strings to avoid scattered hardcoding.
/// </summary>
public static class AevatarAIDefaults
{
    // ============ Model Configuration ============

    /// <summary>
    /// Default AI model for chat completion.
    /// Uses GPT-5.1 as a cost-effective default with good performance.
    /// </summary>
    public const string DefaultModel = "gpt-5.1";

    /// <summary>
    /// Default temperature for response generation.
    /// 0.7 provides balanced creativity and consistency.
    /// </summary>
    public const float DefaultTemperature = 0.7f;

    /// <summary>
    /// Default maximum output tokens for most providers.
    /// 2000 tokens is sufficient for most conversational responses.
    /// </summary>
    public const int DefaultMaxOutputTokens = 2000;

    /// <summary>
    /// Default maximum tokens for providers that support larger contexts.
    /// 4096 allows for longer responses when needed.
    /// </summary>
    public const int DefaultMaxTokensExtended = 4096;
}
