namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 导入结果
/// </summary>
public class AevatarImportResult
{
    /// <summary>
    /// 导入的记忆数
    /// </summary>
    public int ImportedCount { get; set; }
    
    /// <summary>
    /// 跳过的记忆数
    /// </summary>
    public int SkippedCount { get; set; }
    
    /// <summary>
    /// 错误数
    /// </summary>
    public int ErrorCount { get; set; }
    
    /// <summary>
    /// 错误详情
    /// </summary>
    public IList<string> Errors { get; set; } = new List<string>();
}