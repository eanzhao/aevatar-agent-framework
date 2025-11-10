using System.Collections.Generic;
using Aevatar.Agents.AI.Core.Messages;

namespace Aevatar.Agents.AI.Core.Abstractions;

/// <summary>
/// Manages conversation history and context for AI agents.
/// </summary>
public interface IConversationManager
{
    /// <summary>
    /// Adds a user message to the conversation history.
    /// </summary>
    /// <param name="message">The user's message content.</param>
    void AddUserMessage(string message);

    /// <summary>
    /// Adds an assistant message to the conversation history.
    /// </summary>
    /// <param name="message">The assistant's response content.</param>
    void AddAssistantMessage(string message);

    /// <summary>
    /// Adds a system message to the conversation history.
    /// </summary>
    /// <param name="message">The system message content.</param>
    void AddSystemMessage(string message);

    /// <summary>
    /// Adds a function call result to the conversation history.
    /// </summary>
    /// <param name="functionName">The name of the function that was called.</param>
    /// <param name="result">The result of the function call.</param>
    void AddFunctionMessage(string functionName, string result);

    /// <summary>
    /// Gets the complete conversation history.
    /// </summary>
    /// <returns>A list of all conversation messages.</returns>
    List<AevatarChatMessage> GetHistory();

    /// <summary>
    /// Gets the conversation history limited to the most recent messages.
    /// </summary>
    /// <param name="maxMessages">The maximum number of messages to return.</param>
    /// <returns>A list of the most recent conversation messages.</returns>
    List<AevatarChatMessage> GetHistory(int maxMessages);

    /// <summary>
    /// Clears the entire conversation history.
    /// </summary>
    void ClearHistory();

    /// <summary>
    /// Gets the total number of messages in the conversation.
    /// </summary>
    int MessageCount { get; }

    /// <summary>
    /// Gets the approximate token count for the entire conversation.
    /// </summary>
    int EstimatedTokenCount { get; }

    /// <summary>
    /// Removes messages to stay within a token limit.
    /// </summary>
    /// <param name="maxTokens">The maximum number of tokens to retain.</param>
    /// <param name="preserveSystemMessage">Whether to preserve system messages when trimming.</param>
    void TrimToTokenLimit(int maxTokens, bool preserveSystemMessage = true);

    /// <summary>
    /// Gets a summary of the conversation.
    /// </summary>
    /// <returns>A summary string of the conversation.</returns>
    string GetSummary();

    /// <summary>
    /// Exports the conversation to a formatted string.
    /// </summary>
    /// <param name="format">The format to export to (e.g., "json", "text", "markdown").</param>
    /// <returns>The formatted conversation string.</returns>
    string Export(string format = "json");

    /// <summary>
    /// Imports a conversation from a formatted string.
    /// </summary>
    /// <param name="data">The conversation data to import.</param>
    /// <param name="format">The format of the data (e.g., "json", "text").</param>
    void Import(string data, string format = "json");
}
