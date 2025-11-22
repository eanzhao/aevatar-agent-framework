using System.Runtime.CompilerServices;

namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// Base class for LLM providers with common mapping functionality.
/// </summary>
public abstract class AevatarLLMProviderBase : IAevatarLLMProvider
{
    /// <summary>
    /// Generate a response from the LLM.
    /// </summary>
    public abstract Task<AevatarLLMResponse> GenerateAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a streaming response from the LLM.
    /// </summary>
    public abstract IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(
        AevatarLLMRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    /// <summary>
    /// Map Aevatar function parameters to provider-specific format.
    /// Default implementation creates a JSON schema object.
    /// </summary>
    protected virtual object MapFunctionParameters(Dictionary<string, AevatarParameterDefinition> parameters)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var param in parameters)
        {
            var paramDef = new Dictionary<string, object>
            {
                ["type"] = param.Value.Type,
                ["description"] = param.Value.Description ?? ""
            };

            if (param.Value.Enum != null && param.Value.Enum.Count > 0)
            {
                paramDef["enum"] = param.Value.Enum;
            }

            properties[param.Key] = paramDef;

            if (param.Value.Required)
            {
                required.Add(param.Key);
            }
        }

        return new
        {
            type = "object",
            properties = properties,
            required = required
        };
    }

    /// <summary>
    /// Create AevatarTokenUsage from provider-specific token counts.
    /// </summary>
    protected virtual AevatarTokenUsage? CreateTokenUsage(int? promptTokens, int? completionTokens, int? totalTokens)
    {
        if (promptTokens == null && completionTokens == null && totalTokens == null)
            return null;

        return new AevatarTokenUsage
        {
            PromptTokens = promptTokens ?? 0,
            CompletionTokens = completionTokens ?? 0,
            TotalTokens = totalTokens ?? (promptTokens ?? 0) + (completionTokens ?? 0)
        };
    }
}
