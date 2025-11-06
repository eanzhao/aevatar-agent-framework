namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 工具执行请求
/// </summary>
public class ToolExecutionRequest
{
    /// <summary>
    /// 工具名称
    /// </summary>
    public string ToolName { get; set; } = string.Empty;
    
    /// <summary>
    /// 工具参数
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
    
    /// <summary>
    /// 请求元数据
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}