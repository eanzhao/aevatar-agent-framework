namespace Aevatar.Agents.AI.Abstractions;

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