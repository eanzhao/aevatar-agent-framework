namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 工具执行结果
/// </summary>
public class ToolExecutionResult
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
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// 执行结果
    /// </summary>
    public object? Result { get; set; }
    
    /// <summary>
    /// 错误信息（如果失败）
    /// </summary>
    public string? Error { get; set; }
    
    /// <summary>
    /// 执行时间
    /// </summary>
    public TimeSpan ExecutionTime { get; set; }
    
    /// <summary>
    /// 执行元数据
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}