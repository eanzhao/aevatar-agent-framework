namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 函数参数定义
/// </summary>
public class AevatarFunctionParameter
{
    /// <summary>
    /// 参数名称
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 参数类型
    /// </summary>
    public string Type { get; set; } = "string";
    
    /// <summary>
    /// 参数描述
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// 是否必需
    /// </summary>
    public bool IsRequired { get; set; }
    
    /// <summary>
    /// 默认值
    /// </summary>
    public object? DefaultValue { get; set; }
}
