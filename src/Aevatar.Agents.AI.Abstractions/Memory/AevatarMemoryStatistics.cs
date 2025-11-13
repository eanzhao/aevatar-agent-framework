namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 记忆统计
/// </summary>
public class AevatarMemoryStatistics
{
    /// <summary>
    /// 总记忆数
    /// </summary>
    public long TotalMemories { get; set; }
    
    /// <summary>
    /// 总对话消息数
    /// </summary>
    public long TotalMessages { get; set; }
    
    /// <summary>
    /// 活跃会话数
    /// </summary>
    public int ActiveSessions { get; set; }
    
    /// <summary>
    /// 存储大小（字节）
    /// </summary>
    public long StorageSizeBytes { get; set; }
    
    /// <summary>
    /// 按类别统计
    /// </summary>
    public Dictionary<string, long> ByCategory { get; set; } = new();
    
    /// <summary>
    /// 按标签统计
    /// </summary>
    public Dictionary<string, long> ByTag { get; set; } = new();
    
    /// <summary>
    /// 最旧记忆时间
    /// </summary>
    public DateTimeOffset? OldestMemory { get; set; }
    
    /// <summary>
    /// 最新记忆时间
    /// </summary>
    public DateTimeOffset? NewestMemory { get; set; }
}