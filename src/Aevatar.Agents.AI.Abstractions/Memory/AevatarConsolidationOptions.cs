namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 整理选项
/// </summary>
public class AevatarConsolidationOptions
{
    /// <summary>
    /// 相似度阈值（用于合并）
    /// </summary>
    public double SimilarityThreshold { get; set; } = 0.9;
    
    /// <summary>
    /// 保留最重要的N个记忆
    /// </summary>
    public int? KeepTopN { get; set; }
    
    /// <summary>
    /// 删除过期记忆
    /// </summary>
    public bool RemoveExpired { get; set; } = true;
    
    /// <summary>
    /// 删除低重要性记忆的阈值
    /// </summary>
    public double? ImportanceThreshold { get; set; }
}