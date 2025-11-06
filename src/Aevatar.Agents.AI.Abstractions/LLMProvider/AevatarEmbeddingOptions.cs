namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 嵌入选项
/// </summary>
public class AevatarEmbeddingOptions
{
    public string? ModelId { get; set; }
    public int? Dimensions { get; set; }
}