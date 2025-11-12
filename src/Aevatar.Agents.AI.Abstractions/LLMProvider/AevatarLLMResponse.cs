namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// LLM stream chunk
/// LLM流块
/// </summary>
public class AevatarLLMStreamChunk
{
    /// <summary>
    /// Content chunk
    /// 内容块
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether this is the final chunk
    /// 是否为最终块
    /// </summary>
    public bool IsComplete { get; set; }
    
    /// <summary>
    /// Function call if present
    /// 函数调用（如果存在）
    /// </summary>
    public AevatarFunctionCall? FunctionCall { get; set; }
}

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