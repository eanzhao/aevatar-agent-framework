namespace Aevatar.Agents.AI.Abstractions;

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