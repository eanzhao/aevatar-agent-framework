namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// LLM响应
/// </summary>
public class AevatarLLMResponse
{
    /// <summary>
    /// 生成的内容
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// 函数调用（如果有）
    /// </summary>
    public AevatarFunctionCall? AevatarFunctionCall { get; set; }
    
    /// <summary>
    /// 停止原因
    /// </summary>
    public AevatarStopReason AevatarStopReason { get; set; }
    
    /// <summary>
    /// Token使用情况
    /// </summary>
    public AevatarTokenUsage? Usage { get; set; }
    
    /// <summary>
    /// 模型名称
    /// </summary>
    public string? ModelName { get; set; }
    
    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}