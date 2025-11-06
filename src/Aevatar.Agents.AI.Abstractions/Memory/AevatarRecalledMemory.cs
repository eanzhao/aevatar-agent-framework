namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 回忆的记忆
/// </summary>
public class AevatarRecalledMemory
{
    /// <summary>
    /// 记忆项
    /// </summary>
    public AevatarMemoryItem Item { get; set; } = new();
    
    /// <summary>
    /// 相关性分数（0-1）
    /// </summary>
    public double RelevanceScore { get; set; }
    
    /// <summary>
    /// 距离（向量距离）
    /// </summary>
    public double Distance { get; set; }
    
    /// <summary>
    /// 解释（为什么相关）
    /// </summary>
    public string? Explanation { get; set; }
}