using System.Collections.Generic;

namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// AI处理上下文
/// </summary>
public class AevatarAIContext
{
    /// <summary>
    /// Agent标识
    /// </summary>
    public string AgentId { get; set; } = string.Empty;
    
    /// <summary>
    /// 问题或输入
    /// </summary>
    public string? Question { get; set; }
    
    /// <summary>
    /// 系统提示词
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;
    
    /// <summary>
    /// 工作记忆
    /// </summary>
    public object? WorkingMemory { get; set; }
    
    /// <summary>
    /// Agent状态
    /// </summary>
    public object? AgentState { get; set; }
    
    /// <summary>
    /// 对话历史
    /// </summary>
    public List<AevatarConversationEntry> ConversationHistory { get; set; } = new();
    
    /// <summary>
    /// 额外的上下文数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 对话条目
/// </summary>
public class AevatarConversationEntry
{
    /// <summary>
    /// 角色（user, assistant, system等）
    /// </summary>
    public string Role { get; set; } = string.Empty;
    
    /// <summary>
    /// 内容
    /// </summary>
    public string Content { get; set; } = string.Empty;
}