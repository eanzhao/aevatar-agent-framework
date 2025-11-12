using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Extensions;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.Core.Models;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

/// <summary>
/// Level 2: AI Agent with tool/function calling support using state-based conversation.
/// 第二级：使用基于状态的对话管理支持工具/函数调用的AI代理
/// </summary>
/// <typeparam name="TState">The agent state type (must be Protobuf)</typeparam>
public abstract class AIGAgentWithToolBase<TState> : AIGAgentBase<TState>
    where TState : class, IMessage, new()
{
    #region Fields
    
    private readonly IAevatarToolManager _toolManager;
    
    #endregion
    
    #region Properties
    
    /// <summary>
    /// Gets the tool manager.
    /// 获取工具管理器
    /// </summary>
    protected IAevatarToolManager ToolManager => _toolManager;
    
    #endregion
    
    #region Constructors
    
    /// <summary>
    /// Initializes a new instance of the AIGAgentWithToolBase class.
    /// 初始化AIGAgentWithToolBase类的新实例
    /// </summary>
    protected AIGAgentWithToolBase() : base()
    {
        _toolManager = CreateToolManager();
        RegisterTools();
    }
    
    /// <summary>
    /// Initializes a new instance with dependency injection.
    /// 使用依赖注入初始化新实例
    /// </summary>
    protected AIGAgentWithToolBase(
        ILLMProvider llmProvider,
        IAevatarToolManager toolManager,
        ILogger? logger = null) : base(llmProvider, logger)
    {
        _toolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
        RegisterTools();
        UpdateActiveToolsInState();
    }
    
    #endregion
    
    #region Tool Registration
    
    /// <summary>
    /// Register tools for this agent. Override in derived classes.
    /// 为此代理注册工具。在派生类中重写
    /// </summary>
    protected abstract void RegisterTools();
    
    /// <summary>
    /// Helper method to register a tool.
    /// 注册工具的辅助方法
    /// </summary>
    protected void RegisterTool(IAevatarTool tool)
    {
        // Convert IAevatarTool to ToolDefinition
        var definition = new ToolDefinition
        {
            Name = tool.Name,
            Description = tool.Description,
            Parameters = tool.CreateParameters()
        };
        _toolManager.RegisterToolAsync(definition).Wait();
    }
    
    /// <summary>
    /// Creates the tool manager. Override to customize.
    /// 创建工具管理器。重写以自定义
    /// </summary>
    protected virtual IAevatarToolManager CreateToolManager()
    {
        // Return null for now - should be injected
        return null!;
    }
    
    #endregion
    
    #region Chat with Function Calling
    
    /// <summary>
    /// Override chat to include function calling support.
    /// 重写聊天以包含函数调用支持
    /// </summary>
    protected override async Task<ChatResponse> ChatAsync(ChatRequest request)
    {
        try
        {
            // Add user message to conversation history
            var aiState = GetAIState();
            aiState.AddUserMessage(request.Message, Configuration.MaxHistory);
            
            // Build LLM request with functions
            var llmRequest = BuildLLMRequestWithTools(request);
            
            // Get response from LLM
            var llmResponse = await LLMProvider.GenerateAsync(llmRequest);
            
            // Handle function calls if present
            if (llmResponse.AevatarFunctionCall != null)
            {
                return await HandleFunctionCall(request, llmResponse);
            }
            
            // Add assistant response to history
            aiState.AddAssistantMessage(llmResponse.Content, Configuration.MaxHistory);
            
            // Build and return chat response
            return new ChatResponse
            {
                Content = llmResponse.Content,
                Usage = ConvertTokenUsage(llmResponse.Usage),
                RequestId = request.RequestId,
                ToolCalled = false
            };
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error processing chat request with tools");
            throw;
        }
    }
    
    /// <summary>
    /// Build LLM request with tool definitions.
    /// 构建包含工具定义的LLM请求
    /// </summary>
    protected virtual AevatarLLMRequest BuildLLMRequestWithTools(ChatRequest request)
    {
        var llmRequest = base.BuildLLMRequest(request);
        
        // Add function definitions
        var functionDefs = _toolManager.GenerateFunctionDefinitionsAsync().Result;
        if (functionDefs != null && functionDefs.Count > 0)
        {
            llmRequest.Functions = functionDefs.ToList();
        }
        
        return llmRequest;
    }
    
    /// <summary>
    /// Handle function call from LLM.
    /// 处理来自LLM的函数调用
    /// </summary>
    private async Task<ChatResponse> HandleFunctionCall(
        ChatRequest request, 
        AevatarLLMResponse llmResponse)
    {
        var functionCall = llmResponse.AevatarFunctionCall;
        if (functionCall == null)
        {
            throw new InvalidOperationException("Function call expected but not found");
        }
        
        Logger?.LogDebug("Executing tool: {ToolName}", functionCall.Name);
        
        // Execute the tool
        var toolResult = await ExecuteToolAsync(functionCall.Name, functionCall.Arguments);
        
        // Add function message to conversation
        var aiState = GetAIState();
        aiState.AddFunctionMessage(
            functionCall.Name,
            toolResult.Result?.ToString() ?? "No result",
            functionCall.Arguments,
            Configuration.MaxHistory);
        
        // Track tool execution in state
        RecordToolExecution(functionCall.Name, functionCall.Arguments, toolResult);
        
        // Get follow-up response from LLM with tool result
        var followUpRequest = new AevatarLLMRequest
        {
            SystemPrompt = SystemPrompt,
            Messages = aiState.ConversationHistory,
            Settings = GetLLMSettings(request)
        };
        
        var followUpResponse = await LLMProvider.GenerateAsync(followUpRequest);
        
        // Add final response to history
        aiState.AddAssistantMessage(followUpResponse.Content, Configuration.MaxHistory);
        
        return new ChatResponse
        {
            Content = followUpResponse.Content,
            Usage = ConvertTokenUsage(followUpResponse.Usage),
            RequestId = request.RequestId,
            ToolCalled = true,
            ToolCall = new ToolCallInfo
            {
                ToolName = functionCall.Name,
                Arguments = ParseToolArguments(functionCall.Arguments),
                Result = toolResult.Result
            }
        };
    }
    
    /// <summary>
    /// Execute a tool by name with arguments.
    /// 通过名称和参数执行工具
    /// </summary>
    protected async Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName, 
        string argumentsJson)
    {
        var parameters = ParseToolArguments(argumentsJson);
        
        var context = new Aevatar.Agents.AI.Abstractions.ExecutionContext
        {
            AgentId = Id,
            SessionId = Guid.NewGuid().ToString(),
            Metadata = new Dictionary<string, object>()
        };
        
        var result = await _toolManager.ExecuteToolAsync(
            toolName, 
            parameters,
            context);
        
        // Publish tool executed event
        await PublishAsync(new AevatarToolExecutedEvent
        {
            ToolName = toolName,
            Result = result.Result?.ToString() ?? "",
            Success = result.Success
        });
        
        return result;
    }
    
    /// <summary>
    /// Parse tool arguments from JSON string.
    /// 从JSON字符串解析工具参数
    /// </summary>
    private Dictionary<string, object> ParseToolArguments(string argumentsJson)
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
    
    /// <summary>
    /// Convert LLM token usage to our format.
    /// 转换LLM令牌使用量格式
    /// </summary>
    private AevatarTokenUsage? ConvertTokenUsage(AevatarTokenUsage? llmUsage)
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
    
    #region Helper Methods
    
    /// <summary>
    /// Update active tools list in AI state.
    /// 在AI状态中更新活动工具列表
    /// </summary>
    protected void UpdateActiveToolsInState()
    {
        var aiState = GetAIState();
        if (aiState.WorkingMemory == null)
        {
            aiState.WorkingMemory = new WorkingMemory();
        }
        
        // Clear and update active tools list
        aiState.WorkingMemory.ActiveTools.Clear();
        // TODO: Update when tool manager has proper method
        // For now, just mark as having tools available
        if (_toolManager != null)
        {
            aiState.WorkingMemory.ActiveTools.Add("tools_available");
        }
    }
    
    /// <summary>
    /// Record tool execution in state history.
    /// 在状态历史中记录工具执行
    /// </summary>
    private void RecordToolExecution(string toolName, string arguments, ToolExecutionResult result)
    {
        var aiState = GetAIState();
        if (aiState.ToolHistory == null)
        {
            aiState.ToolHistory = new ToolExecutionHistory { MaxHistory = 100 };
        }
        
        var execution = new ToolExecution
        {
            ExecutionId = Guid.NewGuid().ToString(),
            ToolName = toolName,
            Result = result.Result?.ToString() ?? "",
            Success = result.Success,
            Error = result.Error ?? "",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            DurationMs = 0 // TODO: Track actual duration
        };
        
        // Add parameters
        var parsedParams = ParseToolArguments(arguments);
        foreach (var kvp in parsedParams)
        {
            execution.Parameters[kvp.Key] = kvp.Value?.ToString() ?? "";
        }
        
        aiState.ToolHistory.Executions.Add(execution);
        
        // Trim history if needed
        if (aiState.ToolHistory.MaxHistory > 0 && 
            aiState.ToolHistory.Executions.Count > aiState.ToolHistory.MaxHistory)
        {
            var toRemove = aiState.ToolHistory.Executions.Count - aiState.ToolHistory.MaxHistory;
            for (int i = 0; i < toRemove; i++)
            {
                aiState.ToolHistory.Executions.RemoveAt(0);
            }
        }
    }
    
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
    
    
    #endregion
    
    #region Event Handlers
    
    /// <summary>
    /// Handle tool execution request events.
    /// 处理工具执行请求事件
    /// </summary>
    [EventHandlerAttribute]
    protected virtual async Task HandleToolExecutionRequestEvent(ToolExecutionRequestEvent evt)
    {
        var result = await ExecuteToolAsync(evt.ToolName, evt.Arguments);
        
        await PublishAsync(new ToolExecutionResponseEvent
        {
            RequestId = evt.RequestId,
            ToolName = evt.ToolName,
            Success = result.Success,
            Result = result.Result?.ToString() ?? "",
            Error = result.Error
        });
    }
    
    #endregion
}
