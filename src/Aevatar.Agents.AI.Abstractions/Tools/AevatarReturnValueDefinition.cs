namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 返回值定义
/// </summary>
public class AevatarReturnValueDefinition
{
    /// <summary>
    /// 返回值类型
    /// </summary>
    public string Type { get; set; } = "object";
    
    /// <summary>
    /// 返回值描述
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Schema定义（JSON Schema）
    /// </summary>
    public object? Schema { get; set; }
}