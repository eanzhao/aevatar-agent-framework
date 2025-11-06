namespace Aevatar.Agents.AI.Abstractions;

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