using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Microsoft.Extensions.AI;

namespace Aevatar.Agents.AI.MEAI;

/// <summary>
/// Microsoft.Extensions.AI LLM Provider实现
/// 桥接Microsoft.Extensions.AI的IChatClient到框架的IAevatarLLMProvider
/// </summary>
internal class MEAILLMProvider : ILLMProvider
{
    private IChatClient? _chatClient;
    private readonly List<ChatMessage> _conversationHistory = new();
    
    /// <summary>
    /// 设置ChatClient
    /// </summary>
    public void SetChatClient(IChatClient chatClient)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    }
    

    /// <inheritdoc />
    public async Task<AevatarLLMResponse> GenerateAsync(AevatarLLMRequest request)
    {
        if (_chatClient == null)
        {
            throw new InvalidOperationException("ChatClient not set. Call SetChatClient first.");
        }
        
        // Convert request to MEAI format
        var messages = new List<ChatMessage>();
        
        // Add system prompt if provided
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new ChatMessage(ChatRole.System, request.SystemPrompt));
        }
        
        // Add conversation history
        foreach (var msg in request.Messages)
        {
            var role = ConvertToMEAIRole(msg.Role);
            messages.Add(new ChatMessage(role, msg.Content));
        }
        
        // Add user prompt
        if (!string.IsNullOrEmpty(request.UserPrompt))
        {
            messages.Add(new ChatMessage(ChatRole.User, request.UserPrompt));
        }
        
        // Configure options
        var options = new ChatOptions
        {
            Temperature = (float)(request.Settings?.Temperature ?? 0.7),
            MaxOutputTokens = request.Settings?.MaxTokens
        };
        
        // Add functions/tools if available
        if (request.Functions?.Count > 0)
        {
            // Convert functions to AITools
            var tools = new List<AITool>();
            foreach (var func in request.Functions)
            {
                tools.Add(ConvertToAITool(func));
            }
            options.Tools = tools;
        }
        
        // Call the chat client
        var response = await _chatClient.GetResponseAsync(messages, options);
        
        // Convert response
        var result = new AevatarLLMResponse
        {
            Content = response.Text ?? string.Empty
        };
        
        // Check for function calls in the messages
        var lastMessage = response.Messages?.LastOrDefault();
        if (lastMessage?.Contents?.Any(c => c is FunctionCallContent) == true)
        {
            var functionCall = lastMessage.Contents
                .OfType<FunctionCallContent>()
                .FirstOrDefault();
            
            if (functionCall != null)
            {
                result.AevatarFunctionCall = new AevatarFunctionCall
                {
                    Name = functionCall.CallId,
                    Arguments = functionCall.Arguments?.ToString() ?? "{}"
                };
            }
        }
        
        // Set usage if available
        if (response.Usage != null)
        {
            result.Usage = new AevatarTokenUsage
            {
                PromptTokens = (int)(response.Usage.InputTokenCount ?? 0),
                CompletionTokens = (int)(response.Usage.OutputTokenCount ?? 0),
                TotalTokens = (int)(response.Usage.TotalTokenCount ?? 0)
            };
        }
        
        return result;
    }

    
    private ChatRole ConvertToMEAIRole(AevatarChatRole role)
    {
        return role switch
        {
            AevatarChatRole.System => ChatRole.System,
            AevatarChatRole.User => ChatRole.User,
            AevatarChatRole.Assistant => ChatRole.Assistant,
            AevatarChatRole.Function => ChatRole.Tool,
            _ => ChatRole.User
        };
    }
    
    private AITool ConvertToAITool(AevatarFunctionDefinition func)
    {
        // Create a simple AITool from function definition
        // This is a simplified conversion - real implementation might need more details
        Func<Dictionary<string, object?>, Task<object>> handler = async (args) =>
        {
            // This is just a placeholder - actual execution would be handled elsewhere
            return $"Function {func.Name} called with {args.Count} arguments";
        };
        
        return AIFunctionFactory.Create(handler);
    }
}
