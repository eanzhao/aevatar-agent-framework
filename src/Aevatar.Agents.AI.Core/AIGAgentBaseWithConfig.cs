using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.Core;
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

    #endregion

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
                throw new InvalidOperationException("AI Agent must be initialized before use. Call InitializeAsync() first.");
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

        // 1. Load state and config if stores are available
        if (StateStore != null)
        {
            State = await StateStore.LoadAsync(Id, cancellationToken) ?? new TState();
        }

        if (ConfigStore != null)
        {
            var savedConfig = await ConfigStore.LoadAsync(Id, cancellationToken);
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

        // 1. Load state and config if stores are available
        if (StateStore != null)
        {
            State = await StateStore.LoadAsync(Id, cancellationToken) ?? new TState();
        }

        if (ConfigStore != null)
        {
            var savedConfig = await ConfigStore.LoadAsync(Id, cancellationToken);
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
        // This requires ILLMProviderFactory to be available
        // For now, throw to require implementation
        throw new NotImplementedException(
            "LLM Provider factory creation must be implemented. " +
            "Override CreateLLMProviderFromFactoryAsync or ensure ILLMProviderFactory is available.");
    }

    /// <summary>
    /// Creates LLM Provider from custom configuration.
    /// 从自定义配置创建LLM Provider
    /// </summary>
    protected virtual async Task<IAevatarLLMProvider> CreateLLMProviderFromConfigAsync(
        LLMProviderConfig providerConfig,
        CancellationToken cancellationToken)
    {
        // This requires ILLMProviderFactory or direct creation
        // For now, throw to require implementation
        throw new NotImplementedException(
            "LLM Provider creation from config must be implemented. " +
            "Override CreateLLMProviderFromConfigAsync.");
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
}