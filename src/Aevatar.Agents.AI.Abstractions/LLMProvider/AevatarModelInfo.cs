namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 模型信息
/// </summary>
public class AevatarModelInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public int MaxTokens { get; set; }
    public int ContextWindow { get; set; }
    public bool SupportsStreaming { get; set; }
    public bool SupportsFunctions { get; set; }
    public bool SupportsEmbeddings { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}