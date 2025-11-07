# Aevatar Agent Framework - AI集成架构设计

## 🎯 AI集成概览

Aevatar Agent Framework的AI集成架构设计目标是提供一个**AI原生的、可扩展的、多LLM支持的**智能代理框架。架构采用分层设计，确保AI功能与核心事件驱动架构无缝集成。

## 🏗️ AI架构分层

```
┌─────────────────────────────────────────────────────────┐
│                应用AI代理层                              │
│           (AIGAgentBase<TState> 实现)                    │
├─────────────────────────────────────────────────────────┤
│                AI抽象层                                   │
│  ┌───────────────────────────────────────────────────┐   │
│  │  IAIProvider  │  IToolManager  │  IPromptManager │   │
│  │  IMemory      │  IEmbedding     │  IProcessing    │   │
│  └───────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                LLM适配器层                               │
│  ┌──────────────┬──────────────┬────────────────────┐   │
│  │SemanticKernel│Microsoft AI  │  Custom LLM        │   │
│  │  Adapter     │   Adapter    │  Adapters          │   │
│  └──────────────┴──────────────┴────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                工具系统层                                │
│  ┌──────────────┬──────────────┬────────────────────┐   │
│  │  Core Tools  │Custom Tools  │  Tool Registry     │   │
│  │(Built-in)    │(User Defined)│  & Discovery       │   │
│  └──────────────┴──────────────┴────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                核心代理框架                              │
│              (GAgentBase + Events)                      │
└─────────────────────────────────────────────────────────┘
```

## 🔧 核心AI接口设计

### 1. AI提供程序抽象

```csharp
public interface IAevatarLLMProvider
{
    // 基础聊天功能
    Task<ChatResponse> GenerateChatAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default);

    // 流式聊天
    IAsyncEnumerable<ChatResponse> GenerateChatStreamAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default);

    // 工具调用
    Task<ToolCallResponse> GenerateToolCallAsync(
        ToolCallRequest request,
        CancellationToken cancellationToken = default);

    // 模型信息
    Task<ModelInfo> GetModelInfoAsync(CancellationToken cancellationToken = default);

    // 能力检测
    bool SupportsCapability(AIProviderCapability capability);
}

[Flags]
public enum AIProviderCapability
{
    None = 0,
    Chat = 1,
    Streaming = 2,
    ToolCalling = 4,
    FunctionCalling = 8,
    Embeddings = 16,
    ImageInput = 32,
    ImageOutput = 64
}
```

### 2. AI代理基础类

