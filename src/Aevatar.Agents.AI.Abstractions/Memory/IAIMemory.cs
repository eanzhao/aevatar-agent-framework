namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// AI记忆管理接口 - 简化版
/// 管理对话历史和上下文记忆
/// </summary>
// ReSharper disable once InconsistentNaming  
public interface IAevatarAIMemory
{
    /// <summary>
    /// 添加对话记录（核心方法）
    /// </summary>
    Task AddMessageAsync(
        string role,
        string content,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取对话历史（核心方法）
    /// </summary>
    Task<IReadOnlyList<AevatarConversationEntry>> GetConversationHistoryAsync(
        int? limit = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 清空历史（可选实现）
    /// </summary>
    Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        // 默认实现：空操作
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// 搜索相关记忆（可选实现，用于RAG等）
    /// </summary>
    Task<IReadOnlyList<string>> SearchAsync(
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        // 默认实现：返回空列表
        return Task.FromResult<IReadOnlyList<string>>(new List<string>());
    }
}