namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 记忆项
/// </summary>
public class AevatarMemoryItem
{
    /// <summary>
    /// 记忆ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// 内容
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// 嵌入向量
    /// </summary>
    public float[]? Embedding { get; set; }
    
    /// <summary>
    /// 标签
    /// </summary>
    public IList<string> Tags { get; set; } = new List<string>();
    
    /// <summary>
    /// 类别
    /// </summary>
    public string? Category { get; set; }
    
    /// <summary>
    /// 重要性（0-1）
    /// </summary>
    public double Importance { get; set; } = 0.5;
    
    /// <summary>
    /// 来源
    /// </summary>
    public string? Source { get; set; }
    
    /// <summary>
    /// 相关实体
    /// </summary>
    public IList<string>? RelatedEntities { get; set; }
    
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    
    /// <summary>
    /// 最后访问时间
    /// </summary>
    public DateTimeOffset? LastAccessedAt { get; set; }
    
    /// <summary>
    /// 访问次数
    /// </summary>
    public int AccessCount { get; set; }
    
    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    
    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}