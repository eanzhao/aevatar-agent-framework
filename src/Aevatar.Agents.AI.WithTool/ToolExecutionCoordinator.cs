using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Aevatar.Agents.AI.WithTool.Messages;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Aevatar.Agents.AI.WithTool;

/// <summary>
/// Coordinates tool execution workflow including parsing, execution, and response generation.
/// Separates tool execution logic from the main agent class.
/// </summary>
public class ToolExecutionCoordinator
{
    private readonly IAevatarToolManager _toolManager;
    private readonly IAevatarLLMProvider _llmProvider;
    private readonly ConversationHistoryManager _historyManager;
    private readonly ILogger? _logger;

    public ToolExecutionCoordinator(
        IAevatarToolManager toolManager,
        IAevatarLLMProvider llmProvider,
        ConversationHistoryManager historyManager,
        ILogger? logger = null)
    {
        _toolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
        _llmProvider = llmProvider ?? throw new ArgumentNullException(nameof(llmProvider));
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
        _logger = logger;
    }

    /// <summary>
    /// Handle complete tool execution workflow.
    /// </summary>
    public async Task<(ToolExecutionResult Result, AevatarLLMResponse FinalResponse)> ExecuteToolWorkflowAsync(
        AevatarFunctionCall functionCall,
        AevatarLLMRequest llmRequest,
        CancellationToken cancellationToken = default)
    {
        if (functionCall == null)
            throw new ArgumentNullException(nameof(functionCall));

        _logger?.LogDebug("Executing tool: {ToolName}", functionCall.Name);

        // 1. Add tool call to history
        _historyManager.AddToolCallMessage(functionCall);

        // 2. Execute tool
        var result = await ExecuteToolAsync(functionCall, cancellationToken);

        // 3. Add tool result to history
        _historyManager.AddToolResultMessage(functionCall.Name, result);

        // 4. Generate final LLM response with tool result
        var finalResponse = await _llmProvider.GenerateAsync(llmRequest, cancellationToken);

        // 5. Add assistant response to history
        _historyManager.AddMessage(finalResponse.Content, AevatarChatRole.Assistant);

        return (result, finalResponse);
    }

    /// <summary>
    /// Execute tool with given function call.
    /// </summary>
    private async Task<ToolExecutionResult> ExecuteToolAsync(
        AevatarFunctionCall functionCall,
        CancellationToken cancellationToken)
    {
        // Parse arguments
        var parameters = ParseToolArguments(functionCall.Arguments);

        // Execute tool via tool manager
        return await _toolManager.ExecuteToolAsync(
            functionCall.Name,
            parameters,
            context: null,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Parse tool arguments from JSON string.
    /// </summary>
    private Dictionary<string, object> ParseToolArguments(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return new Dictionary<string, object>();

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson);
            if (dict == null)
                return new Dictionary<string, object>();

            var result = new Dictionary<string, object>();
            foreach (var kvp in dict)
            {
                result[kvp.Key] = kvp.Value.ValueKind switch
                {
                    JsonValueKind.String => kvp.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => kvp.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null!,
                    _ => kvp.Value.ToString()
                };
            }
            return result;
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Failed to parse tool arguments: {Arguments}", argumentsJson);
            return new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Create ChatResponse with tool execution information.
    /// </summary>
    public ChatResponse CreateResponseWithToolInfo(
        string requestId,
        AevatarLLMResponse llmResponse,
        AevatarFunctionCall functionCall,
        ToolExecutionResult toolResult)
    {
        var response = new ChatResponse
        {
            Content = llmResponse.Content,
            RequestId = requestId,
            ToolCalled = true,
            ToolCall = new ToolCallInfo
            {
                ToolName = functionCall.Name,
                Result = toolResult.Content ?? string.Empty
            }
        };

        // Populate tool call arguments
        if (!string.IsNullOrEmpty(functionCall.Arguments))
        {
            var args = ParseToolArguments(functionCall.Arguments);
            foreach (var arg in args)
            {
                response.ToolCall.Arguments[arg.Key.ToString()] = arg.Value?.ToString() ?? string.Empty;
            }
        }

        // Add usage information
        if (llmResponse.Usage != null)
        {
            response.Usage = new AevatarTokenUsage
            {
                PromptTokens = llmResponse.Usage.PromptTokens,
                CompletionTokens = llmResponse.Usage.CompletionTokens,
                TotalTokens = llmResponse.Usage.TotalTokens
            };
        }

        return response;
    }
}
