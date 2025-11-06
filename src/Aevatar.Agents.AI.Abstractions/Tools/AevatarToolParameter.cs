namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 工具参数
/// </summary>
public class AevatarToolParameter
{
    /// <summary>
    /// 参数类型
    /// </summary>
    public string Type { get; set; } = "string";
    
    /// <summary>
    /// 参数描述
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// 是否必需
    /// </summary>
    public bool Required { get; set; }
    
    /// <summary>
    /// 默认值
    /// </summary>
    public object? DefaultValue { get; set; }
    
    /// <summary>
    /// 枚举值
    /// </summary>
    public IList<object>? Enum { get; set; }
    
    /// <summary>
    /// 最小值（数字类型）
    /// </summary>
    public double? Minimum { get; set; }
    
    /// <summary>
    /// 最大值（数字类型）
    /// </summary>
    public double? Maximum { get; set; }
    
    /// <summary>
    /// 最小长度（字符串/数组）
    /// </summary>
    public int? MinLength { get; set; }
    
    /// <summary>
    /// 最大长度（字符串/数组）
    /// </summary>
    public int? MaxLength { get; set; }
    
    /// <summary>
    /// 正则表达式模式
    /// </summary>
    public string? Pattern { get; set; }
    
    /// <summary>
    /// 格式（如email、uri、date-time等）
    /// </summary>
    public string? Format { get; set; }
}