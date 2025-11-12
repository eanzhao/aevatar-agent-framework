using System.Collections.Generic;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Messages;

namespace Aevatar.Agents.AI.Core.Models;

/// <summary>
/// Represents a response from an AI agent.
/// 表示来自AI代理的响应
/// </summary>
public class ChatResponse
{
    /// <summary>
    /// The response content.
    /// 响应内容
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// Token usage information.
    /// 令牌使用信息
    /// </summary>
    public AevatarTokenUsage? Usage { get; set; }
    
    /// <summary>
    /// Processing steps taken (if strategy agent).
    /// 处理步骤（如果是策略代理）
    /// </summary>
    public List<ProcessingStep> ProcessingSteps { get; set; } = new();
    
    /// <summary>
    /// Request ID this response is for.
    /// 此响应对应的请求ID
    /// </summary>
    public string RequestId { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether a tool was called.
    /// 是否调用了工具
    /// </summary>
    public bool ToolCalled { get; set; }
    
    /// <summary>
    /// Tool call details if applicable.
    /// 工具调用详情（如果适用）
    /// </summary>
    public ToolCallInfo? ToolCall { get; set; }
}

/// <summary>
/// Information about a tool call.
/// 工具调用信息
/// </summary>
public class ToolCallInfo
{
    /// <summary>
    /// Name of the tool called.
    /// 被调用的工具名称
    /// </summary>
    public string ToolName { get; set; } = string.Empty;
    
    /// <summary>
    /// Arguments passed to the tool.
    /// 传递给工具的参数
    /// </summary>
    public Dictionary<string, object> Arguments { get; set; } = new();
    
    /// <summary>
    /// Result from the tool.
    /// 工具的结果
    /// </summary>
    public object? Result { get; set; }
}

