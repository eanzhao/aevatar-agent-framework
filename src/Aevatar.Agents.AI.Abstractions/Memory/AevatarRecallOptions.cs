namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 回忆选项
/// </summary>
public class AevatarRecallOptions
{
    /// <summary>
    /// 返回的最大项数
    /// </summary>
    public int TopK { get; set; } = 5;
    
    /// <summary>
    /// 相关性阈值（0-1）
    /// </summary>
    public double Threshold { get; set; } = 0.7;
    
    /// <summary>
    /// 过滤标签
    /// </summary>
    public IList<string>? Tags { get; set; }
    
    /// <summary>
    /// 过滤类别
    /// </summary>
    public string? Category { get; set; }
    
    /// <summary>
    /// 时间范围
    /// </summary>
    public AevatarTimeRange? AevatarTimeRange { get; set; }
    
    /// <summary>
    /// 是否包含过期项
    /// </summary>
    public bool IncludeExpired { get; set; }
}