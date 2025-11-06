namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// AI记忆管理接口
/// 管理短期记忆（对话）、长期记忆（向量存储）和工作记忆（上下文）
/// </summary>
// ReSharper disable once InconsistentNaming
public interface IAevatarAIMemory
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