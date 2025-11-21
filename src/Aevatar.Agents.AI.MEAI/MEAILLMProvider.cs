using System.Runtime.CompilerServices;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.MEAI;

// ReSharper disable InconsistentNaming
public sealed class MEAILLMProvider : IAevatarLLMProvider
{
    private readonly IChatClient _chatClient;
    private readonly LLMProviderConfig _config;
    private readonly ILogger _logger;

    public MEAILLMProvider(IChatClient chatClient, LLMProviderConfig config, ILogger logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AevatarLLMResponse> GenerateAsync(AevatarLLMRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating response using MEAI provider: {Model}", _config.Model);

        var messages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(request.SystemPrompt))
            messages.Add(new ChatMessage(ChatRole.System, request.SystemPrompt));

        if (request.Messages?.Count > 0)
        {
            foreach (var msg in request.Messages)
            {
                messages.Add(new ChatMessage(
                    msg.Role == AevatarChatRole.User ? ChatRole.User : ChatRole.Assistant,
                    msg.Content));
            }
        }

        if (!string.IsNullOrEmpty(request.UserPrompt))
            messages.Add(new ChatMessage(ChatRole.User, request.UserPrompt));

        var options = new ChatOptions
        {
            Temperature = (float)(request.Settings?.Temperature ?? _config.Temperature),
            MaxOutputTokens = request.Settings?.MaxTokens ?? _config.MaxTokens,
            ModelId = _config.Model
        };
        
        // Add tools/functions if provided
        if (request.Functions != null && request.Functions.Count > 0)
        {
            var aiTools = new List<AITool>();
            foreach (var func in request.Functions)
            {
                // Create AIFunction from AevatarFunctionDefinition
                var parameters = new Dictionary<string, object>();
                if (func.Parameters != null)
                {
                    foreach (var param in func.Parameters)
                    {
                        parameters[param.Key] = new
                        {
                            type = param.Value.Type,
                            description = param.Value.Description,
                            required = param.Value.Required
                        };
                    }
                }
                
                var schema = new
                {
                    type = "object",
                    properties = parameters,
                    required = func.Parameters?
                        .Where(p => p.Value.Required)
                        .Select(p => p.Key)
                        .ToArray() ?? Array.Empty<string>()
                };
                
                // Create function tool
                Func<Dictionary<string, object?>, Task<object>> handler = async (args) => $"Function {func.Name} called";
                
                var aiFunc = AIFunctionFactory.Create(handler, func.Name, func.Description);
                aiTools.Add(aiFunc);
            }
            
            if (aiTools.Count > 0)
            {
                options.Tools = aiTools;
                _logger.LogInformation("Added {Count} tools to ChatOptions", aiTools.Count);
            }
        }

        var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);

        var result = new AevatarLLMResponse
        {
            Content = response.Text ?? string.Empty,
            ModelName = response.ModelId ?? _config.Model,
            AevatarStopReason = AevatarStopReason.Complete
        };
        
        // Note: Function calling handling depends on the specific ChatClient implementation
        // Tools have been added to ChatOptions, but the current IChatClient.GetResponseAsync
        // may not expose function calls in a standardized way. This needs further investigation
        // based on the actual ChatClient implementation being used (e.g., DeepSeekChatClient)

        if (response.Usage != null)
        {
            result.Usage = new AevatarTokenUsage
            {
                TotalTokens = (int)(response.Usage.TotalTokenCount ?? 0),
                PromptTokens = (int)(response.Usage.InputTokenCount ?? 0),
                CompletionTokens = (int)(response.Usage.OutputTokenCount ?? 0)
            };
        }

        return result;
    }

    public async IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(AevatarLLMRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating streaming response using MEAI provider: {Model}", _config.Model);

        var messages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(request.SystemPrompt))
            messages.Add(new ChatMessage(ChatRole.System, request.SystemPrompt));

        if (request.Messages.Count > 0)
        {
            foreach (var msg in request.Messages)
            {
                messages.Add(new ChatMessage(
                    msg.Role == AevatarChatRole.User ? ChatRole.User : ChatRole.Assistant,
                    msg.Content));
            }
        }

        if (!string.IsNullOrEmpty(request.UserPrompt))
            messages.Add(new ChatMessage(ChatRole.User, request.UserPrompt));

        var options = new ChatOptions
        {
            Temperature = (float)(request.Settings?.Temperature ?? _config.Temperature),
            MaxOutputTokens = request.Settings?.MaxTokens ?? _config.MaxTokens,
            ModelId = _config.Model
        };

        await foreach (var chatUpdate in _chatClient.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            if (!string.IsNullOrEmpty(chatUpdate.Text))
            {
                yield return new AevatarLLMToken
                {
                    Content = chatUpdate.Text,
                    IsComplete = false
                };
            }
        }

        yield return new AevatarLLMToken { Content = string.Empty, IsComplete = true };
    }

    public Task<AevatarModelInfo> GetModelInfoAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AevatarModelInfo
        {
            Name = _config.Model,
            MaxTokens = _config.MaxTokens,
            SupportsStreaming = _config.EnableStreaming,
            SupportsFunctions = true // MEAI supports tools/functions
        });
    }
}