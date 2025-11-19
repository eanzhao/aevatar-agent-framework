using System.Runtime.CompilerServices;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.StateProtection;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

/// <summary>
/// Level 1: Basic AI Agent with chat capabilities using state-based conversation management.
/// 第一级：使用基于状态的对话管理的具有基础聊天能力的AI代理
/// </summary>
/// <typeparam name="TState">The business state type (defined by the developer using protobuf)</typeparam>
/// <typeparam name="TConfig">The configuration type (must be protobuf type)</typeparam>
public abstract class AIGAgentBase<TState, TConfig> : GAgentBase<TState, TConfig>
    where TState : class, IMessage<TState>, new()
    where TConfig : class, IMessage<TConfig>, new()
{
    #region Fields

    private IAevatarLLMProvider? _llmProvider;
    private bool _isInitialized;
    protected ILLMProviderFactory LLMProviderFactory { get; set; }

    #endregion

    public AIGAgentBase()
    {
    }

    public AIGAgentBase(Guid id) : base(id)
    {
    }

    #region Properties

    /// <summary>
    /// System prompt for the AI agent.
    /// AI代理的系统提示词
    /// </summary>
    public virtual string SystemPrompt { get; set; } = "You are a helpful AI assistant.";

    /// <summary>
    /// Gets the LLM provider.
    /// 获取LLM提供商
    /// </summary>
    public IAevatarLLMProvider LLMProvider
    {
        get
        {
            if (!_isInitialized)
                throw new InvalidOperationException(
                    "AI Agent must be initialized before use. Call InitializeAsync() first.");
            return _llmProvider!;
        }
    }

    /// <summary>
    /// Gets the AI configuration (AevatarAIAgentConfiguration is stored in State).
    /// 获取AI配置（AevatarAIAgentConfiguration存储在State中）
    /// </summary>
    public AevatarAIAgentConfiguration Configuration { get; protected set; } = new();

    #endregion

    #region Initialization - Version 1: Use configured LLM Provider

    /// <summary>
    /// Initialize the AI agent with a named LLM provider from ASP.NET Options.
    /// This method must be called before using the agent.
    /// 使用appsettings.json中配置的LLM提供商初始化AI代理。必须在调用前初始化。
    /// </summary>
    /// <param name="providerName">Name of the LLM provider from appsettings.json (e.g., "openai-gpt4", "azure-gpt35")</param>
    /// <param name="configureAI">Optional configuration action for AI settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task InitializeAsync(
        string providerName,
        Action<AevatarAIAgentConfiguration>? configureAI = null,
        CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return;

        // Use InitializationScope to allow State and Config modifications during initialization
        using var initScope = StateProtectionContext.BeginInitializationScope();
        
        // 1. Load state and config if stores are available
        if (StateStore != null)
        {
            State = await StateStore.LoadAsync(Id, cancellationToken) ?? new TState();
        }

        if (ConfigStore != null)
        {
            var agentType = GetType();
            var savedConfig = await ConfigStore.LoadAsync(agentType, Id, cancellationToken);
            if (savedConfig != null)
            {
                Config = savedConfig;
            }
        }

        // 2. Configure AI settings
        ConfigureAI(Configuration);
        configureAI?.Invoke(Configuration);

        // 3. Configure custom settings
        ConfigureCustom(Config);

        // 4. Create LLM Provider from factory using provider name
        _llmProvider = await CreateLLMProviderFromFactoryAsync(providerName, cancellationToken);

        _isInitialized = true;

        Logger.LogInformation("AI Agent {AgentId} initialized with LLM provider '{ProviderName}'", Id, providerName);
    }

    #endregion

    #region Initialization - Version 2: Use custom LLM Provider config

    /// <summary>
    /// Initialize the AI agent with custom LLM provider configuration.
    /// This method must be called before using the agent.
    /// 使用自定义LLM提供商配置初始化AI代理。必须在调用前初始化。
    /// </summary>
    /// <param name="providerConfig">Custom LLM provider configuration</param>
    /// <param name="configureAI">Optional configuration action for AI settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task InitializeAsync(
        LLMProviderConfig providerConfig,
        Action<AevatarAIAgentConfiguration>? configureAI = null,
        CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return;

        // Use InitializationScope to allow State and Config modifications during initialization
        using var initScope = StateProtectionContext.BeginInitializationScope();
        
        // 1. Load state and config if stores are available
        if (StateStore != null)
        {
            State = await StateStore.LoadAsync(Id, cancellationToken) ?? new TState();
        }

        if (ConfigStore != null)
        {
            var agentType = GetType();
            var savedConfig = await ConfigStore.LoadAsync(agentType, Id, cancellationToken);
            if (savedConfig != null)
            {
                Config = savedConfig;
            }
        }

        // 2. Configure AI settings
        ConfigureAI(Configuration);
        configureAI?.Invoke(Configuration);

        // 3. Configure custom settings
        ConfigureCustom(Config);

        // 4. Create LLM Provider from custom config
        _llmProvider = await CreateLLMProviderFromConfigAsync(providerConfig, cancellationToken);

        _isInitialized = true;

        Logger.LogInformation("AI Agent {AgentId} initialized with custom LLM provider '{ProviderType}'",
            Id, providerConfig.ProviderType);
    }

    #endregion

    #region LLM Provider Creation

    /// <summary>
    /// Creates LLM Provider from factory using provider name.
    /// 使用提供商名称从工厂创建LLM Provider
    /// </summary>
    protected virtual async Task<IAevatarLLMProvider> CreateLLMProviderFromFactoryAsync(
        string providerName,
        CancellationToken cancellationToken)
    {
        if (LLMProviderFactory == null)
        {
            throw new InvalidOperationException(
                "ILLMProviderFactory is not available. " +
                "Use the constructor that accepts ILLMProviderFactory or override this method.");
        }

        // Get provider from factory
        return await LLMProviderFactory.GetProviderAsync(providerName, cancellationToken);
    }

    /// <summary>
    /// Creates LLM Provider from custom configuration.
    /// 从自定义配置创建LLM Provider
    /// </summary>
    protected virtual async Task<IAevatarLLMProvider> CreateLLMProviderFromConfigAsync(
        LLMProviderConfig providerConfig,
        CancellationToken cancellationToken)
    {
        if (LLMProviderFactory == null)
        {
            throw new InvalidOperationException(
                "ILLMProviderFactory is not available. " +
                "Use the constructor that accepts ILLMProviderFactory or override this method.");
        }

        // Create provider from config using factory
        return LLMProviderFactory.CreateProvider(providerConfig, cancellationToken);
    }

    #endregion

    #region Configuration Methods

    /// <summary>
    /// Configure AI settings. Override in derived classes.
    /// 配置AI设置。在派生类中重写
    /// </summary>
    protected virtual void ConfigureAI(AevatarAIAgentConfiguration config)
    {
        // Set defaults
        config.Model = "gpt-4";
        config.Temperature = 0.7;
        config.MaxTokens = 2000;
        config.MaxHistory = 20;

        // Override in derived classes
    }

    /// <summary>
    /// Configure custom settings. Override in derived classes.
    /// 配置自定义设置。在派生类中重写
    /// </summary>
    protected virtual void ConfigureCustom(TConfig config)
    {
        // Override in derived classes to configure custom settings
    }

    #endregion

    #region Chat Methods

    /// <summary>
    /// Process a chat request and return a response.
    /// 处理聊天请求并返回响应
    /// </summary>
    /// <param name="request">Chat request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Chat response</returns>
    public virtual async Task<ChatResponse> ChatAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            throw new InvalidOperationException(
                "AI Agent must be initialized before use. Call InitializeAsync() first.");

        try
        {
            // Build LLM request from chat request
            var llmRequest = BuildLLMRequest(request);

            // Call LLM
            var llmResponse = await LLMProvider.GenerateAsync(llmRequest, cancellationToken);

            // Build chat response
            var response = new ChatResponse
            {
                Content = llmResponse.Content,
                RequestId = request.RequestId
            };

            // Add token usage if available
            if (llmResponse.Usage != null)
            {
                response.Usage = new AevatarTokenUsage
                {
                    PromptTokens = llmResponse.Usage.PromptTokens,
                    CompletionTokens = llmResponse.Usage.CompletionTokens,
                    TotalTokens = llmResponse.Usage.TotalTokens
                };
            }

            // Publish chat response event
            await PublishAsync(new ChatResponseEvent
            {
                RequestId = request.RequestId,
                Content = response.Content,
                TokensUsed = response.Usage?.TotalTokens ?? 0,
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            }, ct: cancellationToken);

            return response;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing chat request {RequestId}", request.RequestId);
            throw;
        }
    }

    /// <summary>
    /// Build LLM request from chat request.
    /// 从聊天请求构建 LLM 请求
    /// </summary>
    protected virtual AevatarLLMRequest BuildLLMRequest(
        ChatRequest request)
    {
        return new AevatarLLMRequest
        {
            SystemPrompt = SystemPrompt,
            Messages = new List<AevatarChatMessage>
            {
                new()
                {
                    Role = AevatarChatRole.User,
                    Content = request.Message
                }
            },
            Settings = GetLLMSettings(request)
        };
    }

    /// <summary>
    /// Get LLM settings from chat request.
    /// 从聊天请求获取 LLM 设置
    /// </summary>
    protected virtual AevatarLLMSettings GetLLMSettings(Aevatar.Agents.AI.ChatRequest request)
    {
        // Use request values if provided (considering 0 as a valid temperature), otherwise use configuration
        // For temperature: accept any value >= 0 as valid override
        // For maxTokens: only positive values are valid overrides
        double temperature = request.Temperature >= 0 ? request.Temperature : Configuration.Temperature;
        int maxTokens = request.MaxTokens > 0 ? request.MaxTokens : Configuration.MaxTokens;

        return new AevatarLLMSettings
        {
            Temperature = temperature,
            MaxTokens = maxTokens,
            ModelId = Configuration.Model
        };
    }

    /// <summary>
    /// Create a chat request with the given message.
    /// 使用给定的消息创建聊天请求
    /// </summary>
    public virtual ChatRequest CreateChatRequest(string message)
    {
        return ChatRequest.Create(message);
    }

    /// <summary>
    /// Generate a response to a message (convenience method).
    /// 生成消息的响应（便捷方法）
    /// </summary>
    public virtual Task<ChatResponse> GenerateResponseAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        var request = CreateChatRequest(message);
        return ChatAsync(request, cancellationToken);
    }

    /// <summary>
    /// Generate a streaming response to a chat request.
    /// 生成聊天请求的流式响应
    /// </summary>
    public virtual async IAsyncEnumerable<string> ChatStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            throw new InvalidOperationException(
                "AI Agent must be initialized before use. Call InitializeAsync() first.");

        // Build LLM request
        var llmRequest = BuildLLMRequest(request);

        // Stream from LLM
        var enumerator = LLMProvider.GenerateStreamAsync(llmRequest, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                string? content = null;
                bool hasNext;
                bool isComplete = false;

                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                    if (!hasNext) break;

                    var token = enumerator.Current;
                    content = token.Content;
                    isComplete = token.IsComplete;
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Error in streaming chat request {RequestId}", request.RequestId);
                    throw;
                }

                if (!string.IsNullOrEmpty(content))
                {
                    yield return content;
                }

                if (isComplete)
                    break;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    /// <summary>
    /// Generate a streaming response to a message (convenience method).
    /// 生成消息的流式响应（便捷方法）
    /// </summary>
    public virtual IAsyncEnumerable<string> GenerateResponseStreamAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        var request = CreateChatRequest(message);
        return ChatStreamAsync(request, cancellationToken);
    }

    /// <summary>
    /// Check if the LLM provider supports streaming.
    /// 检查 LLM 提供商是否支持流式响应
    /// </summary>
    public virtual async Task<bool> SupportsStreamingAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            return false;

        var modelInfo = await LLMProvider.GetModelInfoAsync(cancellationToken);
        return modelInfo.SupportsStreaming;
    }

    #endregion
}