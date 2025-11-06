namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 提示词模板
/// </summary>
public class AevatarPromptTemplate
{
    /// <summary>
    /// 模板ID
    /// </summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// 模板名称
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 模板描述
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// 模板内容（支持占位符）
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// 系统提示词
    /// </summary>
    public string? SystemPrompt { get; set; }
    
    /// <summary>
    /// 参数定义
    /// </summary>
    public Dictionary<string, AevatarTemplateParameter> Parameters { get; set; } = new();
    
    /// <summary>
    /// 示例
    /// </summary>
    public IList<AevatarExample>? AevatarExamples { get; set; }
    
    /// <summary>
    /// 输出格式
    /// </summary>
    public AevatarOutputFormat? AevatarOutputFormat { get; set; }
    
    /// <summary>
    /// 标签
    /// </summary>
    public IList<string>? Tags { get; set; }
    
    /// <summary>
    /// 版本
    /// </summary>
    public string Version { get; set; } = "1.0.0";
    
    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    
    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    
    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}