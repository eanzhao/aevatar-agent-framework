namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 工具执行
/// </summary>
public class AevatarToolExecution
{
    /// <summary>
    /// 工具名称
    /// </summary>
    public string ToolName { get; set; } = string.Empty;
    
    /// <summary>
    /// 参数
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
    
    /// <summary>
    /// 执行ID（用于追踪）
    /// </summary>
    public string? ExecutionId { get; set; }
}