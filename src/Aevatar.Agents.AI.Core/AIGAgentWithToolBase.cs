using System.Text.Json;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.Core.Tools;
using Microsoft.Extensions.DependencyInjection;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using AevatarChatRole = Aevatar.Agents.AI.Core.Messages.AevatarChatRole;

namespace Aevatar.Agents.AI.Core;

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
    protected internal IReadOnlyList<ToolDefinition> GetRegisteredTools()
    {
        if (_toolManager == null)
            return Array.Empty<ToolDefinition>();

        return ToolManager.GetAvailableToolsAsync().Result;
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

    #endregion

    #region Tool Execution

    /// <summary>
    /// Execute a tool by name with arguments.
    /// 通过名称和参数执行工具
    /// </summary>
    protected async Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ExecutionContext? context = null,
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
    protected abstract void UpdateActiveToolsInState();

    #endregion

    #region Chat with Tools

    /// <summary>
    /// Chat with the agent (using tools if needed).
    /// 与代理聊天（如果需要会使用工具）
    /// </summary>
    public async Task<ChatResponse> ChatWithToolAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Add user message to conversation history
            AddMessageToHistory(request.Message, AevatarChatRole.User);

            // Build LLM request with tools
            var llmRequest = BuildLLMRequestWithTools(request);

            // Get response from LLM
            var llmResponse = await LLMProvider.GenerateAsync(llmRequest, cancellationToken);

            // Handle function calls if present
            if (llmResponse.AevatarFunctionCall != null)
            {
                return await HandleFunctionCallAsync(request, llmResponse, cancellationToken);
            }

            // Add assistant response to history
            AddMessageToHistory(llmResponse.Content, AevatarChatRole.Assistant);

            // Build and return chat response
            var chatResponse = new ChatResponse
            {
                Content = llmResponse.Content,
                RequestId = request.RequestId,
                ToolCalled = false
            };

            // Add token usage if available
            if (llmResponse.Usage != null)
            {
                chatResponse.Usage = new AevatarTokenUsage
                {
                    PromptTokens = llmResponse.Usage.PromptTokens,
                    CompletionTokens = llmResponse.Usage.CompletionTokens,
                    TotalTokens = llmResponse.Usage.TotalTokens
                };
            }

            // Publish chat response event
            await PublishChatResponseAsync(chatResponse, request.RequestId);

            return chatResponse;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error processing chat with tools request {RequestId}", request.RequestId);
            throw;
        }
    }

    /// <summary>
    /// Build LLM request with tool definitions.
    /// 构建包含工具定义的 LLM 请求
    /// </summary>
    protected virtual AevatarLLMRequest BuildLLMRequestWithTools(ChatRequest request)
    {
        var llmRequest = BuildLLMRequest(request);

        // Add function definitions from tools
        var functionDefs = ToolManager.GenerateFunctionDefinitionsAsync().Result;
        if (functionDefs != null && functionDefs.Count > 0)
        {
            llmRequest.Functions = functionDefs.ToList();
        }

        return llmRequest;
    }

    /// <summary>
    /// Handle function call from LLM.
    /// 处理来自 LLM 的函数调用
    /// </summary>
    private async Task<ChatResponse> HandleFunctionCallAsync(
        ChatRequest request,
        AevatarLLMResponse llmResponse,
        CancellationToken cancellationToken)
    {
        var functionCall = llmResponse.AevatarFunctionCall;
        if (functionCall == null)
        {
            throw new InvalidOperationException("Function call expected but not found");
        }

        Logger?.LogDebug("Executing tool: {ToolName}", functionCall.Name);

        // Parse arguments
        var parameters = ParseToolArguments(functionCall.Arguments);

        // Execute the tool
        var result = await ExecuteToolAsync(
            functionCall.Name,
            parameters,
            cancellationToken: cancellationToken);

        // Add function message to history (simplify to avoid string interpolation issue)
        var message = string.Format("Tool {0} executed", functionCall.Name);
        AddMessageToHistory(message, AevatarChatRole.Function, functionCall.Name);

        // Create follow-up request with tool result
        var followUpRequest = BuildLLMRequestWithTools(request);
        followUpRequest.Messages.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.Function,
            FunctionName = functionCall.Name,
            Content = result.Result?.ToString() ?? "No result"
        });

        // Get final response from LLM
        var followUpResponse = await LLMProvider.GenerateAsync(followUpRequest, cancellationToken);

        // Add assistant response to history
        AddMessageToHistory(followUpResponse.Content, AevatarChatRole.Assistant);

        // Build final response
        var response = new ChatResponse
        {
            Content = followUpResponse.Content,
            RequestId = request.RequestId,
            ToolCalled = true
        };

        // Set tool call info
        response.ToolCall = new ToolCallInfo
        {
            ToolName = functionCall.Name,
            Result = result.Result?.ToString() ?? string.Empty
        };

        // Parse and set arguments map
        if (!string.IsNullOrEmpty(functionCall.Arguments))
        {
            var args = ParseToolArguments(functionCall.Arguments);
            foreach (var arg in args)
            {
                response.ToolCall.Arguments[arg.Key.ToString()] = arg.Value?.ToString() ?? string.Empty;
            }
        }

        // Add token usage if available
        if (followUpResponse.Usage != null)
        {
            response.Usage = new AevatarTokenUsage
            {
                PromptTokens = followUpResponse.Usage.PromptTokens,
                CompletionTokens = followUpResponse.Usage.CompletionTokens,
                TotalTokens = followUpResponse.Usage.TotalTokens
            };
        }

        // Publish events
        await PublishChatResponseAsync(response, request.RequestId);
        await PublishToolExecutionEventAsync(
            functionCall.Name,
            parameters,
            result,
            request.RequestId);

        return response;
    }

    /// <summary>
    /// Add message to conversation history.
    /// 添加消息到对话历史
    /// </summary>
    protected abstract void AddMessageToHistory(string content, AevatarChatRole role, string? name = null);

    /// <summary>
    /// Build basic LLM request from chat request.
    /// 从聊天请求构建基本的 LLM 请求
    /// </summary>
    protected new abstract AevatarLLMRequest BuildLLMRequest(ChatRequest request);

    /// <summary>
    /// Publish chat response event.
    /// 发布聊天响应事件
    /// </summary>
    protected abstract Task PublishChatResponseAsync(ChatResponse response, string requestId);

    /// <summary>
    /// Publish tool execution event.
    /// 发布工具执行事件
    /// </summary>
    protected abstract Task PublishToolExecutionEventAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionResult result,
        string requestId);

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
            Success = result.Success,
            Result = result.Result?.ToString() ?? "",
            Error = result.Error ?? "",
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
