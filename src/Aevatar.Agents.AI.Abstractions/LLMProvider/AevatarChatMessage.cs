namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 聊天消息
/// </summary>
public class AevatarChatMessage
{
    /// <summary>
    /// 角色（system/user/assistant/function）
    /// </summary>
    public AevatarChatRole Role { get; set; }
    
    /// <summary>
    /// 消息内容
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
}