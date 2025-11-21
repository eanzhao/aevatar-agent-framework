using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.WithProcessStrategy.Messages;
using Aevatar.Agents.AI.WithTool.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithProcessStrategy.Strategies;

/// <summary>
/// Standard AI processing strategy - simple pass-through to LLM
/// 标准AI处理策略 - 简单直接传递给LLM
/// </summary>
public class StandardProcessingStrategy : IAevatarAIProcessingStrategy
{
    /// <inheritdoc />
    public string Name => "Standard Processing";

    /// <inheritdoc />
    public string Description => "标准AI处理策略 - 直接将请求传递给LLM提供商，支持对话历史和工具调用";

    /// <inheritdoc />
    public AevatarAIProcessingMode Mode => AevatarAIProcessingMode.Standard;

    /// <inheritdoc />
    public bool CanHandle(AevatarAIContext context)
    {
        // 标准策略可以处理所有基础请求
        // 但如果上下文中明确指定了其他策略，则返回false
        if (context.Metadata?.ContainsKey("PreferredStrategy") == true)
        {
            var preferred = context.Metadata["PreferredStrategy"]?.ToString();
            return string.Equals(preferred, "Standard", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(preferred, Name, StringComparison.OrdinalIgnoreCase);
        }

        // 标准策略适合处理简单的问答
        return true;
    }

    /// <inheritdoc />
    public double EstimateComplexity(AevatarAIContext context)
    {
        // 基于问题长度和对话历史估算复杂度
        var questionLength = context.Question?.Length ?? 0;
        var historyCount = context.ConversationHistory?.Count ?? 0;

        // 简单的启发式计算
        var complexity = 0.0;

        // 问题长度影响（0-0.3）
        complexity += Math.Min(questionLength / 1000.0, 0.3);

        // 对话历史影响（0-0.3）
        complexity += Math.Min(historyCount / 20.0, 0.3);

        // 如果需要工具调用，增加复杂度
        if (context.Metadata?.ContainsKey("ExpectsToolUse") == true)
        {
            complexity += 0.2;
        }

        // 标准策略适合低到中等复杂度
        return Math.Min(complexity, 0.5);
    }

    /// <inheritdoc />
    public bool ValidateRequirements(AevatarAIStrategyDependencies dependencies)
    {
        // 验证必需的依赖项
        if (dependencies == null)
        {
            return false;
        }

        // LLM提供商是必需的
        if (dependencies.LLMProvider == null)
        {
            dependencies.Logger?.LogError("StandardProcessingStrategy requires LLMProvider");
            return false;
        }

        // 配置是必需的
        if (dependencies.Configuration == null)
        {
            dependencies.Logger?.LogError("StandardProcessingStrategy requires Configuration");
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<string> ProcessAsync(
        AevatarAIContext context,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken = default)
    {
        dependencies.Logger?.LogDebug("Processing with Standard strategy for question: {Question}",
            context.Question);

        try
        {
            // Build the LLM request with system prompt and user question
            var request = new AevatarLLMRequest
            {
                SystemPrompt = context.SystemPrompt ?? "You are a helpful AI assistant.",
                UserPrompt = context.Question
            };

            // Add conversation history if available  
            if (context.ConversationHistory?.Count > 0)
            {
                // ConversationHistory is List<AevatarConversationEntry>, convert to messages
                foreach (var entry in context.ConversationHistory)
                {
                    var message = new AevatarChatMessage
                    {
                        Role = ParseChatRole(entry.Role),
                        Content = entry.Content,
                        Timestamp = DateTime.UtcNow.ToTimestamp()
                    };
                    request.Messages.Add(message);
                }
            }

            // Configure LLM settings
            if (request.Settings == null)
            {
                request.Settings = new AevatarLLMSettings();
            }

            request.Settings.ModelId = dependencies.Configuration.Model;
            request.Settings.Temperature = dependencies.Configuration.Temperature;
            request.Settings.MaxTokens = dependencies.Configuration.MaxTokens;

            // Add tool definitions if available
            if (dependencies.ToolManager != null)
            {
                var functionDefs = await dependencies.ToolManager.GenerateFunctionDefinitionsAsync(cancellationToken);
                if (functionDefs?.Count > 0)
                {
                    request.Functions = functionDefs.ToList();
                }
            }

            // Call the LLM provider
            dependencies.Logger?.LogDebug("Calling LLM provider with model: {Model}",
                request.Settings.ModelId);

            var response = await dependencies.LLMProvider.GenerateAsync(request, cancellationToken);

            // Handle tool calls if present
            if (response.AevatarFunctionCall != null && dependencies.ToolManager != null)
            {
                dependencies.Logger?.LogInformation("AI requested tool call: {ToolName}",
                    response.AevatarFunctionCall.Name);

                // Parse arguments from JSON string to dictionary
                var parameters = System.Text.Json.JsonSerializer
                                     .Deserialize<System.Collections.Generic.Dictionary<string, object>>(
                                         response.AevatarFunctionCall.Arguments) ??
                                 new System.Collections.Generic.Dictionary<string, object>();

                // Execute the tool
                var toolResult = await dependencies.ToolManager.ExecuteToolAsync(
                    response.AevatarFunctionCall.Name,
                    parameters,
                    null,
                    cancellationToken);

                // Publish tool executed event if callback is available
                if (dependencies.PublishEventCallback != null && toolResult.IsSuccess)
                {
                    var toolEvent = new AevatarToolExecutedEvent
                    {
                        ToolName = response.AevatarFunctionCall.Name,
                        Result = toolResult.Content ?? "No result",
                        Success = toolResult.IsSuccess
                    };

                    // Add parameters to the event
                    foreach (var param in parameters)
                    {
                        toolEvent.Parameters[param.Key] = param.Value?.ToString() ?? string.Empty;
                    }

                    await dependencies.PublishEventCallback(toolEvent);
                }

                // Append tool result to the response
                var finalResponse =
                    $"{response.Content}\n\n[Tool Executed: {response.AevatarFunctionCall.Name}]\n{toolResult.Content}";

                dependencies.Logger?.LogDebug("Standard strategy completed with tool execution");
                return finalResponse;
            }

            // Return the LLM response directly
            dependencies.Logger?.LogDebug("Standard strategy completed successfully");
            return response.Content;
        }
        catch (Exception ex)
        {
            dependencies.Logger?.LogError(ex, "Error in Standard processing strategy");

            // Return a user-friendly error message
            return "I apologize, but I encountered an error while processing your request. Please try again.";
        }
    }

    private Aevatar.Agents.AI.AevatarChatRole ParseChatRole(string roleString)
    {
        return roleString?.ToLowerInvariant() switch
        {
            "system" => AevatarChatRole.System,
            "user" => AevatarChatRole.User,
            "assistant" => AevatarChatRole.Assistant,
            "tool" or "function" => AevatarChatRole.Tool,
            _ => AevatarChatRole.User
        };
    }
}