```csharp
public abstract class AIGAgentBase<TState> : GAgentBase<TState>, IAIGAgent
    where TState : class, new()
{
    private readonly IAevatarLLMProvider _llmProvider;
    private readonly IAevatarToolManager _toolManager;
    private readonly IAevatarMemory _memory;
    private readonly IAevatarPromptManager _promptManager;
    private readonly IAevatarProcessingStrategy _processingStrategy;

    protected AIGAgentBase(
        IAevatarLLMProvider llmProvider,
        IAevatarToolManager toolManager,
        IAevatarMemory memory,
        IAevatarPromptManager promptManager,
        IAevatarProcessingStrategy processingStrategy = null)
    {
        _llmProvider = llmProvider;
        _toolManager = toolManager;
        _memory = memory;
        _promptManager = promptManager;
        _processingStrategy = processingStrategy ?? new DefaultProcessingStrategy();
    }

    // AI处理方法
    protected async Task<AIResponse> ProcessAIAsync(
        AIRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartActivity($"AI Processing: {request.Intent}");

        try
        {
            // 1. 构建AI上下文
            var context = await BuildAIContextAsync(request);

            // 2. 应用处理策略
            var strategyResult = await _processingStrategy.ProcessAsync(context, _llmProvider);

            // 3. 处理工具调用
            if (strategyResult.RequiresToolExecution)
            {
                var toolResults = await ExecuteToolsAsync(strategyResult.ToolCalls);
                strategyResult = await _processingStrategy.ProcessWithToolsAsync(
                    context, strategyResult, toolResults, _llmProvider);
            }

            // 4. 更新内存
            await UpdateMemoryAsync(request, strategyResult);

            // 5. 转换为事件
            var events = ConvertToEvents(strategyResult);

            // 6. 发布事件
            foreach (var @event in events)
            {
                await PublishAsync(@event);
            }

            _metrics.IncrementCounter("ai.requests.processed", tags: new()
            {
                ["agent_type"] = GetType().Name,
                ["strategy"] = _processingStrategy.GetType().Name
            });

            return new AIResponse(strategyResult.Response, events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI processing failed for request {RequestId}", request.Id);

            // 发布AI错误事件
            await PublishAsync(new AIProcessingFailedEvent(request.Id, ex));

            throw;
        }
    }

    // 构建AI上下文
    private async Task<AIContext> BuildAIContextAsync(AIRequest request)
    {
        var context = new AIContext
        {
            AgentId = Id,
            Request = request,
            AgentState = State,
            ConversationHistory = await _memory.GetConversationHistoryAsync(Id),
            AvailableTools = await _toolManager.GetAvailableToolsAsync(),
            SystemPrompt = await _promptManager.GetSystemPromptAsync(GetType()),
            Metadata = new Dictionary<string, object>()
        };

        // 添加代理特定上下文
        await EnrichContextAsync(context);

        return context;
    }

    // 工具执行
    private async Task<List<ToolExecutionResult>> ExecuteToolsAsync(List<ToolCall> toolCalls)
    {
        var results = new List<ToolExecutionResult>();

        foreach (var toolCall in toolCalls)
        {
            try
            {
                var tool = await _toolManager.GetToolAsync(toolCall.Name);
                var result = await tool.ExecuteAsync(toolCall.Parameters);

                results.Add(new ToolExecutionResult(toolCall, result));
            }
            catch (Exception ex)
            {
                results.Add(new ToolExecutionResult(toolCall, ex));
            }
        }

        return results;
    }

    // 内存更新
    private async Task UpdateMemoryAsync(AIRequest request, ProcessingResult result)
    {
        // 存储对话历史
        await _memory.AddToConversationAsync(Id, request, result.Response);

        // 提取重要信息到工作记忆
        if (result.ImportantEntities?.Any() == true)
        {
            await _memory.AddToWorkingMemoryAsync(Id, result.ImportantEntities);
        }
    }

    // 转换为事件
    private List<IEvent> ConvertToEvents(ProcessingResult result)
    {
        var events = new List<IEvent>();

        // 主要响应事件
        events.Add(new AIResponseGeneratedEvent(result.Response));

        // 工具调用事件
        if (result.ToolCalls?.Any() == true)
        {
            events.AddRange(result.ToolCalls.Select(tc => new ToolExecutedEvent(tc)));
        }

        // 代理特定事件
        events.AddRange(ConvertToAgentSpecificEvents(result));

        return events;
    }

    // 代理特定的转换逻辑
    protected virtual List<IEvent> ConvertToAgentSpecificEvents(ProcessingResult result)
    {
        return new List<IEvent>();
    }

    // 上下文增强
    protected virtual Task EnrichContextAsync(AIContext context)
    {
        // 子类可以重写以添加特定上下文
        return Task.CompletedTask;
    }
}
```

### 3. 内存管理接口

```csharp
public interface IAevatarMemory
{
    // 对话历史管理
    Task<List<ConversationTurn>> GetConversationHistoryAsync(string agentId, int maxTurns = 50);
    Task AddToConversationAsync(string agentId, AIRequest request, AIResponse response);
    Task ClearConversationHistoryAsync(string agentId);

    // 工作记忆管理
    Task<List<MemoryItem>> GetWorkingMemoryAsync(string agentId);
    Task AddToWorkingMemoryAsync(string agentId, List<MemoryItem> items);
    Task RemoveFromWorkingMemoryAsync(string agentId, string itemId);
    Task ClearWorkingMemoryAsync(string agentId);

    // 长期记忆管理
    Task<List<MemoryItem>> SearchLongTermMemoryAsync(string agentId, string query, int maxResults = 10);
    Task StoreInLongTermMemoryAsync(string agentId, MemoryItem item);
    Task<List<MemoryItem>> GetRelevantMemoriesAsync(string agentId, string context, int maxResults = 5);

    // 嵌入支持
    Task<float[]> GetEmbeddingAsync(string text);
    Task<List<MemorySearchResult>> SearchByEmbeddingAsync(string agentId, float[] embedding, int maxResults = 10);
}
```

