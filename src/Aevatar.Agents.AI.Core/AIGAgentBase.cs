using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Extensions;
using Aevatar.Agents.AI.Core.Messages;
// Models are now in Aevatar.Agents.AI namespace from protobuf
using Aevatar.Agents.Core;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

/// <summary>
/// Level 1: Basic AI Agent with chat capabilities using state-based conversation management.
/// 第一级：使用基于状态的对话管理的具有基础聊天能力的AI代理
/// </summary>
/// <typeparam name="TState">The agent state type (must extend or contain AevatarAIAgentState)</typeparam>
public abstract class AIGAgentBase<TState> : GAgentBase<TState>
    where TState : class, IMessage, new()
{
    #region Fields
    
    private readonly ILLMProvider _llmProvider;
    private AevatarAIAgentState? _aiState;
    
    #endregion
    
    #region Properties
    
    /// <summary>
    /// System prompt for the AI agent.
    /// AI代理的系统提示词
    /// </summary>
    public virtual string SystemPrompt { get; set; } = "You are a helpful AI assistant.";
    
    /// <summary>
    /// AI configuration.
    /// AI配置
    /// </summary>
    public AIAgentConfiguration Configuration { get; }
    
    /// <summary>
    /// Gets the LLM provider.
    /// 获取LLM提供商
    /// </summary>
    public ILLMProvider LLMProvider => _llmProvider;
    
    /// <summary>
    /// Gets the AI state. Must be overridden to provide state access.
    /// 获取AI状态。必须重写以提供状态访问
    /// </summary>
    protected virtual AevatarAIAgentState GetAIState()
    {
        // Try to get AI state from the agent state
        // This should be overridden in derived classes for proper state management
        if (_aiState == null)
        {
            _aiState = new AevatarAIAgentState
            {
                AgentId = Id.ToString(),
                AiConfig = new AevatarAIConfiguration
                {
                    Model = Configuration.Model,
                    MaxHistory = Configuration.MaxHistory,
                    Temperature = Configuration.Temperature,
                    MaxTokens = Configuration.MaxTokens,
                    SystemPrompt = SystemPrompt
                }
            };
        }
        return _aiState;
    }
    
    /// <summary>
    /// Gets the conversation history from AI state.
    /// 从AI状态获取对话历史
    /// </summary>
    public IList<AevatarChatMessage> ConversationHistory => GetAIState().ConversationHistory;
    
    #endregion
    
    #region Constructors
    
    /// <summary>
    /// Initializes a new instance of the AIGAgentBase class.
    /// 初始化AIGAgentBase类的新实例
    /// </summary>
    protected AIGAgentBase() : base()
    {
        Configuration = new AIAgentConfiguration();
        ConfigureAI(Configuration);
        
        _llmProvider = CreateLLMProvider();
        InitializeAIState();
        InitializeConversation();
    }
    
    /// <summary>
    /// Initializes a new instance with dependency injection.
    /// 使用依赖注入初始化新实例
    /// </summary>
    protected AIGAgentBase(
        ILLMProvider llmProvider,
        ILogger? logger = null) : base(logger)
    {
        _llmProvider = llmProvider ?? throw new ArgumentNullException(nameof(llmProvider));
        
        Configuration = new AIAgentConfiguration();
        ConfigureAI(Configuration);
        InitializeAIState();
        InitializeConversation();
    }
    
    #endregion
    
    #region Configuration Methods
    
    /// <summary>
    /// Configure AI settings. Override in derived classes.
    /// 配置AI设置。在派生类中重写
    /// </summary>
    protected virtual void ConfigureAI(AIAgentConfiguration config)
    {
        config.Model = "gpt-4";
        config.MaxHistory = 20;
        config.Temperature = 0.7;
        config.MaxTokens = 2000;
    }
    
    /// <summary>
    /// Creates the LLM provider. Override to customize.
    /// 创建LLM提供商。重写以自定义
    /// </summary>
    protected virtual ILLMProvider CreateLLMProvider()
    {
        // In production, this would be injected or created from configuration
        throw new NotImplementedException(
            "LLM Provider must be injected or CreateLLMProvider must be overridden");
    }
    
    /// <summary>
    /// Initialize AI state. Override to customize.
    /// 初始化AI状态。重写以自定义
    /// </summary>
    protected virtual void InitializeAIState()
    {
        var aiState = GetAIState();
        if (aiState.AiConfig == null)
        {
            aiState.AiConfig = new AevatarAIConfiguration
            {
                Model = Configuration.Model,
                MaxHistory = Configuration.MaxHistory,
                Temperature = Configuration.Temperature,
                MaxTokens = Configuration.MaxTokens,
                SystemPrompt = Configuration.SystemPrompt ?? SystemPrompt,
                    ProcessingMode = AevatarAIProcessingMode.Standard
            };
        }
    }
    
    /// <summary>
    /// Initialize conversation with system prompt if needed.
    /// 如果需要，使用系统提示词初始化对话
    /// </summary>
    protected virtual void InitializeConversation()
    {
        var aiState = GetAIState();
        // Add system prompt if conversation is empty and we have a prompt
        if (aiState.ConversationHistory.Count == 0 && !string.IsNullOrEmpty(SystemPrompt))
        {
            aiState.AddSystemMessage(SystemPrompt, Configuration.MaxHistory);
        }
    }
    
    #endregion
    
    #region Core Chat Abstraction
    
    /// <summary>
    /// Process a chat request.
    /// 处理聊天请求
    /// </summary>
    protected virtual async Task<Aevatar.Agents.AI.ChatResponse> ChatAsync(Aevatar.Agents.AI.ChatRequest request)
    {
        try
        {
            // Add user message to conversation history
            var aiState = GetAIState();
            aiState.AddUserMessage(request.Message, Configuration.MaxHistory);
            
            // Build LLM request
            var llmRequest = BuildLLMRequest(request);
            
            // Get response from LLM
            var llmResponse = await _llmProvider.GenerateAsync(llmRequest);
            
            // Add assistant response to history
            aiState.AddAssistantMessage(llmResponse.Content, Configuration.MaxHistory);
            
            // Update activity tracking
            aiState.LastActivity = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow);
            aiState.TotalTokensUsed += llmResponse.Usage?.TotalTokens ?? 0;
            
            // Build and return chat response
            return new Aevatar.Agents.AI.ChatResponse
            {
                Content = llmResponse.Content,
                Usage = ConvertTokenUsage(llmResponse.Usage),
                RequestId = request.RequestId
            };
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error processing chat request");
            throw;
        }
    }
    
    /// <summary>
    /// Build LLM request from chat request.
    /// 从聊天请求构建LLM请求
    /// </summary>
    protected virtual AevatarLLMRequest BuildLLMRequest(Aevatar.Agents.AI.ChatRequest request)
    {
        var llmRequest = new AevatarLLMRequest
        {
            SystemPrompt = SystemPrompt,
            UserPrompt = request.Message,
            Settings = GetLLMSettings(request)
        };
        
        // Add conversation history
        var aiState = GetAIState();
        if (aiState.ConversationHistory.Count > 0)
        {
            var history = aiState.GetRecentHistory(Configuration.MaxHistory);
            // History is from AI namespace (Protobuf), llmRequest expects Abstractions types
            // These are the same type, just different namespaces
            foreach (var item in history)
            {
                llmRequest.Messages.Add(item);
            }
        }
        
        // Add context from state if available
        if (aiState.Context != null && aiState.Context.Count > 0)
        {
            foreach (var kvp in aiState.Context)
            {
                llmRequest.Context[kvp.Key] = kvp.Value;
            }
        }
        
        return llmRequest;
    }
    
    /// <summary>
    /// Get LLM settings for the request.
    /// 获取请求的LLM设置
    /// </summary>
    protected virtual AevatarLLMSettings GetLLMSettings(Aevatar.Agents.AI.ChatRequest request)
    {
        return new AevatarLLMSettings
        {
            ModelId = Configuration.Model,
            Temperature = request.Temperature > 0 ? request.Temperature : Configuration.Temperature,
            MaxTokens = request.MaxTokens > 0 ? request.MaxTokens : Configuration.MaxTokens
        };
    }
    
    #endregion
    
    #region Event Handlers
    
    /// <summary>
    /// Handle chat request events.
    /// 处理聊天请求事件
    /// </summary>
    [EventHandlerAttribute]
    protected virtual async Task HandleChatRequestEvent(ChatRequestEvent evt)
    {
        var request = new Aevatar.Agents.AI.ChatRequest
        {
            Message = evt.Message,
            RequestId = evt.RequestId,
            // Context will be added below
        };
        
        // Add context items to the map
        foreach (var item in evt.Context)
        {
            request.Context[item.Key] = item.Value;
        };
        
        var response = await ChatAsync(request);
        
        await PublishAsync(new Aevatar.Agents.AI.Core.Messages.ChatResponseEvent
        {
            Content = response.Content,
            RequestId = evt.RequestId,
            TokensUsed = response.Usage?.TotalTokens ?? 0
        });
    }
    
    #endregion
    
    #region Helper Methods
    
    
    /// <summary>
    /// Convert metadata from protobuf map to dictionary.
    /// 将元数据从protobuf映射转换为字典
    /// </summary>
    private Dictionary<string, object>? ConvertMetadata(
        Google.Protobuf.Collections.MapField<string, string> metadata)
    {
        if (metadata == null || metadata.Count == 0)
            return null;
            
        var result = new Dictionary<string, object>();
        foreach (var kvp in metadata)
        {
            result[kvp.Key] = kvp.Value;
        }
        return result;
    }
    
    /// <summary>
    /// Convert protobuf map to dictionary context.
    /// 将protobuf映射转换为字典上下文
    /// </summary>
    private Dictionary<string, object> ConvertToContext(
        Google.Protobuf.Collections.MapField<string, string> protoContext)
    {
        var context = new Dictionary<string, object>();
        if (protoContext != null)
        {
            foreach (var kvp in protoContext)
            {
                context[kvp.Key] = kvp.Value;
            }
        }
        return context;
    }
    
    /// <summary>
    /// Convert LLM token usage to our token usage.
    /// 转换LLM令牌使用量
    /// </summary>
    private AevatarTokenUsage ConvertTokenUsage(AevatarTokenUsage? llmUsage)
    {
        if (llmUsage == null) return null;
        
        return new AevatarTokenUsage
        {
            PromptTokens = llmUsage.PromptTokens,
            CompletionTokens = llmUsage.CompletionTokens,
            TotalTokens = llmUsage.TotalTokens
        };
    }
    
    #endregion
}

