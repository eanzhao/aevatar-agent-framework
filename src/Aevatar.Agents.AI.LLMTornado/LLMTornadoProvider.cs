using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Aevatar.Agents.AI.LLMTornadoExtension;

public class LLMTornadoProvider : IAevatarLLMProvider
{
    private readonly LlmTornado.TornadoApi _api;
    private readonly ILogger<LLMTornadoProvider> _logger;
    private readonly LlmTornadoConfig _config;

    public LLMTornadoProvider(LlmTornado.TornadoApi api, LlmTornadoConfig config, ILogger<LLMTornadoProvider> logger)
    {
        _api = api;
        _config = config;
        _logger = logger;
    }

    public async Task<AevatarLLMResponse> GenerateAsync(AevatarLLMRequest request, CancellationToken cancellationToken = default)
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

    public async IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(AevatarLLMRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatRequest = MapToChatRequest(request);
        // Stream property is read-only, handled by StreamChatEnumerable

        await foreach (var chunk in _api.Chat.StreamChatEnumerable(chatRequest))
        {
            yield return MapToLLMToken(chunk);
        }
    }

    private LlmTornado.Chat.ChatRequest MapToChatRequest(AevatarLLMRequest request)
    {
        var chatRequest = new LlmTornado.Chat.ChatRequest
        {
            Model = new LlmTornado.Chat.Models.ChatModel(request.Settings?.ModelId ?? "gpt-3.5-turbo"),
            Temperature = request.Settings?.Temperature ?? 0.7,
            MaxTokens = request.Settings?.MaxTokens ?? 4096,
            Messages = new List<LlmTornado.Chat.ChatMessage>()
        };

        if (request.Messages != null)
        {
            foreach (var msg in request.Messages)
            {
                var role = msg.Role switch
                {
                    AevatarChatRole.System => LlmTornado.Code.ChatMessageRoles.System,
                    AevatarChatRole.User => LlmTornado.Code.ChatMessageRoles.User,
                    AevatarChatRole.Assistant => LlmTornado.Code.ChatMessageRoles.Assistant,
                    AevatarChatRole.Tool => LlmTornado.Code.ChatMessageRoles.Tool,
                    _ => LlmTornado.Code.ChatMessageRoles.User
                };

                chatRequest.Messages.Add(new LlmTornado.Chat.ChatMessage
                {
                    Role = role,
                    Content = msg.Content
                });
            }
        }
        
        // Add System Prompt if exists and not already in messages
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
             // Check if system prompt is already added
             if (!chatRequest.Messages.Any(m => m.Role == LlmTornado.Code.ChatMessageRoles.System))
             {
                 chatRequest.Messages.Insert(0, new LlmTornado.Chat.ChatMessage
                 {
                     Role = LlmTornado.Code.ChatMessageRoles.System,
                     Content = request.SystemPrompt
                 });
             }
        }

        return chatRequest;
    }

    private AevatarLLMResponse MapToLLMResponse(LlmTornado.Chat.ChatResult? response)
    {
        if (response == null || response.Choices == null || response.Choices.Count == 0)
            return new AevatarLLMResponse { Content = string.Empty };

        var choice = response.Choices[0];
        return new AevatarLLMResponse
        {
            Content = choice.Message?.Content ?? string.Empty,
            Usage = response.Usage != null ? new AevatarTokenUsage
            {
                PromptTokens = response.Usage.PromptTokens,
                CompletionTokens = response.Usage.CompletionTokens,
                TotalTokens = response.Usage.TotalTokens
            } : null
        };
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

public class LlmTornadoConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public LlmTornado.Code.LLmProviders Provider { get; set; } = LlmTornado.Code.LLmProviders.OpenAi;
}
