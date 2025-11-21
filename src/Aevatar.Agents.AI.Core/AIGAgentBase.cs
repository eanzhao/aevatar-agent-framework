using System.Runtime.CompilerServices;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.StateProtection;
using Microsoft.Extensions.Logging;

// ReSharper disable InconsistentNaming
namespace Aevatar.Agents.AI.Core;

/// <summary>
/// Layer 0: Core AI Agent Base
/// - Manages LLM interactions
/// - Manages Standard State (History, Token Usage, etc)
/// - Manages Standard Config
/// </summary>
public abstract class AIGAgentBase : GAgentBase<AevatarAIAgentState, AevatarAIAgentConfig>
{
    #region Fields

    protected IAevatarLLMProvider? _llmProvider;
    protected bool _isInitialized;
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
    /// </summary>
    public virtual string SystemPrompt { get; set; } = "You are a helpful AI assistant.";

    /// <summary>
    /// Gets the LLM provider.
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

    #endregion

    #region Initialization

    /// <summary>
    /// Helper method to load and configure state and configuration during initialization.
    /// Must be called within an InitializationScope.
    /// </summary>
    protected virtual async Task InitializeStateAndConfigAsync(
        Action<AevatarAIAgentConfig>? configAI,
        CancellationToken cancellationToken)
    {
        await ActivateAsync();
        
        // Load state and config if stores are available
        if (StateStore != null)
        {
            State = await StateStore.LoadAsync(Id, cancellationToken) ?? new AevatarAIAgentState();
        }

        var agentType = GetType();
        if (ConfigStore != null)
        {
            Config = await ConfigStore.LoadAsync(agentType, Id, cancellationToken) ?? new AevatarAIAgentConfig();
        }

        // Configure AI settings
        ConfigAI(Config);
        configAI?.Invoke(Config);

        if (ConfigStore != null)
        {
            await ConfigStore.SaveAsync(agentType, Id, Config, cancellationToken);
        }
    }

    /// <summary>
    /// Initialize the AI agent with a named LLM provider from ASP.NET Options.
    /// This method must be called before using the agent.
    /// </summary>
    /// <param name="providerName">Name of the LLM provider from appsettings.json (e.g., "openai-gpt4", "azure-gpt35")</param>
    /// <param name="configAI">Optional configuration action for AI settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task InitializeAsync(
        string providerName,
        Action<AevatarAIAgentConfig>? configAI = null,
        CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return;

        // Use InitializationScope to allow State and Config modifications during initialization
        using var initScope = StateProtectionContext.BeginInitializationScope();

        await InitializeStateAndConfigAsync(configAI, cancellationToken);

        // Create LLM Provider from factory using provider name
        _llmProvider = await CreateLLMProviderFromFactoryAsync(providerName, cancellationToken);

        _isInitialized = true;

        Logger.LogInformation("AI Agent {AgentId} initialized with LLM provider '{ProviderName}'", Id, providerName);
    }

    /// <summary>
    /// Initialize the AI agent with custom LLM provider configuration.
    /// This method must be called before using the agent.
    /// </summary>
    /// <param name="providerConfig">Custom LLM provider configuration</param>
    /// <param name="configAI">Optional configuration action for AI settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public virtual async Task InitializeAsync(
        LLMProviderConfig providerConfig,
        Action<AevatarAIAgentConfig>? configAI = null,
        CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return;

        // Use InitializationScope to allow State and Config modifications during initialization
        using var initScope = StateProtectionContext.BeginInitializationScope();

        await InitializeStateAndConfigAsync(configAI, cancellationToken);

        // Create LLM Provider from custom config
        _llmProvider = await CreateLLMProviderFromConfigAsync(providerConfig, cancellationToken);

        _isInitialized = true;

        Logger.LogInformation("AI Agent {AgentId} initialized with custom LLM provider '{ProviderType}'",
            Id, providerConfig.ProviderType);
    }

    #endregion

    #region LLM Provider Creation

    /// <summary>
    /// Creates LLM Provider from factory using provider name.
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
    /// </summary>
    protected virtual void ConfigAI(AevatarAIAgentConfig config)
    {
        // Set defaults
        config.Model = "gpt-5";
        config.Temperature = 0.7f;
        config.MaxOutputTokens = 2000;

        // Override in derived classes
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
    protected virtual AevatarLLMSettings GetLLMSettings(ChatRequest request)
    {
        // Use request values if provided (considering 0 as a valid temperature), otherwise use configuration
        // For temperature: accept any value >= 0 as valid override
        // For maxTokens: only positive values are valid overrides
        var temperature = request.Temperature >= 0 ? request.Temperature : Config.Temperature;
        var maxTokens = request.MaxTokens > 0 ? request.MaxTokens : Config.MaxOutputTokens;

        return new AevatarLLMSettings
        {
            Temperature = temperature,
            MaxTokens = maxTokens,
            ModelId = Config.Model
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
                string? content;
                bool isComplete;

                try
                {
                    var hasNext = await enumerator.MoveNextAsync();
                    if (!hasNext) break;

                    var token = enumerator.Current;
                    content = token.Content;
                    isComplete = token.IsComplete;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error in streaming chat request {RequestId}", request.RequestId);
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