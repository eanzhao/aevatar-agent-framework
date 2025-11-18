namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// LLM生成停止原因
/// </summary>
public enum AevatarStopReason
{
    /// <summary>
    /// 正常完成（模型自然结束）
    /// </summary>
    Complete,
    
    /// <summary>
    /// 达到最大token限制
    /// </summary>
    MaxTokens,
    
    /// <summary>
    /// 遇到停止序列
    /// </summary>
    StopSequence,
    
    /// <summary>
    /// 需要调用函数/工具
    /// </summary>
    AevatarFunctionCall,
    
    /// <summary>
    /// 内容被安全过滤器拦截
    /// </summary>
    ContentFilter,
    
    /// <summary>
    /// 用户主动中断
    /// </summary>
    UserInterruption,
    
    /// <summary>
    /// 请求超时
    /// </summary>
    Timeout,
    
    /// <summary>
    /// 达到API速率限制
    /// </summary>
    RateLimitReached,
    
    /// <summary>
    /// 上下文长度超限
    /// </summary>
    ContextLengthExceeded,
    
    /// <summary>
    /// 发生错误
    /// </summary>
    Error
}