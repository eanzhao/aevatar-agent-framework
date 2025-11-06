namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 提示词管理器接口
/// 负责管理、构建和优化提示词模板
/// </summary>
public interface IAevatarPromptManager
{
    /// <summary>
    /// 获取提示词模板
    /// </summary>
    Task<AevatarPromptTemplate?> GetTemplateAsync(
        string templateId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 注册提示词模板
    /// </summary>
    Task RegisterTemplateAsync(
        string templateId,
        AevatarPromptTemplate template,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 更新提示词模板
    /// </summary>
    Task UpdateTemplateAsync(
        string templateId,
        AevatarPromptTemplate template,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 删除提示词模板
    /// </summary>
    Task<bool> DeleteTemplateAsync(
        string templateId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 构建提示词
    /// </summary>
    Task<string> BuildPromptAsync(
        string templateId,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 构建对话提示词
    /// </summary>
    Task<IList<AevatarChatMessage>> BuildChatPromptAsync(
        string templateId,
        Dictionary<string, object> parameters,
        IList<AevatarChatMessage>? history = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 构建链式思考提示词
    /// </summary>
    Task<string> BuildChainOfThoughtPromptAsync(
        IEnumerable<AevatarThoughtStep> thoughts,
        string? finalQuestion = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 构建少样本学习提示词
    /// </summary>
    Task<string> BuildFewShotPromptAsync(
        string task,
        IEnumerable<AevatarExample> examples,
        string query,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 优化提示词（使用AI优化）
    /// </summary>
    Task<string> OptimizePromptAsync(
        string prompt,
        AevatarOptimizationGoal goal,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 验证提示词
    /// </summary>
    Task<AevatarPromptValidationResult> ValidatePromptAsync(
        string prompt,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取所有模板ID
    /// </summary>
    Task<IReadOnlyList<string>> ListTemplateIdsAsync(
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 导入模板集合
    /// </summary>
    Task ImportTemplatesAsync(
        string jsonContent,
        bool overwrite = false,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 导出模板集合
    /// </summary>
    Task<string> ExportTemplatesAsync(
        IEnumerable<string>? templateIds = null,
        CancellationToken cancellationToken = default);
}

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

/// <summary>
/// 模板参数定义
/// </summary>
public class AevatarTemplateParameter
{
    /// <summary>
    /// 参数名称
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 参数描述
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// 参数类型
    /// </summary>
    public string Type { get; set; } = "string";
    
    /// <summary>
    /// 是否必需
    /// </summary>
    public bool Required { get; set; }
    
    /// <summary>
    /// 默认值
    /// </summary>
    public object? DefaultValue { get; set; }
    
    /// <summary>
    /// 验证规则
    /// </summary>
    public string? ValidationRule { get; set; }
    
    /// <summary>
    /// 可选值列表
    /// </summary>
    public IList<string>? AllowedValues { get; set; }
}

/// <summary>
/// 思考步骤（用于链式思考）
/// </summary>
public class AevatarThoughtStep
{
    /// <summary>
    /// 步骤编号
    /// </summary>
    public int StepNumber { get; set; }
    
    /// <summary>
    /// 思考内容
    /// </summary>
    public string Thought { get; set; } = string.Empty;
    
    /// <summary>
    /// 推理过程
    /// </summary>
    public string? Reasoning { get; set; }
    
    /// <summary>
    /// 结论
    /// </summary>
    public string? Conclusion { get; set; }
    
    /// <summary>
    /// 置信度（0-1）
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// 示例（用于少样本学习）
/// </summary>
public class AevatarExample
{
    /// <summary>
    /// 输入
    /// </summary>
    public string Input { get; set; } = string.Empty;
    
    /// <summary>
    /// 输出
    /// </summary>
    public string Output { get; set; } = string.Empty;
    
    /// <summary>
    /// 解释（可选）
    /// </summary>
    public string? Explanation { get; set; }
    
    /// <summary>
    /// 标签
    /// </summary>
    public IList<string>? Tags { get; set; }
}

/// <summary>
/// 输出格式定义
/// </summary>
public class AevatarOutputFormat
{
    /// <summary>
    /// 格式类型（text/json/xml/markdown）
    /// </summary>
    public string Type { get; set; } = "text";
    
    /// <summary>
    /// JSON Schema（当Type为json时）
    /// </summary>
    public object? Schema { get; set; }
    
    /// <summary>
    /// 格式说明
    /// </summary>
    public string? Instructions { get; set; }
    
    /// <summary>
    /// 示例输出
    /// </summary>
    public string? AevatarExample { get; set; }
}

/// <summary>
/// 优化目标
/// </summary>
public enum AevatarOptimizationGoal
{
    /// <summary>
    /// 清晰度
    /// </summary>
    Clarity,
    
    /// <summary>
    /// 简洁性
    /// </summary>
    Brevity,
    
    /// <summary>
    /// 准确性
    /// </summary>
    Accuracy,
    
    /// <summary>
    /// 创造性
    /// </summary>
    Creativity,
    
    /// <summary>
    /// 专业性
    /// </summary>
    Professionalism,
    
    /// <summary>
    /// 友好性
    /// </summary>
    Friendliness,
    
    /// <summary>
    /// 性能（减少Token）
    /// </summary>
    Performance
}

/// <summary>
/// 提示词验证结果
/// </summary>
public class AevatarPromptValidationResult
{
    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsValid { get; set; }
    
    /// <summary>
    /// 错误列表
    /// </summary>
    public IList<string> Errors { get; set; } = new List<string>();
    
    /// <summary>
    /// 警告列表
    /// </summary>
    public IList<string> Warnings { get; set; } = new List<string>();
    
    /// <summary>
    /// 建议
    /// </summary>
    public IList<string> Suggestions { get; set; } = new List<string>();
    
    /// <summary>
    /// 预估Token数
    /// </summary>
    public int EstimatedTokens { get; set; }
    
    /// <summary>
    /// 复杂度评分（1-10）
    /// </summary>
    public int ComplexityScore { get; set; }
}