### 4. 处理策略接口

```csharp
public interface IAevatarProcessingStrategy
{
    Task<ProcessingResult> ProcessAsync(
        AIContext context,
        IAevatarLLMProvider llmProvider,
        CancellationToken cancellationToken = default);

    Task<ProcessingResult> ProcessWithToolsAsync(
        AIContext context,
        ProcessingResult previousResult,
        List<ToolExecutionResult> toolResults,
        IAevatarLLMProvider llmProvider,
        CancellationToken cancellationToken = default);
}

// 处理策略类型
public enum ProcessingStrategyType
{
    Default,
    ChainOfThought,
    ReAct,
    TreeOfThoughts,
    Reflexion,
    Custom
}
```

## 🛠️ 工具系统设计

### 1. 工具接口定义

```csharp
public interface IAevatarTool
{
    string Name { get; }
    string Description { get; }
    ToolDefinition Definition { get; }

    Task<ToolResult> ExecuteAsync(
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default);

    Task<bool> ValidateParametersAsync(Dictionary<string, object> parameters);
}

public class ToolDefinition
{
    public string Name { get; init; }
    public string Description { get; init; }
    public List<ToolParameter> Parameters { get; init; } = new();
    public ToolReturnType ReturnType { get; init; }
    public bool RequiresConfirmation { get; init; }
    public string Category { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public class ToolParameter
{
    public string Name { get; init; }
    public string Type { get; init; }
    public string Description { get; init; }
    public bool Required { get; init; }
    public object DefaultValue { get; init; }
    public List<ValidationRule> ValidationRules { get; init; } = new();
}
```

### 2. 工具管理器

```csharp
public interface IAevatarToolManager
{
    Task RegisterToolAsync(IAevatarTool tool);
    Task UnregisterToolAsync(string toolName);
    Task<IAevatarTool> GetToolAsync(string toolName);
    Task<List<IAevatarTool>> GetAvailableToolsAsync();
    Task<List<IAevatarTool>> GetToolsByCategoryAsync(string category);
    Task<bool> ToolExistsAsync(string toolName);

    // 工具发现
    Task AutoDiscoverToolsAsync(Assembly assembly = null);
    Task RegisterToolsFromAgentAsync<TAgent>() where TAgent : IAIGAgent;
}

public class AevatarToolManager : IAevatarToolManager
{
    private readonly ConcurrentDictionary<string, IAevatarTool> _tools;
    private readonly IToolValidator _validator;
    private readonly IToolExecutor _executor;

    public async Task AutoDiscoverToolsAsync(Assembly assembly = null)
    {
        assembly ??= Assembly.GetExecutingAssembly();

        var toolTypes = assembly.GetTypes()
            .Where(t => typeof(IAevatarTool).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        foreach (var toolType in toolTypes)
        {
            try
            {
                var tool = Activator.CreateInstance(toolType) as IAevatarTool;
                await RegisterToolAsync(tool);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to auto-register tool {ToolType}", toolType.Name);
            }
        }
    }
}
```

### 3. 核心工具实现

