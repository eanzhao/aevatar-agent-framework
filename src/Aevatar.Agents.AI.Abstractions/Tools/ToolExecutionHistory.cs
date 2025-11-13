namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 工具执行历史记录
/// </summary>
public class ToolExecutionHistory
{
    /// <summary>
    /// 执行ID
    /// </summary>
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// 工具名称
    /// </summary>
    public string ToolName { get; set; } = string.Empty;
    
    /// <summary>
    /// 执行参数
    /// </summary>
    public Dictionary<string, object>? Parameters { get; set; }
    
    /// <summary>
    /// 执行结果
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
    /// 开始时间
    /// </summary>
    public DateTimeOffset StartTime { get; set; }
    
    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTimeOffset EndTime { get; set; }
    
    /// <summary>
    /// 执行上下文
    /// </summary>
    public ExecutionContext? Context { get; set; }
    
    /// <summary>
    /// Agent ID
    /// </summary>
    public string? AgentId { get; set; }
    
    /// <summary>
    /// 会话ID
    /// </summary>
    public string? SessionId { get; set; }
    
    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}
