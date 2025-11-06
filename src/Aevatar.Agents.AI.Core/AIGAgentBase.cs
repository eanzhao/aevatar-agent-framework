using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Strategies;
using Aevatar.Agents.AI.Core.Tools;
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
    protected IAevatarAIMemory Memory { get; private set; } = null!;

    /// <summary>
    /// AI配置
    /// </summary>
    protected AevatarAIAgentConfiguration Configuration { get; private set; } = new();
    
    /// <summary>
    /// AI处理策略工厂
    /// </summary>
    protected IAevatarAIProcessingStrategyFactory StrategyFactory { get; private set; } = null!;
    
    /// <summary>
    /// 工具提供者
    /// </summary>
    protected IToolProvider ToolProvider { get; private set; } = null!;
    
    /// <summary>
    /// 工具执行器
    /// </summary>
    protected IToolExecutor ToolExecutor { get; private set; } = null!;

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
        IAevatarAIMemory memory,
        IAevatarAIProcessingStrategyFactory? strategyFactory = null,
        IToolProvider? toolProvider = null,
        IToolExecutor? toolExecutor = null,
        ILogger? logger = null) : base(logger)
    {
        LLMProvider = llmProvider ?? throw new ArgumentNullException(nameof(llmProvider));
        PromptManager = promptManager ?? throw new ArgumentNullException(nameof(promptManager));
        ToolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
        Memory = memory ?? throw new ArgumentNullException(nameof(memory));
        StrategyFactory = strategyFactory ?? new AevatarAIProcessingStrategyFactory();
        ToolProvider = toolProvider ?? new DefaultToolProvider();
        ToolExecutor = toolExecutor ?? new DefaultToolExecutor();
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
        IAevatarAIMemory? memory = null,
        IAevatarAIProcessingStrategyFactory? strategyFactory = null,
        IToolProvider? toolProvider = null,
        Abstractions.IToolExecutor? toolExecutor = null,
        CancellationToken cancellationToken = default)
    {
        // 设置组件（如果提供）
        if (llmProvider != null) LLMProvider = llmProvider;
        if (promptManager != null) PromptManager = promptManager;
        if (toolManager != null) ToolManager = toolManager;
        if (memory != null) Memory = memory;
        if (strategyFactory != null) StrategyFactory = strategyFactory;
        if (toolProvider != null) ToolProvider = toolProvider;
        if (toolExecutor != null) ToolExecutor = toolExecutor;
        
        // 确保所有组件已初始化
        StrategyFactory ??= new AevatarAIProcessingStrategyFactory();
        ToolProvider ??= new DefaultToolProvider();
        ToolExecutor ??= new DefaultToolExecutor();

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
        var context = new ToolExecutionContext
        {
            AgentId = Id.ToString(),
            ToolManager = ToolManager,
            Memory = Memory,
            PublishEventCallback = async (message) => await PublishAsync(message),
            GetSessionId = GetSessionId,
            RecordToMemory = Configuration.RecordToolExecutions ?? true,
            Logger = Logger
        };

        var result = await ToolExecutor.ExecuteToolAsync(
            toolName,
            parameters,
            context,
            cancellationToken);

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
    /// 注册工具
    /// </summary>
    protected virtual async Task RegisterBuiltInToolsAsync(CancellationToken cancellationToken)
    {
        // 创建工具上下文
        var toolContext = new ToolContext
        {
            AgentId = Id.ToString(),
            AgentType = GetType().Name,
            IncludeCoreTools = true,
            GetStateCallback = () => State,
            PublishEventCallback = async (message) => await PublishAsync(message),
            Memory = Memory,
            Logger = Logger,
            GetSessionIdCallback = GetSessionId
        };

        // 获取所有工具
        var tools = await ToolProvider.GetToolsAsync(toolContext);

        // 注册到工具管理器
        foreach (var tool in tools)
        {
            await ToolManager.RegisterToolAsync(tool, cancellationToken);
            Logger?.LogDebug("Registered tool: {ToolName} (Category: {Category})", 
                tool.Name, tool.Category);
        }
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

    #endregion
}