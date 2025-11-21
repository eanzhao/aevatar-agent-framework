using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.AI.Core.Messages;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.Core.Extensions;

/// <summary>
/// Extension methods for managing AI conversation history.
/// 为AI对话历史提供扩展方法
/// </summary>
public static class ConversationExtensions
{
    #region Add Message Methods
    
    /// <summary>
    /// Adds a user message to the conversation history.
    /// 添加用户消息到对话历史
    /// </summary>
    public static void AddUserMessage(this AevatarAIAgentState state, string message, int? maxHistory = null)
    {
        var chatMessage = new AevatarChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            Role = AevatarChatRole.User,
            Content = message,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            TokenUsed = EstimateTokenCount(message)
        };
        
        state.History.Add(chatMessage);
        
        // Trim history if needed
        if (maxHistory.HasValue && state.History.Count > maxHistory.Value)
        {
            state.TrimHistory(maxHistory.Value);
        }
    }
    
    /// <summary>
    /// Adds an assistant message to the conversation history.
    /// 添加助手消息到对话历史
    /// </summary>
    public static void AddAssistantMessage(this AevatarAIAgentState state, string message, int? maxHistory = null)
    {
        var chatMessage = new AevatarChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            Role = AevatarChatRole.Assistant,
            Content = message,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            TokenUsed = EstimateTokenCount(message)
        };
        
        state.History.Add(chatMessage);
        
        // Trim history if needed
        if (maxHistory.HasValue && state.History.Count > maxHistory.Value)
        {
            state.TrimHistory(maxHistory.Value);
        }
    }
    
    /// <summary>
    /// Adds a system message to the conversation history.
    /// 添加系统消息到对话历史
    /// </summary>
    public static void AddSystemMessage(this AevatarAIAgentState state, string message, int? maxHistory = null)
    {
        var chatMessage = new AevatarChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            Role = AevatarChatRole.System,
            Content = message,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            TokenUsed = EstimateTokenCount(message)
        };
        
        state.History.Add(chatMessage);
        
        // Trim history if needed
        if (maxHistory.HasValue && state.History.Count > maxHistory.Value)
        {
            state.TrimHistory(maxHistory.Value);
        }
    }
    
    /// <summary>
    /// Adds a function message to the conversation history.
    /// 添加函数消息到对话历史
    /// </summary>
    public static void AddFunctionMessage(this AevatarAIAgentState state, 
        string functionName, 
        string result,
        string? arguments = null,
        int? maxHistory = null)
    {
        var chatMessage = new AevatarChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            Role = AevatarChatRole.Tool,
            Content = result,
            ToolCalls = { new ToolCall{ToolName = functionName, Arguments = arguments} },
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            TokenUsed = EstimateTokenCount(result)
        };
        
        state.History.Add(chatMessage);
        
        // Trim history if needed
        if (maxHistory.HasValue && state.History.Count > maxHistory.Value)
        {
            state.TrimHistory(maxHistory.Value);
        }
    }
    
    /// <summary>
    /// Adds a generic chat message to the conversation history.
    /// 添加通用聊天消息到对话历史
    /// </summary>
    public static void AddChatMessage(this AevatarAIAgentState state, AevatarChatMessage message, int? maxHistory = null)
    {
        // Ensure message has an ID and timestamp
        if (string.IsNullOrEmpty(message.Id))
        {
            message.Id = Guid.NewGuid().ToString();
        }
        if (message.Timestamp == null)
        {
            message.Timestamp = Timestamp.FromDateTime(DateTime.UtcNow);
        }
        
        // Estimate tokens if not set
        if (message.TokenUsed == 0)
        {
            message.TokenUsed = EstimateTokenCount(message.Content);
        }
        
        state.History.Add(message);
        
        // Trim history if needed
        if (maxHistory.HasValue && state.History.Count > maxHistory.Value)
        {
            state.TrimHistory(maxHistory.Value);
        }
    }
    
    #endregion
    
    #region Query Methods
    
    /// <summary>
    /// Gets the conversation history limited to the most recent messages.
    /// 获取最近的对话历史
    /// </summary>
    public static List<AevatarChatMessage> GetRecentHistory(this AevatarAIAgentState state, int maxMessages)
    {
        if (maxMessages <= 0 || state.History.Count == 0)
        {
            return new List<AevatarChatMessage>();
        }
        
        return state.History
            .Skip(Math.Max(0, state.History.Count - maxMessages))
            .ToList();
    }
    
    /// <summary>
    /// Gets the estimated total token count for the conversation.
    /// 获取对话的预估总token数
    /// </summary>
    public static int GetEstimatedTokenCount(this AevatarAIAgentState state)
    {
        return state.History.Sum(m => m.TokenUsed);
    }
    
    /// <summary>
    /// Gets messages filtered by role.
    /// 按角色过滤消息
    /// </summary>
    public static List<AevatarChatMessage> GetMessagesByRole(this AevatarAIAgentState state, AevatarChatRole role)
    {
        return state.History.Where(m => m.Role == role).ToList();
    }
    
    /// <summary>
    /// Gets the last message in the conversation.
    /// 获取最后一条消息
    /// </summary>
    public static AevatarChatMessage? GetLastMessage(this AevatarAIAgentState state)
    {
        return state.History.LastOrDefault();
    }
    
    /// <summary>
    /// Gets the last user message in the conversation.
    /// 获取最后一条用户消息
    /// </summary>
    public static AevatarChatMessage? GetLastUserMessage(this AevatarAIAgentState state)
    {
        return state.History.LastOrDefault(m => m.Role == AevatarChatRole.User);
    }
    
    /// <summary>
    /// Gets the last assistant message in the conversation.
    /// 获取最后一条助手消息
    /// </summary>
    public static AevatarChatMessage? GetLastAssistantMessage(this AevatarAIAgentState state)
    {
        return state.History.LastOrDefault(m => m.Role == AevatarChatRole.Assistant);
    }
    
    #endregion
    
    #region Management Methods
    
    /// <summary>
    /// Clears the conversation history.
    /// 清空对话历史
    /// </summary>
    public static void ClearHistory(this AevatarAIAgentState state)
    {
        state.History.Clear();
    }
    
    /// <summary>
    /// Trims the conversation history to the specified number of messages.
    /// 将对话历史修剪到指定数量的消息
    /// </summary>
    public static void TrimHistory(this AevatarAIAgentState state, int maxMessages)
    {
        if (state.History.Count <= maxMessages)
        {
            return;
        }
        
        var toKeep = state.History
            .Skip(state.History.Count - maxMessages)
            .ToList();
        
        state.History.Clear();
        foreach (var message in toKeep)
        {
            state.History.Add(message);
        }
    }
    
    /// <summary>
    /// Trims the conversation history to stay within a token limit.
    /// 修剪对话历史以保持在token限制内
    /// </summary>
    public static void TrimToTokenLimit(this AevatarAIAgentState state, int maxTokens, bool preserveSystemMessage = true)
    {
        if (state.History.Count == 0)
        {
            return;
        }
        
        var systemMessages = preserveSystemMessage
            ? state.History.Where(m => m.Role == AevatarChatRole.System).ToList()
            : new List<AevatarChatMessage>();
        
        var nonSystemMessages = state.History
            .Where(m => m.Role != AevatarChatRole.System)
            .ToList();
        
        var currentTokens = systemMessages.Sum(m => m.TokenUsed);
        var messagesToKeep = new List<AevatarChatMessage>();
        
        // Add messages from the most recent backwards
        for (int i = nonSystemMessages.Count - 1; i >= 0; i--)
        {
            var message = nonSystemMessages[i];
            if (currentTokens + message.TokenUsed <= maxTokens)
            {
                messagesToKeep.Insert(0, message);
                currentTokens += message.TokenUsed;
            }
            else
            {
                break;
            }
        }
        
        // Rebuild conversation history
        state.History.Clear();
        foreach (var msg in systemMessages)
        {
            state.History.Add(msg);
        }
        foreach (var msg in messagesToKeep)
        {
            state.History.Add(msg);
        }
    }
    
    #endregion
    
    #region Export/Import Methods
    
    /// <summary>
    /// Exports the conversation to a JSON string.
    /// 将对话导出为JSON字符串
    /// </summary>
    public static string ExportConversationAsJson(this AevatarAIAgentState state)
    {
        var messages = state.History.Select(m => new
        {
            id = m.Id,
            role = m.Role.ToString(),
            content = m.Content,
            timestamp = m.Timestamp?.ToDateTime().ToString("O"),
            toolCall = m.ToolCalls,
            tokenUsed = m.TokenUsed,
            metadata = m.Metadata
        }).ToList();
        
        return JsonSerializer.Serialize(messages, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
    
    /// <summary>
    /// Exports the conversation to a markdown string.
    /// 将对话导出为Markdown字符串
    /// </summary>
    public static string ExportConversationAsMarkdown(this AevatarAIAgentState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Conversation History");
        sb.AppendLine();
        
        foreach (var message in state.History)
        {
            var timestamp = message.Timestamp?.ToDateTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A";
            
            switch (message.Role)
            {
                case AevatarChatRole.User:
                    sb.AppendLine($"## 👤 User [{timestamp}]");
                    break;
                case AevatarChatRole.Assistant:
                    sb.AppendLine($"## 🤖 Assistant [{timestamp}]");
                    break;
                case AevatarChatRole.System:
                    sb.AppendLine($"## ⚙️ System [{timestamp}]");
                    break;
                case AevatarChatRole.Tool:
                    foreach (var toolCall in message.ToolCalls)
                    {
                        sb.AppendLine($"## 🔧 Function: {toolCall.ToolName} [{timestamp}]");
                        if (!string.IsNullOrEmpty(toolCall.Arguments))
                        {
                            sb.AppendLine($"**Arguments:** `{toolCall.Arguments}`");
                        }
                    }

                    break;
            }
            
            sb.AppendLine();
            sb.AppendLine(message.Content);
            
            if (message.TokenUsed > 0)
            {
                sb.AppendLine($"*Tokens: {message.TokenUsed}*");
            }
            
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Gets a summary of the conversation.
    /// 获取对话摘要
    /// </summary>
    public static string GetConversationSummary(this AevatarAIAgentState state)
    {
        if (state.History.Count == 0)
        {
            return "Empty conversation";
        }
        
        var sb = new StringBuilder();
        sb.AppendLine($"Conversation Summary ({state.History.Count} messages):");
        
        var userMessages = state.History.Count(m => m.Role == AevatarChatRole.User);
        var assistantMessages = state.History.Count(m => m.Role == AevatarChatRole.Assistant);
        var systemMessages = state.History.Count(m => m.Role == AevatarChatRole.System);
        var functionMessages = state.History.Count(m => m.Role == AevatarChatRole.Tool);
        
        sb.AppendLine($"- User messages: {userMessages}");
        sb.AppendLine($"- Assistant messages: {assistantMessages}");
        sb.AppendLine($"- System messages: {systemMessages}");
        sb.AppendLine($"- Function messages: {functionMessages}");
        sb.AppendLine($"- Total tokens: {state.GetEstimatedTokenCount()}");
        
        if (state.History.Count > 0)
        {
            var firstMessage = state.History.First();
            var lastMessage = state.History.Last();
            sb.AppendLine($"- Started: {firstMessage.Timestamp?.ToDateTime():yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"- Last activity: {lastMessage.Timestamp?.ToDateTime():yyyy-MM-dd HH:mm:ss}");
        }
        
        return sb.ToString();
    }
    
    #endregion
    
    #region Helper Methods
    
    /// <summary>
    /// Estimates the token count for a given text.
    /// 估算文本的token数量
    /// </summary>
    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }
        
        // Simple estimation: ~4 characters per token (rough average for English)
        // For more accurate counting, use a proper tokenizer
        return (int)Math.Ceiling(text.Length / 4.0);
    }
    
    /// <summary>
    /// Creates a conversation summary message for context reduction.
    /// 创建对话摘要消息以减少上下文
    /// </summary>
    public static ConversationSummary CreateConversationSummary(this AevatarAIAgentState state, 
        int messagesToSummarize = 10)
    {
        var summary = new ConversationSummary();
        
        if (state.History.Count == 0)
        {
            summary.Content = "No conversation to summarize.";
            return summary;
        }
        
        var messages = state.History
            .Take(Math.Min(messagesToSummarize, state.History.Count))
            .ToList();
        
        // Build summary content
        var sb = new StringBuilder();
        sb.AppendLine($"Summary of {messages.Count} messages:");
        
        foreach (var msg in messages)
        {
            var role = msg.Role.ToString().ToLower();
            var preview = msg.Content.Length > 50 
                ? msg.Content.Substring(0, 47) + "..." 
                : msg.Content;
            sb.AppendLine($"- {role}: {preview}");
        }
        
        summary.Content = sb.ToString();
        
        // Extract topics (simplified - in production, use NLP)
        summary.Topics.AddRange(ExtractTopics(messages));
        
        // Extract key points
        summary.KeyPoints.AddRange(ExtractKeyPoints(messages));
        
        // Determine sentiment (simplified)
        summary.Sentiment = DetermineSentiment(messages);
        
        return summary;
    }
    
    private static List<string> ExtractTopics(List<AevatarChatMessage> messages)
    {
        // Simplified topic extraction
        // In production, use proper NLP techniques
        var topics = new HashSet<string>();
        
        foreach (var msg in messages)
        {
            // Extract nouns as potential topics (very simplified)
            var words = msg.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words.Where(w => w.Length > 5))
            {
                if (char.IsUpper(word[0]))
                {
                    topics.Add(word);
                }
            }
        }
        
        return topics.Take(5).ToList();
    }
    
    private static List<string> ExtractKeyPoints(List<AevatarChatMessage> messages)
    {
        // Simplified key point extraction
        var keyPoints = new List<string>();
        
        // Take the first sentence of each assistant message as a key point
        var assistantMessages = messages.Where(m => m.Role == AevatarChatRole.Assistant);
        foreach (var msg in assistantMessages.Take(3))
        {
            var firstSentence = msg.Content.Split('.', '!', '?').FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(firstSentence))
            {
                keyPoints.Add(firstSentence);
            }
        }
        
        return keyPoints;
    }
    
    private static string DetermineSentiment(List<AevatarChatMessage> messages)
    {
        // Very simplified sentiment analysis
        var content = string.Join(" ", messages.Select(m => m.Content));
        
        var positiveWords = new[] { "good", "great", "excellent", "happy", "success", "thank" };
        var negativeWords = new[] { "bad", "error", "fail", "problem", "issue", "wrong" };
        
        var positiveCount = positiveWords.Count(word => 
            content.Contains(word, StringComparison.OrdinalIgnoreCase));
        var negativeCount = negativeWords.Count(word => 
            content.Contains(word, StringComparison.OrdinalIgnoreCase));
        
        if (positiveCount > negativeCount)
            return "positive";
        if (negativeCount > positiveCount)
            return "negative";
        return "neutral";
    }
    
    #endregion
}

