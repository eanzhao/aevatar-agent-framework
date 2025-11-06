namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 工具执行历史
/// </summary>
public class AevatarAevatarToolExecutionHistory
{
    /// <summary>
    /// 执行ID
    /// </summary>
    public string ExecutionId { get; set; } = string.Empty;
    
    /// <summary>
    /// 工具名称
    /// </summary>
    public string ToolName { get; set; } = string.Empty;
    
    /// <summary>
    /// 参数
    /// </summary>
    public Dictionary<string, object>? Parameters { get; set; }
    
    /// <summary>
    /// 结果
    /// </summary>
    public object? Result { get; set; }
    
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// 错误信息
    /// </summary>
    public string? Error { get; set; }
    
    /// <summary>
    /// 执行时间
    /// </summary>
    public TimeSpan ExecutionTime { get; set; }
    
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }
    
    /// <summary>
    /// 执行上下文
    /// </summary>
    public AevatarExecutionContext? Context { get; set; }
}