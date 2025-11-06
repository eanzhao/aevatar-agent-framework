namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 提示词管理器接口 - 简化版
/// 管理系统提示词和提示词模板
/// </summary>
public interface IAevatarPromptManager  
{
    /// <summary>
    /// 获取系统提示词（核心方法）
    /// </summary>
    Task<string> GetSystemPromptAsync(
        string? key = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 格式化提示词模板（核心方法）
    /// </summary>
    Task<string> FormatPromptAsync(
        string template,
        Dictionary<string, object>? variables = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 构建对话提示词（可选实现）
    /// </summary>
    Task<IList<AevatarChatMessage>> BuildChatPromptAsync(
        string systemPrompt,
        IList<AevatarChatMessage>? history = null,
        CancellationToken cancellationToken = default)
    {
        // 默认实现
        var messages = new List<AevatarChatMessage>();
        messages.Add(new AevatarChatMessage { Role = AevatarChatRole.System, Content = systemPrompt });
        if (history != null)
        {
            messages.AddRange(history);
        }
        return Task.FromResult<IList<AevatarChatMessage>>(messages);
    }
}