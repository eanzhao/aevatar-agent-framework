using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.WithTool;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

// ReSharper disable InconsistentNaming
namespace Aevatar.Agents.AI.MEAI;

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

    /// <summary>
    /// Get model info - MEAI supports streaming for most models.
    /// </summary>
    public Task<AevatarModelInfo> GetModelInfoAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AevatarModelInfo
        {
            Name = _config.Model,
            MaxTokens = _config.MaxTokens,
            SupportsStreaming = true,  // MEAI supports streaming via GetStreamingResponseAsync
            SupportsFunctions = true   // MEAI supports tools/functions
        });
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
    /// Maps Aevatar chat role to Microsoft.Extensions.AI chat role.
    /// </summary>
    private static ChatRole MapToMEAIChatRole(AevatarChatRole role) =>
        role == AevatarChatRole.User ? ChatRole.User : ChatRole.Assistant;

    /// <summary>
    /// Builds chat messages from request.
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
                messages.Add(new ChatMessage(MapToMEAIChatRole(msg.Role), msg.Content));
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
            // TODO: Some models donot support temperature, need to fix
            //Temperature = (float)(request.Settings?.Temperature ?? _config.Temperature),
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
            var schema = ConvertParametersToJsonSchema(func.Parameters);

            // Create custom AIFunction with our schema and handler
            var aiFunc = new DelegatingAIFunction(
                func.Name,
                func.Description,
                schema,
                (_, _) => Task.FromResult<object>(string.Format(ToolConstants.FunctionCalledMessageFormat, func.Name)));

            aiTools.Add(aiFunc);
        }

        return aiTools;
    }

    /// <summary>
    /// Custom AIFunction implementation that wraps our handler and exposes custom JsonSchema
    /// </summary>
    private sealed class DelegatingAIFunction : AIFunction
    {
        private readonly Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<object>> _handler;
        private readonly string _name;
        private readonly string _description;
        private readonly JsonElement _jsonSchema;

        public DelegatingAIFunction(
            string name,
            string description,
            JsonElement jsonSchema,
            Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<object>> handler)
        {
            _handler = handler;
            _name = name;
            _description = description;
            _jsonSchema = jsonSchema;
        }

        public override string Name => _name;
        public override string Description => _description;
        public override JsonElement JsonSchema => _jsonSchema;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            return await _handler(arguments, cancellationToken);
        }
    }

    private static JsonElement ConvertParametersToJsonSchema(Dictionary<string, AevatarParameterDefinition> parameters)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var param in parameters)
        {
            var paramDef = param.Value;
            var property = new JsonObject
            {
                ["type"] = paramDef.Type,
                ["description"] = paramDef.Description
            };

            if (paramDef.Enum != null && paramDef.Enum.Count > 0)
            {
                var enumArray = new JsonArray();
                foreach (var val in paramDef.Enum)
                {
                    enumArray.Add(val);
                }

                property["enum"] = enumArray;
            }

            properties[param.Key] = property;

            if (paramDef.Required)
            {
                required.Add(param.Key);
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        };

        return JsonSerializer.Deserialize<JsonElement>(schema.ToJsonString());
    }

    /// <summary>
    /// Unwraps function arguments that may be wrapped in a "_" key.
    /// This handles an artifact of AIFunctionFactory with Dictionary parameters.
    /// </summary>
    private IDictionary<string, object?>? UnwrapFunctionArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments == null || arguments.Count != 1 || !arguments.TryGetValue("_", out var wrappedValue))
            return arguments;

        if (wrappedValue is JsonElement wrappedElement &&
            wrappedElement.ValueKind == JsonValueKind.Object)
        {
            _logger.LogDebug("Unwrapping arguments from '_' key (JsonElement)");
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(wrappedElement.GetRawText());
        }

        if (wrappedValue is Dictionary<string, object?> wrappedDict)
        {
            _logger.LogDebug("Unwrapping arguments from '_' key (Dictionary)");
            return wrappedDict;
        }

        return arguments;
    }

    /// <summary>
    /// Processes function calls from the chat response and creates an AevatarFunctionCall if found.
    /// </summary>
    private AevatarFunctionCall? ProcessFunctionCalls(Microsoft.Extensions.AI.ChatResponse response)
    {
        if (response.Messages?.Count == 0)
            return null;

        foreach (var message in response.Messages!)
        {
            if (message.Contents?.Count == 0)
                continue;

            foreach (var content in message.Contents!)
            {
                if (content is not FunctionCallContent functionCall)
                    continue;

                _logger.LogDebug("Found function call: {FunctionName} with {ArgCount} arguments",
                    functionCall.Name, functionCall.Arguments?.Count ?? 0);

                var unwrappedArguments = UnwrapFunctionArguments(functionCall.Arguments);

                return new AevatarFunctionCall
                {
                    Name = functionCall.Name,
                    Arguments = unwrappedArguments != null
                        ? JsonSerializer.Serialize(unwrappedArguments)
                        : "{}"
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Creates AevatarLLMResponse from Microsoft.Extensions.AI ChatResponse.
    /// </summary>
    private AevatarLLMResponse CreateAevatarLLMResponse(Microsoft.Extensions.AI.ChatResponse response)
    {
        var result = new AevatarLLMResponse
        {
            Content = response.Text,
            ModelName = response.ModelId ?? _config.Model,
            AevatarStopReason = AevatarStopReason.Complete,
            Usage = CreateTokenUsage(
                (int?)response.Usage?.InputTokenCount,
                (int?)response.Usage?.OutputTokenCount,
                (int?)response.Usage?.TotalTokenCount)
        };

        var functionCall = ProcessFunctionCalls(response);
        if (functionCall != null)
        {
            result.AevatarFunctionCall = functionCall;
            result.Content = string.Format(ToolConstants.FunctionCalledMessageFormat, functionCall.Name);
        }

        return result;
    }

    public override async IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(AevatarLLMRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating streaming response using MEAI provider: {Model}", _config.Model);

        var messages = BuildChatMessages(request);
        var options = BuildChatOptions(request);

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

    /// <summary>
    /// Extracts streaming text from chat update using reflection.
    /// Uses reflection to maintain resilience against Microsoft.Extensions.AI SDK changes.
    /// Attempts multiple property paths: TextDelta, Text, Message.Text, and Message.Content collection.
    /// Also extracts reasoning_content for reasoning models (e.g., DeepSeek-reasoner).
    /// </summary>
    private string ExtractStreamingText(object chatUpdate)
    {
        if (chatUpdate == null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        // ============================================================
        // DeepSeek-reasoner: Extract reasoning_content (thinking process)
        // ============================================================
        var reasoningContent = ExtractReasoningContent(chatUpdate);
        if (!string.IsNullOrEmpty(reasoningContent))
        {
            // Log reasoning content for debugging (optional)
            _logger.LogDebug("[REASONING] {Content}", reasoningContent);
            
            // Optionally include reasoning in output (wrapped for visibility)
            // Uncomment below to show thinking process in UI:
            // sb.Append($"💭 {reasoningContent}");
        }

        // ============================================================
        // Standard content extraction
        // ============================================================
        
        // Try TextDelta or Text properties directly
        var text = StreamingPropertyCache.GetValue(chatUpdate, "TextDelta") as string;
        if (string.IsNullOrEmpty(text))
        {
            text = StreamingPropertyCache.GetValue(chatUpdate, "Text") as string;
        }

        if (!string.IsNullOrEmpty(text))
        {
            sb.Append(text);
            return sb.ToString();
        }

        // Try Message.Text property
        var message = StreamingPropertyCache.GetValue(chatUpdate, "Message");
        if (message == null)
        {
            return sb.ToString();
        }

        text = StreamingPropertyCache.GetValue(message, "Text") as string;
        if (!string.IsNullOrEmpty(text))
        {
            sb.Append(text);
            return sb.ToString();
        }

        // Aggregate text from Message.Content collection
        var content = StreamingPropertyCache.GetValue(message, "Content") as System.Collections.IEnumerable;
        if (content == null)
        {
            return sb.ToString();
        }

        foreach (var part in content)
        {
            if (part == null) continue;
            var partText = StreamingPropertyCache.GetValue(part, "Text") as string;
            if (!string.IsNullOrEmpty(partText))
            {
                sb.Append(partText);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extract reasoning_content from DeepSeek-reasoner or similar reasoning models.
    /// The reasoning content represents the model's internal thinking process.
    /// </summary>
    private static string? ExtractReasoningContent(object chatUpdate)
    {
        // Try direct reasoning_content property (some SDKs expose this directly)
        var reasoning = StreamingPropertyCache.GetValue(chatUpdate, "ReasoningContent") as string;
        if (!string.IsNullOrEmpty(reasoning))
        {
            return reasoning;
        }

        // Try Delta.reasoning_content (DeepSeek API structure)
        var delta = StreamingPropertyCache.GetValue(chatUpdate, "Delta");
        if (delta != null)
        {
            reasoning = StreamingPropertyCache.GetValue(delta, "ReasoningContent") as string;
            if (!string.IsNullOrEmpty(reasoning))
            {
                return reasoning;
            }
            
            // Also try snake_case version
            reasoning = StreamingPropertyCache.GetValue(delta, "reasoning_content") as string;
            if (!string.IsNullOrEmpty(reasoning))
            {
                return reasoning;
            }
        }

        // Try Message.ReasoningContent
        var message = StreamingPropertyCache.GetValue(chatUpdate, "Message");
        if (message != null)
        {
            reasoning = StreamingPropertyCache.GetValue(message, "ReasoningContent") as string;
            if (!string.IsNullOrEmpty(reasoning))
            {
                return reasoning;
            }
        }

        return null;
    }
}