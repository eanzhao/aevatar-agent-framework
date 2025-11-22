using System.Text.Json;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
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
/// Extends AIGAgentBase with custom tool registration and management capabilities.
/// Inheritance: AIGAgentBase -> AIGAgentWithToolBase
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
    /// </summary>
    protected IAevatarToolManager ToolManager
    {
        get
        {
            if (_toolManager == null)
            {
                // Fallback for backward compatibility or if accessed before InitializeAsync
                // But ideally we should throw or warn.
                // For now, let's keep lazy init but without calling RegisterTools synchronously if possible.
                // Actually, EnsureToolManagerInitialized no longer calls RegisterTools, so it's safe(r).
                EnsureToolManagerInitialized();
            }
            return _toolManager!;
        }
    }

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the AIGAgentWithToolBase class.
    /// </summary>
    protected AIGAgentWithToolBase() : base()
    {
        InitializeManagers();
    }

    /// <summary>
    /// Initializes a new instance with dependency injection.
    /// </summary>
    protected AIGAgentWithToolBase(
        IAevatarLLMProvider llmProvider,
        IAevatarToolManager toolManager,
        ILogger? logger = null)
    {
        _toolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
        InitializeManagers();
    }

    /// <summary>
    /// Initialize the AI agent with a named LLM provider from ASP.NET Options.
    /// </summary>
    public override async Task InitializeAsync(
        string providerName,
        Action<AevatarAIAgentConfig>? configAI = null,
        CancellationToken cancellationToken = default)
    {
        await base.InitializeAsync(providerName, configAI, cancellationToken);
        await RegisterToolsAsync();
    }

    /// <summary>
    /// Initialize the AI agent with custom LLM provider configuration.
    /// </summary>
    public override async Task InitializeAsync(
        LLMProviderConfig providerConfig,
        Action<AevatarAIAgentConfig>? configAI = null,
        CancellationToken cancellationToken = default)
    {
        await base.InitializeAsync(providerConfig, configAI, cancellationToken);
        await RegisterToolsAsync();
    }

    #endregion

    #region Tool Registration

    /// <summary>
    /// Register tools asynchronously for this agent. Override in derived classes to register custom tools.
    /// </summary>
    protected virtual async Task RegisterToolsAsync()
    {
        EnsureToolManagerInitialized();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Helper method to register a tool using the new IAevatarTool interface.
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
    /// Get list of registered tools asynchronously.
    /// </summary>
    protected async Task<IReadOnlyList<ToolDefinition>> GetRegisteredToolsAsync()
    {
        if (_toolManager == null)
            return [];

        return await ToolManager.GetAvailableToolsAsync() ?? [];
    }

    /// <summary>
    /// Check if tools are available asynchronously.
    /// </summary>
    protected async Task<bool> HasToolsAsync()
    {
        if (_toolManager == null) return false;
        var tools = await GetRegisteredToolsAsync();
        return tools.Count > 0;
    }

    /// <summary>
    /// Ensure tool manager is initialized.
    /// </summary>
    private void EnsureToolManagerInitialized()
    {
        if (_toolManager != null)
            return;

        _toolManager = CreateToolManager();
        UpdateActiveToolsInState();
    }

    /// <summary>
    /// Creates the tool manager. Override to customize.
    /// </summary>
    protected virtual IAevatarToolManager CreateToolManager()
    {
        // Default implementation creates a AevatarToolManager
        // Use a null logger for now, can be enhanced later
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AevatarToolManager>.Instance;
        return new AevatarToolManager(logger);
    }

    /// <summary>
    /// Initialize history manager and execution coordinator.
    /// </summary>
    private void InitializeManagers()
    {
        _historyManager = new ConversationHistoryManager(State.History);
        // Coordinator will be initialized when needed (requires LLMProvider)
    }

    /// <summary>
    /// Get or create the execution coordinator.
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
            throw new InvalidOperationException(
                "AI Agent must be initialized before use. Call InitializeAsync() first.");

        try
        {
            // 1. Add user message to history
            AddMessageToHistory(request.Message, AevatarChatRole.User);

            // 2. Build LLM request (includes history and tools)
            var llmRequest = await BuildLLMRequestAsync(request, cancellationToken);

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
    protected virtual async Task<AevatarLLMRequest> BuildLLMRequestAsync(ChatRequest request,
        CancellationToken cancellationToken = default)
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
        // Use async method to avoid .Result blocking
        var functionDefs = await ToolManager.GenerateFunctionDefinitionsAsync(cancellationToken);
        if (functionDefs != null && functionDefs.Count > 0)
        {
            llmRequest.Functions = functionDefs.ToList();
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
        var llmRequest = await BuildLLMRequestAsync(request, cancellationToken);

        // Execute tool workflow using coordinator
        var coordinator = GetExecutionCoordinator();
        var (toolResult, finalLLMResponse) = await coordinator.ExecuteToolWorkflowAsync(
            functionCall,
            llmRequest,
            cancellationToken);

        // Publish tool execution event
        await PublishToolExecutionEventAsync(functionCall.Name, new Dictionary<string, object>(), toolResult,
            request.RequestId);

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