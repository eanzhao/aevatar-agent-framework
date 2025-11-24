using System.Runtime.CompilerServices;
using System.Text;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.WithTool;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.MEAI;

// ReSharper disable InconsistentNaming
public sealed class MEAILLMProvider : AevatarLLMProviderBase
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

    public override async Task<AevatarLLMResponse> GenerateAsync(AevatarLLMRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating response using MEAI provider: {Model}", _config.Model);

        var messages = BuildChatMessages(request);
        var options = BuildChatOptions(request);
        var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);

        return CreateAevatarLLMResponse(response);
    }

    /// <summary>
    /// Build chat messages from request.
    /// </summary>
    private List<ChatMessage> BuildChatMessages(AevatarLLMRequest request)
    {
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

        return messages;
    }

    /// <summary>
    /// Build chat options including temperature, max tokens, and tools.
    /// </summary>
    private ChatOptions BuildChatOptions(AevatarLLMRequest request)
    {
        var options = new ChatOptions
        {
            Temperature = (float)(request.Settings?.Temperature ?? _config.Temperature),
            MaxOutputTokens = request.Settings?.MaxTokens ?? _config.MaxTokens,
            ModelId = _config.Model
        };

        // Add tools/functions if provided
        if (request.Functions is { Count: > 0 })
        {
            var aiTools = ConvertFunctionsToAITools(request.Functions);
            if (aiTools.Count > 0)
            {
                options.Tools = aiTools;
                _logger.LogInformation("Added {Count} tools to ChatOptions", aiTools.Count);
            }
        }

        return options;
    }

    /// <summary>
    /// Convert Aevatar function definitions to Microsoft.Extensions.AI tools.
    /// </summary>
    private List<AITool> ConvertFunctionsToAITools(IList<AevatarFunctionDefinition> functions)
    {
        var aiTools = new List<AITool>();
        foreach (var func in functions)
        {
            var aiFunc = AIFunctionFactory.Create((Func<Dictionary<string, object?>, Task<object>>)Handler, func.Name,
                func.Description);
            aiTools.Add(aiFunc);
            continue;

            // Create function tool with placeholder handler
            // Actual execution happens in ToolManager
            async Task<object> Handler(Dictionary<string, object?> _) =>
                string.Format(ToolConstants.FunctionCalledMessageFormat, func.Name);
        }

        return aiTools;
    }

    /// <summary>
    /// Create AevatarLLMResponse from Microsoft.Extensions.AI ChatResponse.
    /// </summary>
    private AevatarLLMResponse CreateAevatarLLMResponse(Microsoft.Extensions.AI.ChatResponse response)
    {
        var result = new AevatarLLMResponse
        {
            Content = response.Text ?? string.Empty,
            ModelName = response.ModelId ?? _config.Model,
            AevatarStopReason = AevatarStopReason.Complete,
            Usage = CreateTokenUsage(
                (int?)response.Usage?.InputTokenCount,
                (int?)response.Usage?.OutputTokenCount,
                (int?)response.Usage?.TotalTokenCount)
        };

        // Check if response contains function/tool calls
        // Microsoft.Extensions.AI puts tool calls in response.Messages[].Contents
        if (response.Messages?.Count > 0)
        {
            foreach (var message in response.Messages)
            {
                if (message.Contents?.Count > 0)
                {
                    foreach (var content in message.Contents)
                    {
                        if (content is FunctionCallContent functionCall)
                        {
                            _logger.LogDebug("Found function call: {FunctionName} with {ArgCount} arguments",
                                functionCall.Name, functionCall.Arguments?.Count ?? 0);
                           
                            // Unwrap arguments if wrapped in "_" (artifact of AIFunctionFactory with Dictionary parameter)
                            var arguments = functionCall.Arguments;
                            if (arguments != null && arguments.Count == 1 && arguments.ContainsKey("_") && arguments["_"] is System.Text.Json.JsonElement wrappedElement && wrappedElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                _logger.LogDebug("Unwrapping arguments from '_' key");
                                arguments = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(wrappedElement.GetRawText());
                            }
                            else if (arguments != null && arguments.Count == 1 && arguments.ContainsKey("_") && arguments["_"] is Dictionary<string, object?> wrappedDict)
                            {
                                 _logger.LogDebug("Unwrapping arguments from '_' key (Dictionary)");
                                 arguments = wrappedDict;
                            }

                            // Set AevatarFunctionCall to trigger tool execution in AIGAgentWithToolBase
                            result.AevatarFunctionCall = new AevatarFunctionCall
                            {
                                Name = functionCall.Name,
                                Arguments = arguments != null 
                                    ? System.Text.Json.JsonSerializer.Serialize(arguments)
                                    : "{}"
                            };
                            
                            // Also set content for logging
                            result.Content = string.Format(ToolConstants.FunctionCalledMessageFormat, functionCall.Name);
                            
                            // Only handle the first function call for now
                            return result;
                        }
                    }
                }
            }
        }

        return result;
    }

    public override async IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(AevatarLLMRequest request,
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
            var chunk = ExtractStreamingText(chatUpdate);
            if (string.IsNullOrEmpty(chunk))
            {
                continue;
            }

            yield return new AevatarLLMToken
            {
                Content = chunk,
                IsComplete = false
            };
        }

        yield return new AevatarLLMToken { Content = string.Empty, IsComplete = true };
    }

    private static string ExtractStreamingText(object chatUpdate)
    {
        if (chatUpdate == null)
        {
            return string.Empty;
        }

        var type = chatUpdate.GetType();

        // Try TextDelta or Text via reflection to stay resilient to SDK changes
        var text = type.GetProperty("TextDelta")?.GetValue(chatUpdate) as string;
        if (string.IsNullOrEmpty(text))
        {
            text = type.GetProperty("Text")?.GetValue(chatUpdate) as string;
        }

        if (!string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Try Message.Text
        var message = type.GetProperty("Message")?.GetValue(chatUpdate);
        if (message == null)
        {
            return string.Empty;
        }

        var messageType = message.GetType();
        text = messageType.GetProperty("Text")?.GetValue(message) as string;
        if (!string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Try aggregating message.Content (collection of parts)
        var content = messageType.GetProperty("Content")?.GetValue(message) as System.Collections.IEnumerable;
        if (content == null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var part in content)
        {
            if (part == null) continue;
            var partType = part.GetType();
            var partText = partType.GetProperty("Text")?.GetValue(part) as string;
            if (!string.IsNullOrEmpty(partText))
            {
                sb.Append(partText);
            }
        }

        return sb.ToString();
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