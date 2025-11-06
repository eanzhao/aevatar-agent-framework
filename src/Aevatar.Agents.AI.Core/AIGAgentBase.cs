using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Strategies;
using Aevatar.Agents.Core;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

/// <summary>
/// AI增强的Agent基类
/// 提供LLM集成、工具管理、记忆管理等AI能力
/// </summary>
// ReSharper disable InconsistentNaming
public abstract class AIGAgentBase<TState> : GAgentBase<TState>
    where TState : class, IMessage, new()
{
    #region AI Components

    /// <summary>
    /// LLM提供者
    /// </summary>
    protected IAevatarLLMProvider LLMProvider { get; private set; } = null!;

    /// <summary>
    /// 提示词管理器
    /// </summary>
    protected IAevatarPromptManager PromptManager { get; private set; } = null!;

    /// <summary>
    /// 工具管理器
    /// </summary>
    protected IAevatarToolManager ToolManager { get; private set; } = null!;

    /// <summary>
    /// 记忆管理器
    /// </summary>
    protected IAevatarMemory Memory { get; private set; } = null!;

    /// <summary>
    /// AI配置
    /// </summary>
    protected AevatarAIAgentConfiguration Configuration { get; private set; } = new();
    
    /// <summary>
    /// AI处理策略工厂
    /// </summary>
    protected IAevatarAIProcessingStrategyFactory StrategyFactory { get; private set; } = null!;

    #endregion

    #region Ctors

    protected AIGAgentBase()
    {
        // AI组件将通过依赖注入设置
    }

    protected AIGAgentBase(
        IAevatarLLMProvider llmProvider,
        IAevatarPromptManager promptManager,
        IAevatarToolManager toolManager,
        IAevatarMemory memory,
        IAevatarAIProcessingStrategyFactory? strategyFactory = null,
        ILogger? logger = null) : base(logger)
    {
        LLMProvider = llmProvider ?? throw new ArgumentNullException(nameof(llmProvider));
        PromptManager = promptManager ?? throw new ArgumentNullException(nameof(promptManager));
        ToolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
        Memory = memory ?? throw new ArgumentNullException(nameof(memory));
        StrategyFactory = strategyFactory ?? new AevatarAIProcessingStrategyFactory();
    }

    #endregion

    #region AI Configuration

    /// <summary>
    /// 配置AI（子类重写）
    /// </summary>
    protected virtual void ConfigureAI(AevatarAIAgentConfiguration config)
    {
        // 子类重写以配置AI参数
    }

    /// <summary>
    /// 初始化AI组件
    /// </summary>
    public virtual async Task InitializeAsync(
        IAevatarLLMProvider? llmProvider = null,
        IAevatarPromptManager? promptManager = null,
        IAevatarToolManager? toolManager = null,
        IAevatarMemory? memory = null,
        IAevatarAIProcessingStrategyFactory? strategyFactory = null,
        CancellationToken cancellationToken = default)
    {
        // 设置组件（如果提供）
        if (llmProvider != null) LLMProvider = llmProvider;
        if (promptManager != null) PromptManager = promptManager;
        if (toolManager != null) ToolManager = toolManager;
        if (memory != null) Memory = memory;
        if (strategyFactory != null) StrategyFactory = strategyFactory;
        
        // 确保策略工厂已初始化
        StrategyFactory ??= new AevatarAIProcessingStrategyFactory();

        // 配置
        ConfigureAI(Configuration);

        // 注册内置工具
        await RegisterBuiltInToolsAsync(cancellationToken);

        // 初始化记忆
        await InitializeMemoryAsync(cancellationToken);

        Logger?.LogInformation("AI components initialized for agent {AgentId}", Id);
    }

    #endregion

    #region AI Event Handler

    /// <summary>
    /// AI事件处理器（通用入口）
    /// </summary>
    protected virtual async Task<IMessage?> ProcessWithAIAsync(
        EventEnvelope envelope,
        AevatarAIEventHandlerAttribute? config = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 记录到对话历史
            await Memory.AddMessageAsync(new AevatarConversationMessage
            {
                Role = AevatarChatRole.User,
                Content = $"Event: {envelope.Id}",
                Metadata = new Dictionary<string, object>
                {
                    ["EventType"] = envelope.Payload?.TypeUrl ?? "unknown",
                    ["EventId"] = envelope.Id
                }
            }, cancellationToken);

            // 构建上下文
            var context = await BuildAevatarAIContextAsync(envelope, cancellationToken);

            // 选择处理模式和策略
            var mode = config?.Mode ?? AevatarAIProcessingMode.Standard;
            var strategy = StrategyFactory.GetStrategy(mode);
            
            // 构建策略依赖
            var dependencies = CreateStrategyDependencies();

            // 使用策略处理
            var response = await strategy.ProcessAsync(context, config, dependencies, cancellationToken);

            // 记录响应到对话历史
            if (!string.IsNullOrEmpty(response))
            {
                await Memory.AddMessageAsync(new AevatarConversationMessage
                {
                    Role = AevatarChatRole.Assistant,
                    Content = response
                }, cancellationToken);
            }

            // 转换为事件（子类可重写）
            return await ConvertResponseToEventAsync(response, envelope, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "AI processing failed for event {EventId}", envelope.Id);

            // 发布错误事件
            await PublishAsync(new AevatarAIErrorEvent
            {
                ErrorType = ex.GetType().Name,
                Message = ex.Message,
                StackTrace = ex.StackTrace ?? string.Empty,
                Context = $"Processing event {envelope.Id}",
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            });

            throw;
        }
    }
    
    /// <summary>
    /// 创建策略依赖项
    /// </summary>
    protected virtual AevatarAIStrategyDependencies CreateStrategyDependencies()
    {
        return new AevatarAIStrategyDependencies
        {
            LLMProvider = LLMProvider,
            PromptManager = PromptManager,
            ToolManager = ToolManager,
            Memory = Memory,
            Configuration = Configuration,
            Logger = Logger,
            AgentId = Id.ToString(),
            PublishEventCallback = async (message) => await PublishAsync(message),
            ExecuteToolCallback = ExecuteToolAsync
        };
    }

    #endregion


    #region 工具执行

    /// <summary>
    /// 执行工具
    /// </summary>
    protected async Task<object?> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        var context = new AevatarExecutionContext
        {
            AgentId = Id,
            SessionId = GetSessionId()
        };

        var result = await ToolManager.ExecuteToolAsync(
            toolName,
            parameters,
            context,
            cancellationToken);

        // 发布工具执行事件
        var toolEvent = new AevatarToolExecutedEvent
        {
            ToolName = toolName,
            Result = result.Result?.ToString() ?? string.Empty,
            Success = result.Success,
            DurationMs = (long)result.ExecutionTime.TotalMilliseconds
        };

        // 添加参数到 map
        foreach (var param in parameters)
        {
            toolEvent.Parameters[param.Key] = param.Value?.ToString() ?? string.Empty;
        }

        await PublishAsync(toolEvent);

        return result.Result;
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 构建AI上下文
    /// </summary>
    protected virtual async Task<AevatarAIContext> BuildAevatarAIContextAsync(
        EventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var context = new AevatarAIContext
        {
            EventEnvelope = envelope,
            AgentState = State,
            WorkingMemory = await Memory.GetAllContextAsync(cancellationToken: cancellationToken),
            RecentMessages = await Memory.GetRecentMessagesAsync(5, cancellationToken)
        };

        // 从事件中提取问题（子类可重写）
        context.Question = await ExtractQuestionFromEventAsync(envelope, cancellationToken);

        return context;
    }


    /// <summary>
    /// 从事件中提取问题
    /// </summary>
    protected virtual Task<string?> ExtractQuestionFromEventAsync(
        EventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        // 子类重写以从特定事件类型中提取问题
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// 将响应转换为事件
    /// </summary>
    protected virtual Task<IMessage?> ConvertResponseToEventAsync(
        string response,
        EventEnvelope originalEnvelope,
        CancellationToken cancellationToken)
    {
        // 子类重写以生成特定的响应事件
        return Task.FromResult<IMessage?>(null);
    }

    /// <summary>
    /// 注册内置工具
    /// </summary>
    protected virtual async Task RegisterBuiltInToolsAsync(CancellationToken cancellationToken)
    {
        // 注册事件发布工具
        await ToolManager.RegisterToolAsync(CreateEventPublishTool(), cancellationToken);

        // 注册状态查询工具
        await ToolManager.RegisterToolAsync(CreateStateQueryTool(), cancellationToken);

        // 注册记忆搜索工具
        await ToolManager.RegisterToolAsync(CreateMemorySearchTool(), cancellationToken);
    }

    /// <summary>
    /// 初始化记忆
    /// </summary>
    protected virtual async Task InitializeMemoryAsync(CancellationToken cancellationToken)
    {
        // 加载历史记忆（如果有）
        // 设置初始上下文
        await Memory.UpdateContextAsync("agent_id", Id.ToString(), cancellationToken: cancellationToken);
        await Memory.UpdateContextAsync("agent_type", GetType().Name, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 获取会话ID
    /// </summary>
    protected virtual string GetSessionId()
    {
        return $"{Id}_{DateTimeOffset.UtcNow:yyyyMMdd}";
    }


    #region 内置工具创建

    private AevatarTool CreateEventPublishTool()
    {
        return new AevatarTool
        {
            Name = "publish_event",
            Description = "Publish an event to the agent stream",
            Parameters = new AevatarAevatarToolParameters
            {
                Items = new Dictionary<string, AevatarToolParameter>
                {
                    ["event_type"] = new() { Type = "string", Required = true },
                    ["payload"] = new() { Type = "object", Required = true },
                    ["direction"] = new() { Type = "string", Enum = new[] { "up", "down", "both" } }
                },
                Required = new[] { "event_type", "payload" }
            },
            ExecuteAsync = async (parameters, context, ct) =>
            {
                // 实现事件发布逻辑
                return new { success = true, eventId = Guid.NewGuid() };
            }
        };
    }

    private AevatarTool CreateStateQueryTool()
    {
        return new AevatarTool
        {
            Name = "query_state",
            Description = "Query agent state information",
            Parameters = new AevatarAevatarToolParameters
            {
                Items = new Dictionary<string, AevatarToolParameter>
                {
                    ["field"] = new() { Type = "string", Required = true }
                }
            },
            ExecuteAsync = async (parameters, context, ct) =>
            {
                // 实现状态查询逻辑
                var field = parameters["field"]?.ToString();
                // 使用反射或其他方式获取状态字段
                return State.ToString();
            }
        };
    }

    private AevatarTool CreateMemorySearchTool()
    {
        return new AevatarTool
        {
            Name = "search_memory",
            Description = "Search long-term memory",
            Parameters = new AevatarAevatarToolParameters
            {
                Items = new Dictionary<string, AevatarToolParameter>
                {
                    ["query"] = new() { Type = "string", Required = true },
                    ["top_k"] = new() { Type = "integer", DefaultValue = 5 }
                }
            },
            ExecuteAsync = async (parameters, context, ct) =>
            {
                var query = parameters["query"]?.ToString() ?? "";
                var topK = Convert.ToInt32(parameters.GetValueOrDefault("top_k", 5));

                var results = await Memory.RecallAsync(
                    query,
                    new AevatarRecallOptions { TopK = topK },
                    ct);

                return results.Select(r => new
                {
                    content = r.Item.Content,
                    score = r.RelevanceScore
                });
            }
        };
    }

    #endregion

    #endregion
}