using Aevatar.Agents.AI.Core;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;

namespace Aevatar.Agents.AI.MEAI;

/// <summary>
/// Microsoft.Extensions.AI集成的Agent基类
/// 继承自简化的AIGAgentBase，使用IChatClient实现AI功能
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

    /// <summary>
    /// 对话消息历史（Microsoft.Extensions.AI格式）
    /// </summary>
    private readonly List<ChatMessage> _chatMessages = [];

    #region Constructors

    /// <summary>
    /// 使用IChatClient的构造函数
    /// </summary>
    protected MEAIGAgentBase(IChatClient chatClient, ILogger? logger = null)
        : base(logger)
    {
        ChatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));

        // 初始化系统提示词
        if (!string.IsNullOrEmpty(SystemPrompt))
        {
            _chatMessages.Add(new ChatMessage(ChatRole.System, SystemPrompt));
        }
    }

    protected override string SystemPrompt { get; } = "You are a helpful AI assistant.";

    /// <summary>
    /// 使用配置的构造函数
    /// </summary>
    protected MEAIGAgentBase(MEAIConfiguration config, ILogger? logger = null)
        : this(CreateChatClient(config, logger), logger)
    {
    }

    #endregion

    #region AI Implementation

    /// <summary>
    /// 实现内部的AI生成方法
    /// </summary>
    protected override async Task<string> InternalGenerateResponseAsync(
        string input,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        // 构建消息列表
        var messages = new List<ChatMessage>();

        // 如果提供了特定的系统提示词，使用它；否则使用已保存的消息历史
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new ChatMessage(ChatRole.System, systemPrompt));
            messages.Add(new ChatMessage(ChatRole.User, input));
        }
        else
        {
            // 使用现有的对话历史
            messages.AddRange(_chatMessages);
            messages.Add(new ChatMessage(ChatRole.User, input));
        }

        // 配置选项
        var options = new ChatOptions
        {
            Temperature = (float)Configuration.Temperature,
            MaxOutputTokens = Configuration.MaxTokens
        };

        // 如果有工具，添加到选项中
        if (AITools.Count > 0)
        {
            options.Tools = AITools;
        }

        // 调用ChatClient
        var response = await ChatClient.CompleteAsync(messages, options, cancellationToken);

        // 保存到对话历史
        if (string.IsNullOrEmpty(systemPrompt)) // 只在使用持续对话时保存
        {
            _chatMessages.Add(new ChatMessage(ChatRole.User, input));
            if (response.Message != null)
            {
                _chatMessages.Add(response.Message);
            }
        }

        return response.Message?.Text ?? string.Empty;
    }

    /// <summary>
    /// 实现流式生成
    /// </summary>
    protected override async IAsyncEnumerable<string> GenerateStreamingResponseAsync(
        string input,
        string? systemPrompt = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        // 构建消息列表
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new ChatMessage(ChatRole.System, systemPrompt));
            messages.Add(new ChatMessage(ChatRole.User, input));
        }
        else
        {
            messages.AddRange(_chatMessages);
            messages.Add(new ChatMessage(ChatRole.User, input));
        }

        // 配置选项
        var options = new ChatOptions
        {
            Temperature = (float)Configuration.Temperature,
            MaxOutputTokens = Configuration.MaxTokens
        };

        if (AITools.Count > 0)
        {
            options.Tools = AITools;
        }

        // 流式调用
        var responseBuilder = new System.Text.StringBuilder();

        await foreach (var update in ChatClient.CompleteStreamingAsync(messages, options, cancellationToken))
        {
            if (update.Text != null)
            {
                responseBuilder.Append(update.Text);
                yield return update.Text;
            }
        }

        // 保存完整响应到历史
        if (string.IsNullOrEmpty(systemPrompt) && responseBuilder.Length > 0)
        {
            _chatMessages.Add(new ChatMessage(ChatRole.User, input));
            _chatMessages.Add(new ChatMessage(ChatRole.Assistant, responseBuilder.ToString()));
        }
    }

    #endregion


    #region Tool Management

    /// <summary>
    /// 注册工具 - 简化版本
    /// </summary>
    protected override void RegisterTools()
    {
        // 让子类注册AITools
        RegisterAITools();
    }

    /// <summary>
    /// 注册Microsoft.Extensions.AI工具（子类重写）
    /// </summary>
    protected virtual void RegisterAITools()
    {
        // 子类实现，例如：
        // AITools.Add(AIFunctionFactory.Create(...));
    }


    #endregion


    #region Helper Methods

    /// <summary>
    /// 清空对话历史（覆盖基类方法）
    /// </summary>
    public override void ClearConversationHistory()
    {
        base.ClearConversationHistory();
        _chatMessages.Clear();

        // 重新添加系统提示词
        if (!string.IsNullOrEmpty(SystemPrompt))
        {
            _chatMessages.Add(new ChatMessage(ChatRole.System, SystemPrompt));
        }
    }

    /// <summary>
    /// 获取Microsoft.Extensions.AI格式的对话历史
    /// </summary>
    public IReadOnlyList<ChatMessage> GetChatMessages()
    {
        return _chatMessages.AsReadOnly();
    }

    /// <summary>
    /// 获取JSON类型字符串
    /// </summary>
    private static string GetJsonType(Type? type)
    {
        if (type == null) return "object";

        return Type.GetTypeCode(type) switch
        {
            TypeCode.String => "string",
            TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 => "integer",
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal => "number",
            TypeCode.Boolean => "boolean",
            _ when type.IsArray => "array",
            _ => "object"
        };
    }

    /// <summary>
    /// 类型转换
    /// </summary>
    private static object? ConvertToType(object value, Type targetType)
    {
        if (value == null) return null;
        if (targetType.IsInstanceOfType(value)) return value;

        try
        {
            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            return value;
        }
    }

    /// <summary>
    /// 创建ChatClient
    /// </summary>
    private static IChatClient CreateChatClient(MEAIConfiguration config, ILogger? logger)
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