using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Tools;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Extensions;
using Azure.AI.OpenAI;
using Google.Protobuf;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Aevatar.Agents.AI.MEAI;

/// <summary>
/// Microsoft.Extensions.AI集成的Agent基类
/// 继承自AIGAgentBase，使用IChatClient实现AI功能
/// 使用基于状态的对话管理
/// </summary>
// ReSharper disable InconsistentNaming
public abstract class MEAIGAgentBase<TState> : AIGAgentBase<TState>
    where TState : class, IMessage, new()
{
    /// <summary>
    /// Microsoft.Extensions.AI ChatClient
    /// </summary>
    protected IChatClient ChatClient { get; }

    /// <summary>
    /// 注册的AITools
    /// </summary>
    protected List<AITool> AITools { get; } = [];

    #region Constructors

    /// <summary>
    /// 使用IChatClient的构造函数
    /// </summary>
    protected MEAIGAgentBase(IChatClient chatClient, ILogger<MEAIGAgentBase<TState>>? logger = null)
        : base()
    {
        ChatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        
        // Override the LLM provider with MEAI implementation
        var meaiProvider = new MEAILLMProvider();
        meaiProvider.SetChatClient(chatClient);
        SetLLMProvider(meaiProvider);
    }

    /// <summary>
    /// 系统提示词
    /// </summary>
    public override string SystemPrompt => "You are a helpful AI assistant.";

    /// <summary>
    /// 使用配置的构造函数
    /// </summary>
    protected MEAIGAgentBase(MEAIConfiguration config, ILogger<MEAIGAgentBase<TState>>? logger = null)
        : this(CreateChatClient(config), logger)
    {
        // Apply configuration
        Configuration.Model = config.Model ?? "gpt-4";
        Configuration.Temperature = config.Temperature;
        Configuration.MaxTokens = config.MaxTokens;
    }

    /// <summary>
    /// 创建LLM Provider - 重写基类方法
    /// </summary>
    protected override ILLMProvider CreateLLMProvider()
    {
        // This will be replaced in constructor
        return new MEAILLMProvider();
    }
    
    /// <summary>
    /// 设置LLM Provider
    /// </summary>
    private void SetLLMProvider(ILLMProvider provider)
    {
        var fieldInfo = typeof(AIGAgentBase<TState>).GetField("_llmProvider", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        fieldInfo?.SetValue(this, provider);
    }

    #endregion


    #region Tool Management

    /// <summary>
    /// 注册Microsoft.Extensions.AI工具（子类重写）
    /// </summary>
    protected virtual void RegisterMEAITools()
    {
        // 子类实现，例如：
        // AITools.Add(AIFunctionFactory.Create(...));
    }

    #endregion


    #region Helper Methods

    /// <summary>
    /// 获取Microsoft.Extensions.AI格式的对话历史
    /// </summary>
    public IReadOnlyList<ChatMessage> GetChatMessages()
    {
        var aiState = GetAIState();
        var history = aiState.ConversationHistory;
        var messages = new List<ChatMessage>();
        
        foreach (var msg in history)
        {
            var role = msg.Role switch
            {
                Aevatar.Agents.AI.AevatarChatRole.System => ChatRole.System,
                Aevatar.Agents.AI.AevatarChatRole.User => ChatRole.User,
                Aevatar.Agents.AI.AevatarChatRole.Assistant => ChatRole.Assistant,
                Aevatar.Agents.AI.AevatarChatRole.Function => ChatRole.Tool,
                _ => ChatRole.User
            };
            messages.Add(new ChatMessage(role, msg.Content));
        }
        
        return messages.AsReadOnly();
    }


    /// <summary>
    /// 创建ChatClient
    /// </summary>
    private static IChatClient CreateChatClient(MEAIConfiguration config)
    {
        if (config.ChatClient != null)
        {
            return config.ChatClient;
        }

        return config.Provider?.ToLowerInvariant() switch
        {
            "azure" or "azureopenai" => CreateAzureOpenAIChatClient(config),
            "openai" => CreateOpenAIChatClient(config),
            _ => throw new NotSupportedException($"Provider {config.Provider} is not supported")
        };
    }

    private static IChatClient CreateAzureOpenAIChatClient(MEAIConfiguration config)
    {
        if (string.IsNullOrEmpty(config.Endpoint))
        {
            throw new ArgumentException("Azure endpoint is required");
        }

        AzureOpenAIClient azureClient;

        if (config.UseAzureCliAuth)
        {
            azureClient = new AzureOpenAIClient(
                new Uri(config.Endpoint),
                new Azure.Identity.AzureCliCredential());
        }
        else if (!string.IsNullOrEmpty(config.ApiKey))
        {
            azureClient = new AzureOpenAIClient(
                new Uri(config.Endpoint),
                new Azure.AzureKeyCredential(config.ApiKey));
        }
        else
        {
            azureClient = new AzureOpenAIClient(
                new Uri(config.Endpoint),
                new Azure.Identity.DefaultAzureCredential());
        }

        var deploymentName = config.DeploymentName ?? config.Model ?? "gpt-4";
        return azureClient.AsChatClient(deploymentName);
    }

    private static IChatClient CreateOpenAIChatClient(MEAIConfiguration config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            throw new ArgumentException("OpenAI API key is required");
        }

        var openAIClient = new OpenAIClient(config.ApiKey);
        var model = config.Model ?? "gpt-4";
        return openAIClient.AsChatClient(model);
    }

    #endregion
}