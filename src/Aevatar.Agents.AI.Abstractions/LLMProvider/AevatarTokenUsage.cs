namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// Token使用情况
/// </summary>
public class AevatarTokenUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}