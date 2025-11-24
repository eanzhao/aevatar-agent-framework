using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.WithTool;

/// <summary>
/// Adds tool-specific history helpers on top of the base conversation manager.
/// </summary>
public class ToolAwareConversationHistoryManager : ConversationHistoryManager
{
    public ToolAwareConversationHistoryManager(RepeatedField<AevatarChatMessage> history)
        : base(history)
    {
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

        History.Add(toolCallMsg);
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

        History.Add(toolResultMsg);
    }
}