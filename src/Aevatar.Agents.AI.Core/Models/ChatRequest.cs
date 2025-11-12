using System.Collections.Generic;

namespace Aevatar.Agents.AI.Core.Models;

/// <summary>
/// Represents a chat request to an AI agent.
/// 表示对AI代理的聊天请求
/// </summary>
public class ChatRequest
{
    /// <summary>
    /// The user's message.
    /// 用户消息
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Additional context for the request.
    /// 请求的附加上下文
    /// </summary>
    public Dictionary<string, object> Context { get; set; } = new();
    
    /// <summary>
    /// Request ID for tracking.
    /// 用于跟踪的请求ID
    /// </summary>
    public string RequestId { get; set; } = System.Guid.NewGuid().ToString();
    
    /// <summary>
    /// Maximum tokens for response.
    /// 响应的最大令牌数
    /// </summary>
    public int? MaxTokens { get; set; }
    
    /// <summary>
    /// Temperature for generation (0-1).
    /// 生成的温度参数（0-1）
    /// </summary>
    public double? Temperature { get; set; }
}

