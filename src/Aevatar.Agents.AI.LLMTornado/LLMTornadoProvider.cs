using Aevatar.Agents.AI.Abstractions;
using LlmTornado;
using LlmTornado.Code;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.LLMTornado;

public class LLMTornadoProvider : AevatarLLMProviderBase
{
    private readonly TornadoApi _api;
    private readonly ILogger<LLMTornadoProvider> _logger;

    public LLMTornadoProvider(TornadoApi api, ILogger<LLMTornadoProvider> logger)
    {
        _api = api;
        _logger = logger;
    }

    public override async Task<AevatarLLMResponse> GenerateAsync(AevatarLLMRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var chatRequest = MapToChatRequest(request);
            var response = await _api.Chat.CreateChatCompletion(chatRequest);

            return MapToLLMResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating response from LlmTornado");
            throw;
        }
    }

    public override async IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(AevatarLLMRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        var chatRequest = MapToChatRequest(request);
        // Stream property is read-only, handled by StreamChatEnumerable

        await foreach (var chunk in _api.Chat.StreamChatEnumerable(chatRequest).WithCancellation(cancellationToken))
        {
            yield return MapToLLMToken(chunk);
        }
    }

    private LlmTornado.Chat.ChatRequest MapToChatRequest(AevatarLLMRequest request)
    {
        var chatRequest = new LlmTornado.Chat.ChatRequest
        {
            Model = new LlmTornado.Chat.Models.ChatModel(request.Settings?.ModelId ?? AevatarAIDefaults.DefaultModel),
            Temperature = request.Settings?.Temperature ?? AevatarAIDefaults.DefaultTemperature,
            MaxTokens = request.Settings?.MaxTokens ?? AevatarAIDefaults.DefaultMaxTokensExtended,
            Messages = []
        };

        if (request.Messages != null)
        {
            foreach (var msg in request.Messages)
            {
                var role = msg.Role switch
                {
                    AevatarChatRole.System => ChatMessageRoles.System,
                    AevatarChatRole.User => ChatMessageRoles.User,
                    AevatarChatRole.Assistant => ChatMessageRoles.Assistant,
                    AevatarChatRole.Tool => ChatMessageRoles.Tool,
                    _ => ChatMessageRoles.User
                };

                var chatMsg = new LlmTornado.Chat.ChatMessage
                {
                    Role = role,
                    Content = msg.Content
                };

                // Handle Tool Call ID for Tool messages
                // Note: AevatarChatMessage currently doesn't support ToolCallId directly.
                // We might need to use Metadata if supported in future.
                
                chatRequest.Messages.Add(chatMsg);
            }
        }

        // Add System Prompt if exists and not already in messages
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            // Check if system prompt is already added
            if (chatRequest.Messages.All(m => m.Role != ChatMessageRoles.System))
            {
                chatRequest.Messages.Insert(0, new LlmTornado.Chat.ChatMessage
                {
                    Role = ChatMessageRoles.System,
                    Content = request.SystemPrompt
                });
            }
        }

        // Map Tools
        if (request.Functions != null && request.Functions.Count > 0)
        {
            chatRequest.Tools = MapToChatTools(request.Functions);
            chatRequest.ToolChoice = "auto";
        }

        return chatRequest;
    }

    private List<LlmTornado.Common.Tool> MapToChatTools(IList<AevatarFunctionDefinition> functions)
    {
        var tools = new List<LlmTornado.Common.Tool>();

        foreach (var func in functions)
        {
            var tool = new LlmTornado.Common.Tool
            {
                Type = "function",
                Function = new LlmTornado.Common.ToolFunction(
                    func.Name,
                    func.Description,
                    MapFunctionParameters(func.Parameters)
                )
            };
            tools.Add(tool);
        }

        return tools;
    }

    private AevatarLLMResponse MapToLLMResponse(LlmTornado.Chat.ChatResult? response)
    {
        if (response == null || response.Choices == null || response.Choices.Count == 0)
            return new AevatarLLMResponse { Content = string.Empty };

        var choice = response.Choices[0];
        var result = new AevatarLLMResponse
        {
            Content = choice.Message?.Content ?? string.Empty,
            Usage = CreateTokenUsage(
                response.Usage?.PromptTokens,
                response.Usage?.CompletionTokens,
                response.Usage?.TotalTokens)
        };

        // Handle Tool Calls
        if (choice.Message?.ToolCalls != null && choice.Message.ToolCalls.Count > 0)
        {
            var toolCall = choice.Message.ToolCalls[0];
            
            // Try to access FunctionCall property (guessing name)
            if (toolCall.FunctionCall != null)
            {
                result.AevatarFunctionCall = new AevatarFunctionCall
                {
                    Name = toolCall.FunctionCall.Name!,
                    Arguments = toolCall.FunctionCall.Arguments
                };
            }
        }

        return result;
    }

    private AevatarLLMToken MapToLLMToken(LlmTornado.Chat.ChatResult? chunk)
    {
        if (chunk == null || chunk.Choices == null || chunk.Choices.Count == 0)
            return new AevatarLLMToken { Content = string.Empty };

        var choice = chunk.Choices[0];
        return new AevatarLLMToken
        {
            Content = choice.Delta?.Content ?? string.Empty
        };
    }
}