/// <summary>
/// AI Agent configuration.
/// AI代理配置
/// </summary>
public class AIAgentConfiguration
{
    /// <summary>
    /// LLM provider instance.
    /// LLM提供商实例
    /// </summary>
    public ILLMProvider? LLMProvider { get; set; }
    
    /// <summary>
    /// Model to use.
    /// 要使用的模型
    /// </summary>
    public string Model { get; set; } = "gpt-4";
    
    /// <summary>
    /// System prompt.
    /// 系统提示词
    /// </summary>
    public string? SystemPrompt { get; set; }
    
    /// <summary>
    /// Maximum conversation history.
    /// 最大对话历史
    /// </summary>
    public int MaxHistory { get; set; } = 20;
    
    /// <summary>
    /// Default temperature.
    /// 默认温度
    /// </summary>
    public double Temperature { get; set; } = 0.7;
    
    /// <summary>
    /// Default max tokens.
    /// 默认最大令牌数
    /// </summary>
    public int MaxTokens { get; set; } = 2000;
}

/// <summary>
/// Interface for LLM providers.
/// LLM提供商接口
/// </summary>
public interface ILLMProvider
{
    /// <summary>
    /// Generate a response from the LLM.
    /// 从LLM生成响应
    /// </summary>
    Task<AevatarLLMResponse> GenerateAsync(AevatarLLMRequest request);
}