using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Strategies;
using Aevatar.Agents.AI.Core.Extensions;
using Aevatar.Agents.AI.Core.Messages;
// Models are now in Aevatar.Agents.AI namespace from protobuf
using Aevatar.Agents.AI.Core.Strategies;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

/// <summary>
/// Level 3: AI Agent with advanced processing strategies.
/// 第三级：具有高级处理策略的AI代理
/// </summary>
/// <typeparam name="TState">The agent state type (must be Protobuf)</typeparam>
public abstract class AIGAgentWithProcessStrategy<TState> : AIGAgentWithToolBase<TState>
    where TState : class, IMessage, new()
{
    #region Fields

    private readonly Dictionary<string, IAevatarAIProcessingStrategy> _strategies;
    private readonly IAevatarAIProcessingStrategyFactory _strategyFactory;

    #endregion

    #region Properties

    /// <summary>
    /// Gets available processing strategies.
    /// 获取可用的处理策略
    /// </summary>
    protected IReadOnlyDictionary<string, IAevatarAIProcessingStrategy> Strategies => _strategies;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the AIGAgentWithProcessStrategy class.
    /// 初始化AIGAgentWithProcessStrategy类的新实例
    /// </summary>
    protected AIGAgentWithProcessStrategy() : base()
    {
        _strategyFactory = CreateStrategyFactory();
        _strategies = InitializeStrategies();
    }

    /// <summary>
    /// Initializes a new instance with dependency injection.
    /// 使用依赖注入初始化新实例
    /// </summary>
    protected AIGAgentWithProcessStrategy(
        IAevatarLLMProvider llmProvider,
        IAevatarToolManager toolManager,
        IAevatarAIProcessingStrategyFactory strategyFactory,
        ILogger? logger = null)
        : base(llmProvider, toolManager, logger)
    {
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
        _strategies = InitializeStrategies();
    }

    #endregion

    #region Strategy Management

    /// <summary>
    /// Initialize processing strategies.
    /// 初始化处理策略
    /// </summary>
    protected virtual Dictionary<string, IAevatarAIProcessingStrategy> InitializeStrategies()
    {
        return new Dictionary<string, IAevatarAIProcessingStrategy>
        {
            ["standard"] = _strategyFactory.GetOrCreateStrategy(AevatarAIProcessingMode.Standard),
            ["chain-of-thought"] = _strategyFactory.GetOrCreateStrategy(AevatarAIProcessingMode.ChainOfThought),
            ["react"] = _strategyFactory.GetOrCreateStrategy(AevatarAIProcessingMode.ReAct),
            ["tree-of-thoughts"] = _strategyFactory.GetOrCreateStrategy(AevatarAIProcessingMode.TreeOfThoughts)
        };
    }

    /// <summary>
    /// Create the strategy factory. Override to customize.
    /// 创建策略工厂。重写以自定义
    /// </summary>
    protected virtual IAevatarAIProcessingStrategyFactory CreateStrategyFactory()
    {
        return new AevatarAIProcessingStrategyFactory(null, null);
    }

    /// <summary>
    /// Select the appropriate strategy for a request.
    /// 为请求选择适当的策略
    /// </summary>
    protected virtual string SelectStrategy(Aevatar.Agents.AI.ChatRequest request)
    {
        // Check if strategy is specified in context
        if (request.Context?.ContainsKey("strategy") == true)
        {
            var requestedStrategy = request.Context["strategy"]?.ToString();
            if (!string.IsNullOrEmpty(requestedStrategy) && _strategies.ContainsKey(requestedStrategy))
            {
                return requestedStrategy;
            }
        }

        // Auto-detect based on message characteristics
        if (RequiresDeepReasoning(request.Message))
        {
            return "tree-of-thoughts";
        }

        if (RequiresStepByStepThinking(request.Message))
        {
            return "chain-of-thought";
        }

        if (RequiresToolInteraction(request.Message))
        {
            return "react";
        }

        return "standard";
    }

    #endregion

    #region Chat with Strategy Processing

    /// <summary>
    /// Override chat to use processing strategies.
    /// 重写聊天以使用处理策略
    /// </summary>
    protected override async Task<Aevatar.Agents.AI.ChatResponse> ChatAsync(Aevatar.Agents.AI.ChatRequest request)
    {
        try
        {
            var strategyName = SelectStrategy(request);
            Logger?.LogDebug("Selected strategy: {Strategy} for request: {RequestId}",
                strategyName, request.RequestId);

            if (!_strategies.TryGetValue(strategyName, out var strategy))
            {
                // Fallback to base implementation if strategy not found
                Logger?.LogWarning("Strategy {Strategy} not found, falling back to standard",
                    strategyName);
                return await base.ChatAsync(request);
            }

            // Create AI context for strategy
            var aiContext = CreateAIContext(request);

            // Create strategy dependencies
            var dependencies = CreateStrategyDependencies();

            // Validate strategy requirements
            if (!strategy.ValidateRequirements(dependencies))
            {
                Logger?.LogWarning("Strategy {Strategy} requirements not met, falling back",
                    strategyName);
                return await base.ChatAsync(request);
            }

            // Process with strategy
            var result = await strategy.ProcessAsync(
                aiContext,
                null, // Event handler config if needed
                dependencies);

            // Convert strategy result to chat response
            return ConvertStrategyResult(result, request, strategyName);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error processing chat with strategy");
            throw;
        }
    }

    /// <summary>
    /// Create AI context from chat request.
    /// 从聊天请求创建AI上下文
    /// </summary>
    private Aevatar.Agents.AI.AevatarAIContext CreateAIContext(Aevatar.Agents.AI.ChatRequest request)
    {
        var context = new Aevatar.Agents.AI.AevatarAIContext
        {
            AgentId = Id.ToString(),
            Question = request.Message,
            SystemPrompt = SystemPrompt
        };

        // Add conversation history
        context.ConversationHistory.AddRange(ConvertToConversationEntries());

        // Add metadata
        foreach (var item in request.Context)
        {
            context.Metadata[item.Key] = item.Value;
        }

        return context;
    }

    /// <summary>
    /// Create strategy dependencies.
    /// 创建策略依赖项
    /// </summary>
    private AevatarAIStrategyDependencies CreateStrategyDependencies()
    {
        return new AevatarAIStrategyDependencies
        {
            LLMProvider = ConvertToAevatarLLMProvider(),
            PromptManager = new BasicPromptManager(),
            ToolManager = ToolManager,
            Memory = CreateMemoryManager(),
            Configuration = ConvertToAevatarConfiguration(),
            Logger = Logger,
            AgentId = Id.ToString(),
            PublishEventCallback = async (message) => await PublishAsync(message)
        };
    }

    /// <summary>
    /// Convert strategy result to chat response.
    /// 将策略结果转换为聊天响应
    /// </summary>
    private Aevatar.Agents.AI.ChatResponse ConvertStrategyResult(
        string result,
        Aevatar.Agents.AI.ChatRequest request,
        string strategyName)
    {
        // Add result to conversation history
        var aiState = GetAIState();
        aiState.AddAssistantMessage(result, Configuration.MaxHistory);

        // Create response
        var response = new Aevatar.Agents.AI.ChatResponse
        {
            Content = result,
            RequestId = request.RequestId
        };

        // Add processing steps
        response.ProcessingSteps.Add(new Aevatar.Agents.AI.ProcessingStep
        {
            Type = Aevatar.Agents.AI.ProcessingStepType.StrategySelection,
            Description = $"Selected {strategyName} strategy",
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
        });

        return response;
    }

    #endregion

    #region Strategy Detection Helpers

    /// <summary>
    /// Check if request requires deep reasoning.
    /// 检查请求是否需要深度推理
    /// </summary>
    protected virtual bool RequiresDeepReasoning(string message)
    {
        var keywords = new[]
        {
            "analyze", "compare", "evaluate", "assess", "explore",
            "分析", "比较", "评估", "探索"
        };
        return keywords.Any(k => message.ToLower().Contains(k));
    }

    /// <summary>
    /// Check if request requires step-by-step thinking.
    /// 检查请求是否需要逐步思考
    /// </summary>
    protected virtual bool RequiresStepByStepThinking(string message)
    {
        var keywords = new[]
        {
            "step by step", "explain", "how", "why", "reasoning",
            "步骤", "解释", "为什么", "怎么", "推理"
        };
        return keywords.Any(k => message.ToLower().Contains(k));
    }

    /// <summary>
    /// Check if request requires tool interaction.
    /// 检查请求是否需要工具交互
    /// </summary>
    protected virtual bool RequiresToolInteraction(string message)
    {
        var keywords = new[]
        {
            "calculate", "search", "query", "fetch", "execute",
            "计算", "搜索", "查询", "获取", "执行"
        };
        return keywords.Any(k => message.ToLower().Contains(k));
    }

    #endregion

    #region Conversion Helpers

    /// <summary>
    /// Convert conversation history to entries.
    /// 转换对话历史为条目
    /// </summary>
    private List<Aevatar.Agents.AI.AevatarConversationEntry> ConvertToConversationEntries()
    {
        var entries = new List<Aevatar.Agents.AI.AevatarConversationEntry>();
        var aiState = GetAIState();
        var history = aiState.ConversationHistory;

        foreach (var msg in history)
        {
            entries.Add(new AevatarConversationEntry
            {
                Role = msg.Role.ToString().ToLower(),
                Content = msg.Content
            });
        }

        return entries;
    }

    /// <summary>
    /// Convert to Aevatar LLM provider.
    /// 转换为Aevatar LLM提供商
    /// </summary>
    private IAevatarLLMProvider ConvertToAevatarLLMProvider()
    {
        // This would be a wrapper or adapter in production
        return new LLMProviderAdapter(LLMProvider);
    }

    /// <summary>
    /// Create memory manager.
    /// 创建内存管理器
    /// </summary>
    private IAevatarAIMemory CreateMemoryManager()
    {
        // Return null for now, can be overridden
        return null;
    }

    /// <summary>
    /// Convert to Aevatar configuration.
    /// 转换为Aevatar配置
    /// </summary>
    private AevatarAIAgentConfiguration ConvertToAevatarConfiguration()
    {
        return new AevatarAIAgentConfiguration
        {
            Model = Configuration.Model,
            Temperature = Configuration.Temperature,
            MaxTokens = Configuration.MaxTokens,
            SystemPrompt = SystemPrompt
        };
    }

    #endregion

    #region Inner Classes

    /// <summary>
    /// Basic prompt manager implementation.
    /// 基本提示管理器实现
    /// </summary>
    private class BasicPromptManager : IAevatarPromptManager
    {
        public Task<AevatarPromptTemplate?> GetPromptTemplateAsync(
            string templateName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<AevatarPromptTemplate?>(null);
        }

        public Task<string> RenderPromptAsync(
            string templateName,
            Dictionary<string, object> variables,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.Empty);
        }

        public Task<IReadOnlyList<AevatarPromptTemplate>> GetAllTemplatesAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<AevatarPromptTemplate>>(
                new List<AevatarPromptTemplate>());
        }

        public Task RegisterPromptTemplateAsync(
            AevatarPromptTemplate template,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<string> GetSystemPromptAsync(
            string? promptName = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("You are a helpful AI assistant.");
        }

        public Task<string> FormatPromptAsync(
            string template,
            Dictionary<string, object>? variables = null,
            CancellationToken cancellationToken = default)
        {
            // Simple variable replacement
            var result = template;
            if (variables != null)
            {
                foreach (var kvp in variables)
                {
                    result = result.Replace($"{{{kvp.Key}}}", kvp.Value?.ToString() ?? string.Empty);
                }
            }

            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// Adapter for LLM provider.
    /// LLM提供商适配器
    /// </summary>
    private class LLMProviderAdapter : IAevatarLLMProvider
    {
        private readonly IAevatarLLMProvider _innerProvider;

        public LLMProviderAdapter(IAevatarLLMProvider innerProvider)
        {
            _innerProvider = innerProvider;
        }

        public async Task<AevatarLLMResponse> GenerateAsync(
            AevatarLLMRequest request,
            CancellationToken cancellationToken = default)
        {
            var response = await _innerProvider.GenerateAsync(request);
            return response;
        }

        public Task<AevatarLLMResponse> GenerateChatResponseAsync(
            IList<AevatarChatMessage> messages,
            IList<AevatarFunctionDefinition>? functions = null,
            double temperature = 0.7,
            double topP = 1.0,
            int maxTokens = 500,
            IList<string>? stopSequences = null,
            CancellationToken cancellationToken = default)
        {
            var request = new AevatarLLMRequest
            {
                Messages = messages.ToList(),
                Functions = functions?.ToList(),
                Settings = new AevatarLLMSettings
                {
                    Temperature = temperature,
                    MaxTokens = maxTokens
                }
            };

            return GenerateAsync(request, cancellationToken);
        }

        public async IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(
            AevatarLLMRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            // For now, just return the full response as a single token
            var response = await GenerateAsync(request, cancellationToken);
            yield return new AevatarLLMToken
            {
                Content = response.Content,
                IsComplete = true
            };
        }

        public Task<IList<double>> GenerateEmbeddingsAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    #endregion
}