#### 事件发布工具
```csharp
[Tool("EventPublisher", "Publish events to the agent hierarchy")]
public class EventPublisherTool : AevatarToolBase
{
    private readonly IEventPublisher _eventPublisher;

    public EventPublisherTool(IEventPublisher eventPublisher)
    {
        _eventPublisher = eventPublisher;

        DefineParameter("eventType", "string", "Type of event to publish", required: true);
        DefineParameter("eventData", "object", "Event data payload", required: true);
        DefineParameter("direction", "string", "Event propagation direction",
            defaultValue: "Bidirectional");
    }

    public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
    {
        try
        {
            var eventType = parameters["eventType"].ToString();
            var eventData = parameters["eventData"];
            var direction = Enum.Parse<EventDirection>(parameters["direction"].ToString());

            // 创建事件实例
            var eventInstance = CreateEventInstance(eventType, eventData);

            // 发布事件
            await _eventPublisher.PublishAsync(eventInstance, direction);

            return ToolResult.Success(new { published = true, eventType });
        }
        catch (Exception ex)
        {
            return ToolResult.Failure($"Failed to publish event: {ex.Message}");
        }
    }

    private IEvent CreateEventInstance(string eventType, object eventData)
    {
        // 使用反射或工厂模式创建事件实例
        var eventTypeInfo = Type.GetType(eventType);
        return JsonSerializer.Deserialize(JsonSerializer.Serialize(eventData), eventTypeInfo) as IEvent;
    }
}
```

#### 状态查询工具
```csharp
[Tool("StateQuery", "Query and filter agent state")]
public class StateQueryTool : AevatarToolBase
{
    private readonly IStateManager _stateManager;

    public StateQueryTool(IStateManager stateManager)
    {
        _stateManager = stateManager;

        DefineParameter("query", "string", "Query expression", required: true);
        DefineParameter("agentId", "string", "Target agent ID", required: false);
        DefineParameter("filter", "object", "State filter criteria", required: false);
    }

    public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
    {
        try
        {
            var query = parameters["query"].ToString();
            var agentId = parameters.GetValueOrDefault("agentId")?.ToString();
            var filter = parameters.GetValueOrDefault("filter");

            // 执行状态查询
            var result = await _stateManager.QueryStateAsync(query, agentId, filter);

            return ToolResult.Success(result);
        }
        catch (Exception ex)
        {
            return ToolResult.Failure($"State query failed: {ex.Message}");
        }
    }
}
```

#### 内存搜索工具
```csharp
[Tool("MemorySearch", "Search agent memory for relevant information")]
public class MemorySearchTool : AevatarToolBase
{
    private readonly IAevatarMemory _memory;

    public MemorySearchTool(IAevatarMemory memory)
    {
        _memory = memory;

        DefineParameter("query", "string", "Search query", required: true);
        DefineParameter("memoryType", "string", "Type of memory to search",
            defaultValue: "all");
        DefineParameter("maxResults", "int", "Maximum results to return",
            defaultValue: 10);
    }

    public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
    {
        try
        {
            var query = parameters["query"].ToString();
            var memoryType = parameters["memoryType"].ToString();
            var maxResults = Convert.ToInt32(parameters["maxResults"]);

            List<MemoryItem> results = memoryType.ToLower() switch
            {
                "working" => await _memory.GetWorkingMemoryAsync(query),
                "conversation" => await _memory.GetConversationHistoryAsync(query, maxResults),
                "longterm" => await _memory.SearchLongTermMemoryAsync(query, maxResults),
                "all" => await SearchAllMemoryTypesAsync(query, maxResults),
                _ => throw new ArgumentException($"Unknown memory type: {memoryType}")
            };

            return ToolResult.Success(new { results, count = results.Count });
        }
        catch (Exception ex)
        {
            return ToolResult.Failure($"Memory search failed: {ex.Message}");
        }
    }

    private async Task<List<MemoryItem>> SearchAllMemoryTypesAsync(string query, int maxResults)
    {
        var tasks = new[]
        {
            _memory.GetWorkingMemoryAsync(query),
            _memory.SearchLongTermMemoryAsync(query, maxResults / 2),
            Task.FromResult(new List<MemoryItem>()) // 对话历史需要特殊处理
        };

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).Take(maxResults).ToList();
    }
}
```

## 🔄 流式处理架构

