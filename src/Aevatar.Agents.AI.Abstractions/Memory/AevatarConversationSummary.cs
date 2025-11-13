namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 对话总结
/// </summary>
public class AevatarConversationSummary
{
    /// <summary>
    /// 总结内容
    /// </summary>
    public string Summary { get; set; } = string.Empty;
    
    /// <summary>
    /// 关键点
    /// </summary>
    public IList<string> KeyPoints { get; set; } = new List<string>();
    
    /// <summary>
    /// 主题
    /// </summary>
    public IList<string> Topics { get; set; } = new List<string>();
    
    /// <summary>
    /// 情绪分析
    /// </summary>
    public AevatarSentimentAnalysis? Sentiment { get; set; }
    
    /// <summary>
    /// 消息数量
    /// </summary>
    public int MessageCount { get; set; }
    
    /// <summary>
    /// 时间范围
    /// </summary>
    public AevatarTimeRange? AevatarTimeRange { get; set; }
}