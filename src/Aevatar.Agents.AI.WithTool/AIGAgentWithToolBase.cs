using System.Text.Json;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Aevatar.Agents.AI.WithTool.Messages;
using Aevatar.Agents.AI.WithTool.Tools;
using Microsoft.Extensions.DependencyInjection;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool;

/// <summary>
/// Level 2: AI Agent with tool/function calling support.
/// 第二级：支持工具/函数调用的AI代理
/// 在AIGAgentBase基础上增加了自定义工具注册和管理功能
/// 继承关系：AIGAgentBase -> AIGAgentWithToolBase
/// </summary>
/// <typeparam name="TState">The agent state type (must be Protobuf)</typeparam>
public abstract class AIGAgentWithToolBase<TState> : AIGAgentBase<TState>
    where TState : class, IMessage<TState>, new()
{
    #region Fields

    private IAevatarToolManager? _toolManager;
    private ConversationHistoryManager? _historyManager;
    private ToolExecutionCoordinator? _executionCoordinator;

    #endregion

    #region Properties

    /// <summary>
    /// Gets the tool manager.
    /// 获取工具管理器
    /// </summary>
    protected IAevatarToolManager ToolManager
    {
        get
        {
            EnsureToolManagerInitialized();
            return _toolManager!;
        }
    }

    /// <summary>
    /// Gets whether tools are registered and available.
    /// 获取是否已注册工具并可用
    /// </summary>
    protected bool HasTools => _toolManager != null && GetRegisteredTools().Count > 0;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the AIGAgentWithToolBase class.
    /// 初始化AIGAgentWithToolBase类的新实例
    /// </summary>
    protected AIGAgentWithToolBase() : base()
    {
        InitializeManagers();
    }

    /// <summary>
    /// Initializes a new instance with dependency injection.
    /// 使用依赖注入初始化新实例
    /// </summary>
    protected AIGAgentWithToolBase(
        IAevatarLLMProvider llmProvider,
        IAevatarToolManager toolManager,
        ILogger? logger = null)
    {
        _toolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
        InitializeManagers();
    }

    #endregion

    #region Tool Registration

    /// <summary>
    /// Register tools for this agent. Override in derived classes to register custom tools.
    /// 为此代理注册工具。在派生类中重写以注册自定义工具
    /// </summary>
    protected virtual void RegisterTools()
    {
        // Override in derived classes to register tools
    }

    /// <summary>
    /// Helper method to register a tool using the new IAevatarTool interface.
    /// 使用新的 IAevatarTool 接口注册工具的辅助方法
    /// </summary>
    protected async Task RegisterToolAsync(IAevatarTool tool, ILogger? logger = null)
    {
        EnsureToolManagerInitialized();

        var context = new ToolContext
        {
            AgentId = Id.ToString(),
            AgentType = GetType().Name
        };

        var toolDefinition = tool.CreateToolDefinition(context, logger ?? Logger);
        await ToolManager.RegisterToolAsync(toolDefinition);
    }

    /// <summary>
    /// Get list of registered tools.
    /// 获取已注册的工具列表
    /// </summary>
    protected IReadOnlyList<ToolDefinition> GetRegisteredTools()
    {
        if (_toolManager == null)
            return Array.Empty<ToolDefinition>();

        var task = ToolManager.GetAvailableToolsAsync();
        if (task == null) return Array.Empty<ToolDefinition>();
        return task.Result ?? [];
    }

    /// <summary>
    /// Ensure tool manager is initialized.
    /// 确保工具管理器已初始化
    /// </summary>
    private void EnsureToolManagerInitialized()
    {
        if (_toolManager != null)
            return;

        _toolManager = CreateToolManager();

        // Register tools after manager is created
        RegisterTools();
        UpdateActiveToolsInState();
    }

    /// <summary>
    /// Creates the tool manager. Override to customize.
    /// 创建工具管理器。重写以自定义
    /// </summary>
    protected virtual IAevatarToolManager CreateToolManager()
    {
        // Default implementation creates a DefaultToolManager
        // Use a null logger for now, can be enhanced later
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultToolManager>.Instance;
        return new DefaultToolManager(logger);
    }

    /// <summary>
    /// Initialize history manager and execution coordinator.
    /// 初始化历史管理器和执行协调器
    /// </summary>
    private void InitializeManagers()
    {
        _historyManager = new ConversationHistoryManager(State.History);
        // Coordinator will be initialized when needed (requires LLMProvider)
    }

    /// <summary>
    /// Get or create the execution coordinator.
    /// 获取或创建执行协调器
    /// </summary>
    private ToolExecutionCoordinator GetExecutionCoordinator()
    {
        if (_executionCoordinator == null)
        {
            if (_historyManager == null)
                throw new InvalidOperationException("History manager not initialized");
            
            _executionCoordinator = new ToolExecutionCoordinator(
                ToolManager,
                LLMProvider,
                _historyManager,
                Logger);
        }
        return _executionCoordinator;
    }

    #endregion

    #region Tool Execution

    /// <summary>
    /// Execute a tool by name with arguments.
    /// 通过名称和参数执行工具
    /// </summary>
    protected async Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        return await ToolManager.ExecuteToolAsync(toolName, parameters, context, cancellationToken);
    }

    #endregion

    #region State Management

    /// <summary>
    /// Update active tools list in AI state.
    /// 在AI状态中更新活动工具列表
    /// </summary>
    protected virtual void UpdateActiveToolsInState()
    {
        // Default implementation does nothing.
        // Derived classes can override to persist active tools in state if needed.
    }

    #endregion

    #region Chat Methods

    /// <summary>
    /// Process a chat request and return a response.
    /// Overrides base implementation to support tools and history.
    /// </summary>
    public override async Task<ChatResponse> ChatAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("AI Agent must be initialized before use. Call InitializeAsync() first.");

        try
        {
            // 1. Add user message to history
            AddMessageToHistory(request.Message, AevatarChatRole.User);

            // 2. Build LLM request (includes history and tools)
            var llmRequest = BuildLLMRequest(request);

            // 3. Call LLM
            var llmResponse = await LLMProvider.GenerateAsync(llmRequest, cancellationToken);

            // 4. Handle function calls if present
            if (llmResponse.AevatarFunctionCall != null)
            {
                return await HandleFunctionCallAsync(request, llmResponse, cancellationToken);
            }

            // 5. Add assistant response to history
            AddMessageToHistory(llmResponse.Content, AevatarChatRole.Assistant);

            // 6. Build response
            var response = new ChatResponse
            {
                Content = llmResponse.Content,
                RequestId = request.RequestId
            };

            if (llmResponse.Usage != null)
            {
                response.Usage = new AevatarTokenUsage
                {
                    PromptTokens = llmResponse.Usage.PromptTokens,
                    CompletionTokens = llmResponse.Usage.CompletionTokens,
                    TotalTokens = llmResponse.Usage.TotalTokens
                };
            }

            // 7. Publish events
            await PublishChatResponseAsync(response, request.RequestId);

            // 8. Record AI decision (Event Sourcing)
            RaiseAIDecision(
                request.Message,
                response.Content,
                response.Usage?.TotalTokens ?? 0,
                new Dictionary<string, string>
                {
                    ["request_id"] = request.RequestId,
                    ["chat_type"] = "tool_aware"
                });

            if (AutoConfirmEvents && EventStore != null)
            {
                await ConfirmEventsAsync(cancellationToken);
            }

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
    /// Overrides base to include history and tools.
    /// </summary>
    protected override AevatarLLMRequest BuildLLMRequest(ChatRequest request)
    {
        var llmRequest = new AevatarLLMRequest
        {
            SystemPrompt = SystemPrompt,
            Settings = GetLLMSettings(request),
            Messages = new List<AevatarChatMessage>()
        };

        // Add history
        if (State.History != null)
        {
            foreach (var msg in State.History)
            {
                llmRequest.Messages.Add(msg);
            }
        }

        // Inject tools
        if (HasTools)
        {
            var functionDefs = ToolManager.GenerateFunctionDefinitionsAsync().Result;
            if (functionDefs != null && functionDefs.Count > 0)
            {
                llmRequest.Functions = functionDefs.ToList();
            }
        }

        return llmRequest;
    }

    /// <summary>
    /// Handle function call from LLM.
    /// </summary>
    private async Task<ChatResponse> HandleFunctionCallAsync(
        ChatRequest request,
        AevatarLLMResponse llmResponse,
        CancellationToken cancellationToken)
    {
        var functionCall = llmResponse.AevatarFunctionCall;
        if (functionCall == null) throw new InvalidOperationException("Function call expected");

        Logger.LogDebug("Executing tool: {ToolName}", functionCall.Name);

        // Build LLM request for follow-up (includes history + tool result)
        var llmRequest = BuildLLMRequest(request);

        // Execute tool workflow using coordinator
        var coordinator = GetExecutionCoordinator();
        var (toolResult, finalLLMResponse) = await coordinator.ExecuteToolWorkflowAsync(
            functionCall,
            llmRequest,
            cancellationToken);

        // Publish tool execution event
        await PublishToolExecutionEventAsync(functionCall.Name, new Dictionary<string, object>(), toolResult, request.RequestId);

        // Build and publish final response
        var response = coordinator.CreateResponseWithToolInfo(
            request.RequestId,
            finalLLMResponse,
            functionCall,
            toolResult);

        await PublishChatResponseAsync(response, request.RequestId);

        // Record AI decision (Event Sourcing)
        RaiseAIDecision(
            request.Message,
            response.Content,
            response.Usage?.TotalTokens ?? 0,
            new Dictionary<string, string>
            {
                ["request_id"] = request.RequestId,
                ["chat_type"] = "tool_execution",
                ["tool_name"] = functionCall.Name
            });

        if (AutoConfirmEvents && EventStore != null)
        {
            await ConfirmEventsAsync(cancellationToken);
        }

        return response;
    }


    /// <summary>
    /// Populate tool call arguments from JSON string.
    /// </summary>
    private void PopulateToolCallArguments(ToolCallInfo toolCall, string argumentsJson)
    {
        var args = ParseToolArguments(argumentsJson);
        foreach (var arg in args)
        {
            toolCall.Arguments[arg.Key.ToString()] = arg.Value?.ToString() ?? string.Empty;
        }
    }

    /// <summary>
    /// Add message to conversation history.
    /// </summary>
    protected void AddMessageToHistory(string content, AevatarChatRole role, string? name = null)
    {
        if (_historyManager == null)
            throw new InvalidOperationException("History manager not initialized");
        
        _historyManager.AddMessage(content, role, name);
    }

    protected void AddMessageToHistory(AevatarChatMessage msg)
    {
        if (_historyManager == null)
            throw new InvalidOperationException("History manager not initialized");
        
        _historyManager.AddMessage(msg);
    }
    /// <summary>
    /// Publish chat response event.
    /// </summary>
    protected virtual async Task PublishChatResponseAsync(ChatResponse response, string requestId)
    {
        await PublishAsync(new ChatResponseEvent
        {
            RequestId = requestId,
            Content = response.Content,
            TokensUsed = response.Usage?.TotalTokens ?? 0,
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
        });
    }

    /// <summary>
    /// Publish tool execution event.
    /// </summary>
    protected virtual async Task PublishToolExecutionEventAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionResult result,
        string requestId)
    {
        var response = new ToolExecutionResponseEvent
        {
            RequestId = requestId,
            ToolName = toolName,
            Success = result.IsSuccess,
            Result = result.Content ?? "",
            Error = result.ErrorMessage ?? "",
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
        };
        await PublishAsync(response);
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handle tool execution request events.
    /// 处理工具执行请求事件
    /// </summary>
    [EventHandler]
    protected virtual async Task HandleToolExecutionRequestEvent(ToolExecutionRequestEvent evt)
    {
        var parameters = ParseToolArguments(evt.Arguments);
        var result = await ExecuteToolAsync(evt.ToolName, parameters);

        var response = new ToolExecutionResponseEvent
        {
            RequestId = evt.RequestId,
            ToolName = evt.ToolName,
            Success = result.IsSuccess,
            Result = result.Content ?? "",
            Error = result.ErrorMessage ?? "",
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
        };

        await PublishAsync(response);
    }

    /// <summary>
    /// Parse tool arguments from JSON string.
    /// 从JSON字符串解析工具参数
    /// </summary>
    protected Dictionary<string, object> ParseToolArguments(string argumentsJson)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return new Dictionary<string, object>();
            }

            return JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson)
                ?? new Dictionary<string, object>();
        }
        catch (JsonException ex)
        {
            Logger?.LogError(ex, "Failed to parse tool arguments: {Json}", argumentsJson);
            return new Dictionary<string, object>();
        }
    }

    #endregion
}