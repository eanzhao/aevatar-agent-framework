using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.WithTool.Messages;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.WithTool;

/// <summary>
/// Manages conversation history for AI agents with tools.
/// Responsible for adding messages to history and maintaining conversation state.
/// </summary>
public class ConversationHistoryManager
{
    private readonly RepeatedField<AevatarChatMessage> _history;

    /// <summary>
    /// Initializes a new instance of ConversationHistoryManager.
    /// </summary>
    /// <param name="history">The conversation history collection to manage</param>
    public ConversationHistoryManager(RepeatedField<AevatarChatMessage> history)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    /// <summary>
    /// Add a message to conversation history.
    /// </summary>
    public void AddMessage(string content, AevatarChatRole role, string? name = null)
    {
        var msg = new AevatarChatMessage
        {
            Role = role,
            Content = content,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        _history.Add(msg);
    }

    /// <summary>
    /// Add a pre-constructed message to conversation history.
    /// </summary>
    public void AddMessage(AevatarChatMessage message)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if (message.Timestamp == null)
        {
            message.Timestamp = Timestamp.FromDateTime(DateTime.UtcNow);
        }

        _history.Add(message);
    }

    /// <summary>
    /// Add tool call message to history.
    /// </summary>
    public void AddToolCallMessage(AevatarFunctionCall functionCall)
    {
        if (functionCall == null)
            throw new ArgumentNullException(nameof(functionCall));

        var toolCallMsg = new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ToolCalls = { new ToolCall { ToolName = functionCall.Name, Arguments = functionCall.Arguments } }
        };
        
        _history.Add(toolCallMsg);
    }

    /// <summary>
    /// Add tool result message to history.
    /// </summary>
    public void AddToolResultMessage(string toolName, ToolExecutionResult result)
    {
        if (string.IsNullOrEmpty(toolName))
            throw new ArgumentException("Tool name cannot be null or empty", nameof(toolName));
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        var toolResultMsg = new AevatarChatMessage
        {
            Role = AevatarChatRole.Tool,
            Content = result.Content,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ToolResult = new ToolExecutionResult
            {
                ToolName = toolName,
                Content = result.Content,
                IsSuccess = result.IsSuccess,
                ErrorMessage = result.ErrorMessage
            }
        };
        
        _history.Add(toolResultMsg);
    }

    /// <summary>
    /// Get the number of messages in history.
    /// </summary>
    public int MessageCount => _history.Count;

    /// <summary>
    /// Clear all messages from history.
    /// </summary>
    public void Clear()
    {
        _history.Clear();
    }
}
