namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 嵌入响应
/// </summary>
public class AevatarEmbeddingResponse
{
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public int Index { get; set; }
    public string? ModelName { get; set; }
    public AevatarTokenUsage? Usage { get; set; }
}