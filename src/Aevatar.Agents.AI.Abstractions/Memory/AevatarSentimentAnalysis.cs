namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 情绪分析
/// </summary>
public class AevatarSentimentAnalysis
{
    /// <summary>
    /// 整体情绪（positive/negative/neutral）
    /// </summary>
    public string Overall { get; set; } = "neutral";
    
    /// <summary>
    /// 积极度（0-1）
    /// </summary>
    public double Positivity { get; set; }
    
    /// <summary>
    /// 情绪标签
    /// </summary>
    public IList<string> Emotions { get; set; } = new List<string>();
}