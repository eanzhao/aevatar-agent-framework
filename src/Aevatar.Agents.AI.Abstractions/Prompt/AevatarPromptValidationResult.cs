namespace Aevatar.Agents.AI.Abstractions;

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