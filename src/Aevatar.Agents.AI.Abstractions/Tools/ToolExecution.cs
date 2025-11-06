namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 工具执行请求
/// </summary>
public class ToolExecution
{
    /// <summary>
    /// 工具名称
    /// </summary>
    public string ToolName { get; set; } = string.Empty;
    
    /// <summary>
    /// 执行参数
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
    
    /// <summary>
    /// 执行ID（可选）
    /// </summary>
    public string? ExecutionId { get; set; }
    
    /// <summary>
    /// 超时时间（可选）
    /// </summary>
    public TimeSpan? Timeout { get; set; }
    
    /// <summary>
    /// 元数据（可选）
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}
