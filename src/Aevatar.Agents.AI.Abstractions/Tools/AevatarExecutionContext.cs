namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 执行上下文
/// </summary>
public class AevatarExecutionContext
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public string? UserId { get; set; }
    
    /// <summary>
    /// 会话ID
    /// </summary>
    public string? SessionId { get; set; }
    
    /// <summary>
    /// Agent ID
    /// </summary>
    public Guid? AgentId { get; set; }
    
    /// <summary>
    /// 追踪ID
    /// </summary>
    public string? TraceId { get; set; }
    
    /// <summary>
    /// 附加数据
    /// </summary>
    public Dictionary<string, object>? AdditionalData { get; set; }
}