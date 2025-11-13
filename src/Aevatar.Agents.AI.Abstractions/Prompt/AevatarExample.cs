namespace Aevatar.Agents.AI.Abstractions;

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