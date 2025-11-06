namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// AI记忆管理接口
/// 管理短期记忆（对话）、长期记忆（向量存储）和工作记忆（上下文）
/// </summary>
public interface IAevatarMemory
{
    #region 短期记忆（对话历史）
    
    /// <summary>
    /// 添加对话消息
    /// </summary>
    Task AddMessageAsync(
        AevatarConversationMessage message,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取最近的消息
    /// </summary>
    Task<IReadOnlyList<AevatarConversationMessage>> GetRecentMessagesAsync(
        int count = 10,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取会话的所有消息
    /// </summary>
    Task<IReadOnlyList<AevatarConversationMessage>> GetConversationAsync(
        string? sessionId = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 清除对话历史
    /// </summary>
    Task ClearConversationAsync(
        string? sessionId = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 总结对话
    /// </summary>
    Task<AevatarConversationSummary> SummarizeConversationAsync(
        string? sessionId = null,
        CancellationToken cancellationToken = default);
    
    #endregion
    
    #region 长期记忆（向量存储）
    
    /// <summary>
    /// 存储记忆项
    /// </summary>
    Task<string> StoreMemoryAsync(
        AevatarMemoryItem item,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 批量存储记忆项
    /// </summary>
    Task<IReadOnlyList<string>> StoreMemoriesAsync(
        IEnumerable<AevatarMemoryItem> items,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 回忆（语义搜索）
    /// </summary>
    Task<IReadOnlyList<AevatarRecalledMemory>> RecallAsync(
        string query,
        AevatarRecallOptions? options = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 根据ID获取记忆
    /// </summary>
    Task<AevatarMemoryItem?> GetMemoryAsync(
        string memoryId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 更新记忆
    /// </summary>
    Task<bool> UpdateMemoryAsync(
        string memoryId,
        AevatarMemoryItem item,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 删除记忆
    /// </summary>
    Task<bool> DeleteMemoryAsync(
        string memoryId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 按标签获取记忆
    /// </summary>
    Task<IReadOnlyList<AevatarMemoryItem>> GetMemoriesByTagAsync(
        string tag,
        int maxItems = 50,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 按时间范围获取记忆
    /// </summary>
    Task<IReadOnlyList<AevatarMemoryItem>> GetMemoriesByAevatarTimeRangeAsync(
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        int maxItems = 100,
        CancellationToken cancellationToken = default);
    
    #endregion
    
    #region 工作记忆（上下文）
    
    /// <summary>
    /// 更新上下文
    /// </summary>
    Task UpdateContextAsync(
        string key,
        object value,
        AevatarContextScope scope = AevatarContextScope.Session,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 批量更新上下文
    /// </summary>
    Task UpdateContextBatchAsync(
        Dictionary<string, object> items,
        AevatarContextScope scope = AevatarContextScope.Session,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取上下文值
    /// </summary>
    Task<T?> GetContextAsync<T>(
        string key,
        AevatarContextScope scope = AevatarContextScope.Session,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取所有上下文
    /// </summary>
    Task<Dictionary<string, object>> GetAllContextAsync(
        AevatarContextScope scope = AevatarContextScope.Session,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 删除上下文
    /// </summary>
    Task<bool> RemoveContextAsync(
        string key,
        AevatarContextScope scope = AevatarContextScope.Session,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 清除上下文
    /// </summary>
    Task ClearContextAsync(
        AevatarContextScope scope = AevatarContextScope.Session,
        CancellationToken cancellationToken = default);
    
    #endregion
    
    #region 记忆管理
    
    /// <summary>
    /// 获取记忆统计
    /// </summary>
    Task<AevatarMemoryStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 整理记忆（去重、合并相似项）
    /// </summary>
    Task<AevatarConsolidationResult> ConsolidateMemoriesAsync(
        AevatarConsolidationOptions? options = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 导出记忆
    /// </summary>
    Task<string> ExportMemoriesAsync(
        AevatarExportFormat format = AevatarExportFormat.Json,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 导入记忆
    /// </summary>
    Task<AevatarImportResult> ImportMemoriesAsync(
        string content,
        AevatarExportFormat format = AevatarExportFormat.Json,
        bool overwrite = false,
        CancellationToken cancellationToken = default);
    
    #endregion
}

/// <summary>
/// 对话消息
/// </summary>
public class AevatarConversationMessage
{
    /// <summary>
    /// 消息ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// 会话ID
    /// </summary>
    public string? SessionId { get; set; }
    
    /// <summary>
    /// 角色
    /// </summary>
    public AevatarChatRole Role { get; set; }
    
    /// <summary>
    /// 内容
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// 函数名称（当Role为Function时）
    /// </summary>
    public string? FunctionName { get; set; }
    
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    
    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

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

/// <summary>
/// 时间范围
/// </summary>
public class AevatarTimeRange
{
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
}

/// <summary>
/// 上下文范围
/// </summary>
public enum AevatarContextScope
{
    /// <summary>
    /// 会话级别
    /// </summary>
    Session,
    
    /// <summary>
    /// Agent级别
    /// </summary>
    Agent,
    
    /// <summary>
    /// 全局级别
    /// </summary>
    Global,
    
    /// <summary>
    /// 临时（当前请求）
    /// </summary>
    Temporary
}

/// <summary>
/// 对话总结
/// </summary>
public class AevatarConversationSummary
{
    /// <summary>
    /// 总结内容
    /// </summary>
    public string Summary { get; set; } = string.Empty;
    
    /// <summary>
    /// 关键点
    /// </summary>
    public IList<string> KeyPoints { get; set; } = new List<string>();
    
    /// <summary>
    /// 主题
    /// </summary>
    public IList<string> Topics { get; set; } = new List<string>();
    
    /// <summary>
    /// 情绪分析
    /// </summary>
    public AevatarSentimentAnalysis? Sentiment { get; set; }
    
    /// <summary>
    /// 消息数量
    /// </summary>
    public int MessageCount { get; set; }
    
    /// <summary>
    /// 时间范围
    /// </summary>
    public AevatarTimeRange? AevatarTimeRange { get; set; }
}

/// <summary>
/// 情绪分析
/// </summary>
public class AevatarSentimentAnalysis
{
    /// <summary>
    /// 整体情绪（positive/negative/neutral）
    /// </summary>
    public string Overall { get; set; } = "neutral";
    
    /// <summary>
    /// 积极度（0-1）
    /// </summary>
    public double Positivity { get; set; }
    
    /// <summary>
    /// 情绪标签
    /// </summary>
    public IList<string> Emotions { get; set; } = new List<string>();
}

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

/// <summary>
/// 导出格式
/// </summary>
public enum AevatarExportFormat
{
    Json,
    Csv,
    Markdown,
    Binary
}

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
