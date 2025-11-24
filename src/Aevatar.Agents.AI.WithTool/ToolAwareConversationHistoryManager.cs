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
    public AevatarChatMessage AddToolCallMessage(AevatarFunctionCall functionCall)
    {
        if (functionCall == null)
            throw new ArgumentNullException(nameof(functionCall));

        var toolCallMsg = new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Content = $"Calling tool {functionCall.Name} with arguments: {functionCall.Arguments}",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ToolCalls = { new ToolCall { ToolName = functionCall.Name, Arguments = functionCall.Arguments } }
        };

        History.Add(toolCallMsg);
        return toolCallMsg;
    }

    /// <summary>
    /// Add tool result message to history.
    /// </summary>
    public AevatarChatMessage AddToolResultMessage(string toolName, ToolExecutionResult result)
    {
        if (string.IsNullOrEmpty(toolName))
            throw new ArgumentException("Tool name cannot be null or empty", nameof(toolName));
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        // Console.WriteLine($"[DEBUG] AddToolResultMessage: Tool={toolName}, Content={result.Content}");

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
        return toolResultMsg;
    }
}