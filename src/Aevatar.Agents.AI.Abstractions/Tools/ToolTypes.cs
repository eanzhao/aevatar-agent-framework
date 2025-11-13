namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 验证结果
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsValid { get; set; }
    
    /// <summary>
    /// 错误列表
    /// </summary>
    public List<string> Errors { get; set; } = new();
    
    /// <summary>
    /// 警告列表
    /// </summary>
    public List<string> Warnings { get; set; } = new();
    
    /// <summary>
    /// 验证详情
    /// </summary>
    public Dictionary<string, object>? Details { get; set; }
}

/// <summary>
/// 描述格式
/// </summary>
public enum DescriptionFormat
{
    /// <summary>
    /// JSON格式
    /// </summary>
    Json,
    
    /// <summary>
    /// Markdown格式
    /// </summary>
    Markdown,
    
    /// <summary>
    /// 纯文本格式
    /// </summary>
    PlainText,
    
    /// <summary>
    /// YAML格式
    /// </summary>
    Yaml
}

// FunctionDefinition - 使用 AevatarFunctionDefinition 替代，已在 LLMProvider 目录中定义