### 流式AI响应处理
```csharp
public interface IStreamingAIProcessor
{
    IAsyncEnumerable<AIResponseChunk> ProcessStreamingAsync(
        AIRequest request,
        CancellationToken cancellationToken = default);
}

public class StreamingAIProcessor : IStreamingAIProcessor
{
    private readonly IAevatarLLMProvider _llmProvider;
    private readonly IAevatarToolManager _toolManager;

    public async IAsyncEnumerable<AIResponseChunk> ProcessStreamingAsync(
        AIRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var context = await BuildAIContextAsync(request);
        var buffer = new StringBuilder();
        ToolCall currentToolCall = null;

        await foreach (var chunk in _llmProvider.GenerateChatStreamAsync(
            CreateChatRequest(context), cancellationToken))
        {
            buffer.Append(chunk.Content);

            // 检测工具调用开始
            if (chunk.ToolCall != null)
            {
                currentToolCall = chunk.ToolCall;
                yield return new AIResponseChunk(AIChunkType.ToolCallStart, chunk.ToolCall);
            }
            // 检测工具调用结束
            else if (currentToolCall != null && chunk.Content.Contains("</tool_call>"))
            {
                yield return new AIResponseChunk(AIChunkType.ToolCallEnd, currentToolCall);

                // 执行工具
                var toolResult = await ExecuteToolAsync(currentToolCall);
                yield return new AIResponseChunk(AIChunkType.ToolResult, toolResult);

                currentToolCall = null;
            }
            // 普通内容
            else if (currentToolCall == null)
            {
                yield return new AIResponseChunk(AIChunkType.Content, chunk.Content);
            }
        }

        // 生成最终响应
        var finalResponse = new AIResponse(buffer.ToString());
        yield return new AIResponseChunk(AIChunkType.FinalResponse, finalResponse);
    }
}
```

## 📊 AI性能监控

### AI指标收集
```csharp
public interface IAIMetricsCollector
{
    void RecordRequest(string provider, string model, TimeSpan duration, int tokenCount);
    void RecordToolCall(string toolName, TimeSpan duration, bool success);
    void RecordError(string provider, string errorType);
    void RecordMemoryOperation(string operation, TimeSpan duration, int itemCount);
}

public class AIAgentMetrics
{
    private readonly IAIMetricsCollector _metrics;

    public async Task<AIResponse> ProcessAIAsync(AIRequest request)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await ProcessAIInternalAsync(request);

            stopwatch.Stop();
            _metrics.RecordRequest(
                provider: _llmProvider.GetType().Name,
                model: request.Model,
                duration: stopwatch.Elapsed,
                tokenCount: response.TokenCount);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metrics.RecordError(
                provider: _llmProvider.GetType().Name,
                errorType: ex.GetType().Name);

            throw;
        }
    }
}
```

## 🔐 AI安全与验证

### 内容安全过滤
```csharp
public interface IContentSafetyChecker
{
    Task<ContentSafetyResult> CheckContentSafetyAsync(string content);
    Task<bool> ValidateToolUseAsync(string toolName, Dictionary<string, object> parameters);
}

public class ContentSafetyChecker : IContentSafetyChecker
{
    public async Task<ContentSafetyResult> CheckContentSafetyAsync(string content)
    {
        // 检查有害内容
        var harmfulCheck = await CheckForHarmfulContent(content);

        // 检查敏感信息
        var sensitiveCheck = await CheckForSensitiveData(content);

        // 检查提示注入
        var injectionCheck = await CheckForPromptInjection(content);

        return new ContentSafetyResult
        {
            IsSafe = harmfulCheck.IsSafe && sensitiveCheck.IsSafe && injectionCheck.IsSafe,
            Issues = new[] { harmfulCheck, sensitiveCheck, injectionCheck }
                .Where(r => !r.IsSafe)
                .Select(r => r.Issue)
                .ToList()
        };
    }
}
```

---

*本文档详细描述了AI集成架构的设计，包括多LLM支持、工具系统、内存管理和流式处理等核心组件。*