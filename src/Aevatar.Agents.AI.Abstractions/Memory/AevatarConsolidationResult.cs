namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 整理结果
/// </summary>
public class AevatarConsolidationResult
{
    /// <summary>
    /// 合并的记忆数
    /// </summary>
    public int MergedCount { get; set; }
    
    /// <summary>
    /// 删除的记忆数
    /// </summary>
    public int RemovedCount { get; set; }
    
    /// <summary>
    /// 更新的记忆数
    /// </summary>
    public int UpdatedCount { get; set; }
    
    /// <summary>
    /// 处理时间
    /// </summary>
    public TimeSpan ProcessingTime { get; set; }
}