using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Strategies;
using Aevatar.Agents.AI.Core.Strategies;
using Aevatar.Agents.AI.Core.Tools;
using Aevatar.Agents.Core;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

/// <summary>
/// AI Agent基类
/// 提供可扩展的AI能力，支持多LLM框架实现
/// </summary>
public abstract class AIGAgentBase<TState> : GAgentBase<TState>
    where TState : class, IMessage, new()
{
    #region Properties

    /// <summary>
    /// AI配置
    /// </summary>
    protected AevatarAIAgentConfiguration Configuration { get; } = new();

    /// <summary>
    /// 系统提示词（子类必须实现）
    /// </summary>
    protected abstract string SystemPrompt { get; }

    /// <summary>
    /// LLM提供者（可选注入）
    /// </summary>
    protected virtual IAevatarLLMProvider? LLMProvider { get; set; }

    /// <summary>
    /// 工具管理器（使用内置实现）
    /// </summary>
    protected virtual IAevatarToolManager ToolManager { get; }

    /// <summary>
    /// 对话历史
    /// </summary>
    private readonly List<(string role, string content)> _conversationHistory = new();

    /// <summary>
    /// AI处理策略工厂（可选）
    /// </summary>
    protected IAevatarAIProcessingStrategyFactory? StrategyFactory { get; set; }

    #endregion

    #region Constructors

    protected AIGAgentBase(ILogger? logger = null) : base(logger)
    {
        // 初始化工具管理器
        ToolManager = new InternalToolManager();
        Initialize();
    }

    #endregion

    #region Initialization

    private void Initialize()
    {
        // 配置AI
        ConfigureAI(Configuration);

        // 注册核心工具
        RegisterCoreTools();

        // 注册自定义工具
        RegisterTools();
    }

    /// <summary>
    /// 配置AI（子类可重写）
    /// </summary>
    protected virtual void ConfigureAI(AevatarAIAgentConfiguration config)
    {
        config.SystemPrompt = SystemPrompt;
        config.Temperature = 0.7;
        config.MaxTokens = 32000;
    }

    /// <summary>
    /// 注册自定义工具（子类重写）
    /// </summary>
    protected virtual void RegisterTools()
    {
        // 子类注册自己的工具
    }

    /// <summary>
    /// 注册工具
    /// </summary>
    protected void RegisterTool(ToolDefinition tool)
    {
        ToolManager.RegisterToolAsync(tool).GetAwaiter().GetResult();
        Logger?.LogDebug("Registered tool: {ToolName}", tool.Name);
    }

    #endregion

    #region AI Processing - 核心方法供子类实现

    /// <summary>
    /// 处理AI请求的核心方法
    /// 如果提供了LLMProvider，使用它；否则子类必须实现
    /// </summary>
    protected virtual async Task<string> GenerateResponseAsync(
        string input,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        // 如果有LLMProvider，使用它
        if (LLMProvider != null)
        {
            var request = new AevatarLLMRequest
            {
                UserPrompt = input,
                SystemPrompt = systemPrompt,
                Settings = new AevatarLLMSettings
                {
                    Temperature = Configuration.Temperature,
                    MaxTokens = Configuration.MaxTokens
                }
            };
            var response = await LLMProvider.GenerateAsync(request, cancellationToken);
            return response.Content;
        }

        // 否则使用子类实现
        return await InternalGenerateResponseAsync(input, systemPrompt, cancellationToken);
    }

    /// <summary>
    /// 内部生成方法（子类实现）
    /// </summary>
    protected virtual Task<string> InternalGenerateResponseAsync(
        string input,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(
            "Either provide an IAevatarLLMProvider or override InternalGenerateResponseAsync");
    }

    /// <summary>
    /// 流式生成（可选实现）
    /// </summary>
    protected virtual async IAsyncEnumerable<string> GenerateStreamingResponseAsync(
        string input,
        string? systemPrompt = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        // 如果有LLMProvider，使用它
        if (LLMProvider != null)
        {
            var request = new AevatarLLMRequest
            {
                UserPrompt = input,
                SystemPrompt = systemPrompt,
                Settings = new AevatarLLMSettings
                {
                    Temperature = Configuration.Temperature,
                    MaxTokens = Configuration.MaxTokens
                }
            };

            await foreach (var token in LLMProvider.GenerateStreamAsync(request, cancellationToken))
            {
                yield return token.Content;
            }
        }
        else
        {
            // 默认实现：返回非流式结果
            var result = await GenerateResponseAsync(input, systemPrompt, cancellationToken);
            yield return result;
        }
    }

    #endregion

    #region Public AI Methods

    /// <summary>
    /// 使用AI处理文本
    /// </summary>
    public virtual async Task<string> ProcessWithAIAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        // 添加到对话历史
        _conversationHistory.Add(("user", input));

        // 生成响应
        var response = await GenerateResponseAsync(input, Configuration.SystemPrompt, cancellationToken);

        // 添加响应到历史
        _conversationHistory.Add(("assistant", response));

        return response;
    }

    /// <summary>
    /// 使用特定策略处理（可选）
    /// </summary>
    public virtual async Task<string> ProcessWithStrategyAsync(
        string input,
        AevatarAIProcessingMode mode = AevatarAIProcessingMode.Standard,
        CancellationToken cancellationToken = default)
    {
        // 如果有策略工厂，优先使用策略工厂
        if (StrategyFactory != null)
        {
            try
            {
                var strategy = StrategyFactory.GetStrategy(mode);
                var context = CreateAIContext(input);
                var dependencies = CreateStrategyDependencies();

                return await strategy.ProcessAsync(
                    context,
                    null, // 没有特定的event handler配置
                    dependencies,
                    cancellationToken);
            }
            catch (NotSupportedException ex)
            {
                Logger?.LogWarning(ex, "Strategy {Mode} not supported, falling back to simple implementation", mode);
                // 继续执行简单实现
            }
        }

        // 简单实现：根据模式调整处理
        switch (mode)
        {
            case AevatarAIProcessingMode.ChainOfThought:
                var cotPrompt = $"Let's think step by step about this: {input}";
                return await ProcessWithAIAsync(cotPrompt, cancellationToken);

            case AevatarAIProcessingMode.ReAct:
                var reactPrompt = $"Thought: Analyze the following\nQuestion: {input}\nAction:";
                return await ProcessWithAIAsync(reactPrompt, cancellationToken);

            case AevatarAIProcessingMode.TreeOfThoughts:
                var totPrompt = $"Explore multiple approaches for: {input}";
                return await ProcessWithAIAsync(totPrompt, cancellationToken);

            default:
                return await ProcessWithAIAsync(input, cancellationToken);
        }
    }

    /// <summary>
    /// 创建AI上下文（可被子类重写）
    /// </summary>
    protected virtual AevatarAIContext CreateAIContext(string input)
    {
        return new AevatarAIContext
        {
            AgentId = Id.ToString(),
            Question = input,
            SystemPrompt = SystemPrompt,
            ConversationHistory = _conversationHistory.Select(h =>
                new AevatarConversationEntry { Role = h.role, Content = h.content }).ToList()
        };
    }

    /// <summary>
    /// 创建策略依赖项（可被子类重写）
    /// </summary>
    protected virtual AevatarAIStrategyDependencies CreateStrategyDependencies()
    {
        return new AevatarAIStrategyDependencies
        {
            Configuration = Configuration,
            Logger = Logger,
            // 这些依赖项需要子类提供具体实现
            LLMProvider = null, // 子类应该提供
            PromptManager = null, // 子类应该提供
            ToolManager = null, // 子类应该提供
            PublishEventCallback = async (evt) => { await PublishAsync(evt); }
        };
    }

    #endregion

    #region Tool Management

    /// <summary>
    /// 执行工具
    /// </summary>
    public virtual async Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ToolManager.ExecuteToolAsync(toolName, parameters, null, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error executing tool {ToolName}", toolName);
            return new ToolExecutionResult
            {
                Success = false,
                Error = ex.Message,
                ToolName = toolName
            };
        }
    }

    /// <summary>
    /// 获取所有工具定义
    /// </summary>
    public async Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(
        CancellationToken cancellationToken = default)
    {
        return await ToolManager.GetAvailableToolsAsync(cancellationToken);
    }

    /// <summary>
    /// 获取工具的函数定义
    /// </summary>
    public async Task<IReadOnlyList<AevatarFunctionDefinition>> GetToolDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        return await ToolManager.GenerateFunctionDefinitionsAsync(cancellationToken);
    }

    #endregion

    #region Event Handling

    /// <summary>
    /// 处理事件（使用AI）
    /// </summary>
    [Aevatar.Agents.Abstractions.EventHandler]
    protected virtual async Task<IMessage?> HandleEventWithAIAsync(
        EventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 提取问题
            var question = await ExtractQuestionFromEventAsync(envelope, cancellationToken);
            if (string.IsNullOrEmpty(question))
            {
                return null;
            }

            // 处理
            var response = await ProcessWithAIAsync(question, cancellationToken);

            // 转换响应
            return await ConvertResponseToEventAsync(response, envelope, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to process event {EventId} with AI", envelope.Id);
            return null;
        }
    }

    /// <summary>
    /// 从事件提取问题（子类可重写）
    /// </summary>
    protected virtual Task<string?> ExtractQuestionFromEventAsync(
        EventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        // 子类实现
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// 将响应转换为事件（子类可重写）
    /// </summary>
    protected virtual Task<IMessage?> ConvertResponseToEventAsync(
        string response,
        EventEnvelope originalEnvelope,
        CancellationToken cancellationToken)
    {
        // 子类实现
        return Task.FromResult<IMessage?>(null);
    }

    #endregion

    #region Core Tools

    private void RegisterCoreTools()
    {
        // 使用 CoreToolsRegistry 注册核心工具
        // 这提供了更好的关注点分离和可测试性
        var toolContext = new ToolContext
        {
            AgentId = Id.ToString(),
            AgentType = GetType().Name,
            GetStateCallback = () => State,
            PublishEventCallback = async (evt) => await PublishAsync(evt),
            Logger = Logger,
            IncludeCoreTools = true
        };

        // 注册所有核心工具
        foreach (var coreToolInstance in CoreToolsRegistry.GetAllTools())
        {
            var toolDefinition = coreToolInstance.CreateToolDefinition(toolContext, Logger);
            RegisterTool(toolDefinition);

            Logger?.LogDebug("Registered core tool: {ToolName} - {ToolDescription}",
                toolDefinition.Name,
                toolDefinition.Description);
        }
    }

    /// <summary>
    /// 可选：允许子类替换工具提供者
    /// </summary>
    protected virtual IToolProvider? ToolProvider { get; set; }

    #endregion

    #region Memory Management (Simple)

    /// <summary>
    /// 清空对话历史
    /// </summary>
    public virtual void ClearConversationHistory()
    {
        _conversationHistory.Clear();
        Logger?.LogDebug("Conversation history cleared");
    }

    /// <summary>
    /// 获取对话历史
    /// </summary>
    public IReadOnlyList<(string role, string content)> GetConversationHistory()
    {
        return _conversationHistory.AsReadOnly();
    }

    #endregion

    #region Internal Tool Manager Implementation

    /// <summary>
    /// 内部的简单工具管理器实现
    /// </summary>
    private class InternalToolManager : IAevatarToolManager
    {
        private readonly Dictionary<string, ToolDefinition> _tools = new();

        public Task RegisterToolAsync(ToolDefinition tool, CancellationToken cancellationToken = default)
        {
            _tools[tool.Name] = tool;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ToolDefinition>>(_tools.Values.ToList());
        }

        public async Task<ToolExecutionResult> ExecuteToolAsync(
            string toolName,
            Dictionary<string, object> parameters,
            Abstractions.ExecutionContext? context = null,
            CancellationToken cancellationToken = default)
        {
            if (_tools.TryGetValue(toolName, out var tool) && tool.ExecuteAsync != null)
            {
                var result = await tool.ExecuteAsync(parameters, context, cancellationToken);
                return new ToolExecutionResult
                {
                    Success = true,
                    Result = result,
                    ToolName = toolName
                };
            }

            return new ToolExecutionResult
            {
                Success = false,
                Error = $"Tool {toolName} not found or has no executor",
                ToolName = toolName
            };
        }

        public Task<IReadOnlyList<AevatarFunctionDefinition>> GenerateFunctionDefinitionsAsync(
            CancellationToken cancellationToken = default)
        {
            var definitions = new List<AevatarFunctionDefinition>();
            foreach (var tool in _tools.Values)
            {
                definitions.Add(new AevatarFunctionDefinition
                {
                    Name = tool.Name,
                    Description = tool.Description ?? "",
                    Parameters = tool.Parameters?.Items?.ToDictionary(
                        p => p.Key,
                        p => new AevatarParameterDefinition
                        {
                            Type = p.Value.Type ?? "string",
                            Description = p.Value.Description ?? "",
                            Required = tool.Parameters?.Required?.Contains(p.Key) ?? false
                        }) ?? new Dictionary<string, AevatarParameterDefinition>()
                });
            }

            return Task.FromResult<IReadOnlyList<AevatarFunctionDefinition>>(definitions);
        }
    }

    #endregion
}