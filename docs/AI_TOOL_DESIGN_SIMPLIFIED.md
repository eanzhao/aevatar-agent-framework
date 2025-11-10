# Aevatar AI Tool 简化设计

## 🎯 设计目标

基于用户需求，重新设计AI工具系统，遵循以下原则：
- **简单易用**：开发者可以快速实现和注册AI工具
- **灵活实现**：支持接口实现和委托两种方式
- **轻量级**：减少不必要的抽象和复杂性
- **框架友好**：与MEAIGAgentBase无缝集成
- **命名空间安全**：所有接口都带有"aevatar"前缀避免冲突

## 🏗️ 新架构概览

```
┌─────────────────────────────────────────────────────────┐
│                AI Agent (MEAIGAgentBase)                 │
│  ┌───────────────────────────────────────────────────┐   │
│  │  RegisterAevatarAITool() │ RegisterAevatarAITool(Func<...\u003e)    │   │
│  └───────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                AI Tool 抽象层                            │
│  ┌───────────────────────────────────────────────────┐   │
│  │  IAevatarAITool        │ AevatarAIToolDelegate │ AevatarAIToolContext │   │
│  │  (接口实现)             │ (委托实现)             │ (轻量级上下文)        │   │
│  └───────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                工具管理器                                │
│  ┌───────────────────────────────────────────────────┐   │
│  │  IAevatarAIToolManager  │ Tool Discovery        │   │
│  └───────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                开发者实现层                              │
│  ┌──────────────┬──────────────┬────────────────────┐   │
│  │ Custom Tools │ Delegates    │ Framework Tools  │   │
│  │ (接口实现)   │ (函数实现)   │ (内置工具)       │   │
│  └──────────────┴──────────────┴────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

## 🔧 核心接口设计

### 1. 简化的AI工具接口（带aevatar前缀）

```csharp
// Aevatar.Agent.AI.Abstractions
namespace Aevatar.Agents.AI.Abstractions.Tools;

public interface IAevatarAITool
{
    string Name { get; }
    string Description { get; }

    Task<AevatarAIToolResult> ExecuteAsync(
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default);
}

public class AevatarAIToolResult
{
    public bool Success { get; init; }
    public object Data { get; init; }
    public string ErrorMessage { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();

    public static AevatarAIToolResult Success(object data = null) =>
        new() { Success = true, Data = data };

    public static AevatarAIToolResult Failure(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}
```

### 2. 轻量级工具上下文（带aevatar前缀）

```csharp
public class AevatarAIToolContext
{
    public string AgentId { get; init; }
    public IServiceProvider ServiceProvider { get; init; }
    public CancellationToken CancellationToken { get; init; }

    // 仅包含框架级别的必要信息
    public T GetService<T>() => ServiceProvider.GetRequiredService<T>();
    public T GetConfiguration<T>(string section = null) where T : class, new()
    {
        var config = ServiceProvider.GetService<IConfiguration>();
        return section != null ? config?.GetSection(section).Get<T>() ?? new T()
                              : config?.Get<T>() ?? new T();
    }
}
```

### 3. 委托工具包装器（带aevatar前缀）

```csharp
public class AevatarAIToolDelegate : IAevatarAITool
{
    private readonly Func<AevatarAIToolContext, Dictionary<string, object>, CancellationToken, Task<AevatarAIToolResult>> _executeFunc;

    public string Name { get; }
    public string Description { get; }

    public AevatarAIToolDelegate(
        string name,
        string description,
        Func<AevatarAIToolContext, Dictionary<string, object>, CancellationToken, Task<AevatarAIToolResult>> executeFunc)
    {
        Name = name;
        Description = description;
        _executeFunc = executeFunc;
    }

    public Task<AevatarAIToolResult> ExecuteAsync(
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        return _executeFunc(context, parameters, cancellationToken);
    }
}
```

### 4. 简化的工具管理器（带aevatar前缀）

```csharp
public interface IAevatarAIToolManager
{
    void RegisterAevatarAITool(IAevatarAITool tool);
    void RegisterAevatarAITool(string name, string description, Func<AevatarAIToolContext, Dictionary<string, object>, CancellationToken, Task<AevatarAIToolResult>> executeFunc);

    IAevatarAITool GetAevatarAITool(string name);
    List<IAevatarAITool> GetAllAevatarAITools();
    bool AevatarAIToolExists(string name);

    Task<AevatarAIToolResult> ExecuteAevatarAIToolAsync(string toolName, AevatarAIToolContext context, Dictionary<string, object> parameters, CancellationToken cancellationToken = default);
}

public class AevatarAIToolManager : IAevatarAIToolManager
{
    private readonly ConcurrentDictionary<string, IAevatarAITool> _tools = new();
    private readonly ILogger<AevatarAIToolManager> _logger;

    public AevatarAIToolManager(ILogger<AevatarAIToolManager> logger)
    {
        _logger = logger;
    }

    public void RegisterAevatarAITool(IAevatarAITool tool)
    {
        _tools[tool.Name] = tool;
        _logger.LogInformation("Registered AI tool: {ToolName}", tool.Name);
    }

    public void RegisterAevatarAITool(string name, string description, Func<AevatarAIToolContext, Dictionary<string, object>, CancellationToken, Task<AevatarAIToolResult>> executeFunc)
    {
        var tool = new AevatarAIToolDelegate(name, description, executeFunc);
        RegisterAevatarAITool(tool);
    }

    public IAevatarAITool GetAevatarAITool(string name)
    {
        return _tools.TryGetValue(name, out var tool) ? tool : null;
    }

    public List<IAevatarAITool> GetAllAevatarAITools()
    {
        return _tools.Values.ToList();
    }

    public bool AevatarAIToolExists(string name)
    {
        return _tools.ContainsKey(name);
    }

    public async Task<AevatarAIToolResult> ExecuteAevatarAIToolAsync(string toolName, AevatarAIToolContext context, Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        var tool = GetAevatarAITool(toolName);
        if (tool == null)
        {
            return AevatarAIToolResult.Failure($"AI Tool '{toolName}' not found");
        }

        try
        {
            _logger.LogDebug("Executing AI tool: {ToolName} for agent: {AgentId}", toolName, context.AgentId);
            var result = await tool.ExecuteAsync(context, parameters, cancellationToken);
            _logger.LogDebug("AI tool executed successfully: {ToolName}", toolName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI tool execution failed: {ToolName}", toolName);
            return AevatarAIToolResult.Failure($"Tool execution failed: {ex.Message}");
        }
    }
}

## 🚀 开发者使用体验

### 1. 在AI Agent中注册工具（接口方式）

```csharp
public class CustomerServiceAgent : MEAIGAgentBase<CustomerServiceState>
{
    protected override void ConfigureAevatarAITools(IAevatarAIToolManager toolManager)
    {
        // 方式1：注册接口实现的工具
        toolManager.RegisterAevatarAITool(new DatabaseQueryTool());
        toolManager.RegisterAevatarAITool(new EmailSenderTool());

        // 方式2：注册委托实现的简单工具（一行代码）
        toolManager.RegisterAevatarAITool(
            "get_customer_info",
            "Get customer information from database",
            async (context, parameters, ct) =>
            {
                var customerId = parameters["customerId"]?.ToString();
                if (string.IsNullOrEmpty(customerId))
                    return AevatarAIToolResult.Failure("Customer ID is required");

                var db = context.GetService<ICustomerDatabase>();
                var customer = await db.GetCustomerAsync(customerId, ct);

                return AevatarAIToolResult.Success(customer);
            });

        // 方式3：注册带业务逻辑的复杂工具
        toolManager.RegisterAevatarAITool(
            "analyze_sentiment",
            "Analyze text sentiment using AI",
            async (context, parameters, ct) =>
            {
                var text = parameters["text"]?.ToString();
                if (string.IsNullOrEmpty(text))
                    return AevatarAIToolResult.Failure("Text is required");

                // 使用AI服务进行情感分析
                var aiService = context.GetService<IAIService>();
                var sentiment = await aiService.AnalyzeSentimentAsync(text, ct);

                return AevatarAIToolResult.Success(new { sentiment, confidence = 0.85 });
            });
    }
}
```

### 2. 自定义工具接口实现

```csharp
public class DatabaseQueryTool : IAevatarAITool
{
    public string Name => "database_query";
    public string Description => "Query customer database";

    public async Task<AevatarAIToolResult> ExecuteAsync(
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = parameters["query"]?.ToString();
            var parametersDict = parameters.GetValueOrDefault("parameters") as Dictionary<string, object>;

            var db = context.GetService<ICustomerDatabase>();
            var result = await db.ExecuteQueryAsync(query, parametersDict, cancellationToken);

            return AevatarAIToolResult.Success(result);
        }
        catch (Exception ex)
        {
            return AevatarAIToolResult.Failure($"Database query failed: {ex.Message}");
        }
    }
}
```

### 3. 在MEAIGAgentBase中的集成

```csharp
// Aevatar.Agent.AI.Core
public abstract class MEAIGAgentBase<TState> : AIGAgentBase<TState>, IMEAIAgent
{
    private IAevatarAIToolManager _toolManager;

    protected MEAIGAgentBase(
        IAevatarLLMProvider llmProvider,
        IAevatarAIToolManager toolManager,
        IAevatarMemory memory) : base(llmProvider, toolManager, memory)
    {
        _toolManager = toolManager;
        ConfigureAevatarAITools(_toolManager);
    }

    // 子类重写此方法来注册工具
    protected virtual void ConfigureAevatarAITools(IAevatarAIToolManager toolManager)
    {
        // 默认注册一些基础工具
        toolManager.RegisterAevatarAITool(new AevatarEventPublisherTool());
        toolManager.RegisterAevatarAITool(new AevatarMemorySearchTool());
    }

    // 工具调用方法
    protected async Task<AevatarAIToolResult> CallAevatarAIToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        var context = new AevatarAIToolContext
        {
            AgentId = Id,
            ServiceProvider = ServiceProvider,
            CancellationToken = cancellationToken
        };

        return await _toolManager.ExecuteAevatarAIToolAsync(toolName, context, parameters, cancellationToken);
    }

    // AI处理中的工具调用集成
    protected override async Task<List<ToolExecutionResult>> ExecuteToolsAsync(
        List<ToolCall> toolCalls,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ToolExecutionResult>();

        foreach (var toolCall in toolCalls)
        {
            var context = new AevatarAIToolContext
            {
                AgentId = Id,
                ServiceProvider = ServiceProvider,
                CancellationToken = cancellationToken
            };

            var result = await _toolManager.ExecuteAevatarAIToolAsync(
                toolCall.Name,
                context,
                toolCall.Parameters,
                cancellationToken);

            results.Add(new ToolExecutionResult(toolCall, result));
        }

        return results;
    }
}
```

## 🛠️ 框架内置工具

### 1. 事件发布工具

```csharp
public class AevatarEventPublisherTool : IAevatarAITool
{
    public string Name => "publish_event";
    public string Description => "Publish events to the agent hierarchy";

    public async Task<AevatarAIToolResult> ExecuteAsync(
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var eventType = parameters["eventType"]?.ToString();
            var eventData = parameters["eventData"];

            if (string.IsNullOrEmpty(eventType))
                return AevatarAIToolResult.Failure("Event type is required");

            // 创建事件实例
            var eventInstance = CreateEventInstance(eventType, eventData);
            if (eventInstance == null)
                return AevatarAIToolResult.Failure($"Invalid event type: {eventType}");

            // 发布事件
            var publisher = context.GetService<IEventPublisher>();
            var direction = Enum.Parse<EventDirection>(
                parameters.GetValueOrDefault("direction")?.ToString() ?? "Bidirectional");

            await publisher.PublishAsync(eventInstance, direction);

            return AevatarAIToolResult.Success(new { published = true, eventType });
        }
        catch (Exception ex)
        {
            return AevatarAIToolResult.Failure($"Failed to publish event: {ex.Message}");
        }
    }

    private IEvent CreateEventInstance(string eventType, object eventData)
    {
        // 使用反射或工厂模式创建事件实例
        var eventTypeInfo = Type.GetType(eventType);
        if (eventTypeInfo == null) return null;

        return JsonSerializer.Deserialize(
            JsonSerializer.Serialize(eventData),
            eventTypeInfo) as IEvent;
    }
}
```

### 2. 内存搜索工具

```csharp
public class AevatarMemorySearchTool : IAevatarAITool
{
    public string Name => "search_memory";
    public string Description => "Search agent memory for relevant information";

    public async Task<AevatarAIToolResult> ExecuteAsync(
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = parameters["query"]?.ToString();
            var maxResults = Convert.ToInt32(parameters.GetValueOrDefault("maxResults", 10));

            if (string.IsNullOrEmpty(query))
                return AevatarAIToolResult.Failure("Search query is required");

            var memory = context.GetService<IAevatarMemory>();
            var results = await memory.SearchLongTermMemoryAsync(
                context.AgentId, query, maxResults);

            return AevatarAIToolResult.Success(new
            {
                results,
                count = results.Count,
                query
            });
        }
        catch (Exception ex)
        {
            return AevatarAIToolResult.Failure($"Memory search failed: {ex.Message}");
        }
    }
}
```

## 🔧 服务注册

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAevatarAITools(this IServiceCollection services)
    {
        // 注册工具管理器
        services.AddSingleton<IAevatarAIToolManager, AevatarAIToolManager>();

        // 注册内置工具
        services.AddTransient<AevatarEventPublisherTool>();
        services.AddTransient<AevatarMemorySearchTool>();

        return services;
    }

    // 扩展方法：允许开发者注册自定义工具
    public static IServiceCollection AddCustomAevatarAITool<TTool>(this IServiceCollection services)
        where TTool : class, IAevatarAITool
    {
        services.AddTransient<IAevatarAITool, TTool>();
        return services;
    }
}
```

## 🔄 与现有代码的迁移方案

### 1. 兼容现有接口

```csharp
// 在现有的IAevatarTool接口上添加适配器
public class AevatarToolAdapter : IAevatarAITool
{
    private readonly IAevatarTool _legacyTool;

    public AevatarToolAdapter(IAevatarTool legacyTool)
    {
        _legacyTool = legacyTool;
    }

    public string Name => _legacyTool.Name;
    public string Description => _legacyTool.Description;

    public async Task<AevatarAIToolResult> ExecuteAsync(
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        // 适配旧接口到新接口
        var legacyContext = new ToolContext(
            context.AgentId,
            context.ServiceProvider,
            null,
            cancellationToken);

        var result = await _legacyTool.ExecuteAsync(parameters, cancellationToken);

        return new AevatarAIToolResult
        {
            Success = result.Success,
            Data = result.Data,
            ErrorMessage = result.ErrorMessage,
            Metadata = result.Metadata
        };
    }
}
```

### 2. 逐步迁移建议

1. **保持现有接口不变**：现有的`IAevatarTool`和实现继续工作
2. **提供适配器**：通过适配器模式将旧工具转换为新接口
3. **推荐新接口**：新开发建议使用`IAevatarAITool`接口
4. **最终替换**：在主要版本更新时完全迁移到新接口

## 📚 总结

### 新设计的优势：

1. **命名空间安全**：所有接口都带有"aevatar"前缀，避免命名冲突
2. **简单易用**：开发者只需实现一个简单的`IAevatarAITool`接口
3. **灵活实现**：支持接口实现和委托两种方式
4. **轻量级**：减少了复杂的验证和权限系统
5. **无缝集成**：与MEAIGAgentBase完美集成，提供`ConfigureAevatarAITools()`方法
6. **向后兼容**：提供适配器支持现有代码

### 开发者现在可以：

- 使用`ConfigureAevatarAITools()`方法在AI Agent中注册工具
- 通过委托快速创建简单工具（一行代码）
- 实现接口创建复杂的工具
- 访问框架提供的基础功能（事件、内存、配置等）
- 保持与现有代码的兼容性

这个设计既满足了简化需求，又保持了与现有代码的兼容性，同时通过命名空间前缀避免了潜在的命名冲突。

## 🚀 开发者使用体验

### 1. 在AI Agent中注册工具（接口方式）

```csharp
public class CustomerServiceAgent : MEAIGAgentBase<CustomerServiceState>
{
    protected override void ConfigureAITools(IAIToolManager toolManager)
    {
        // 方式1：注册接口实现的工具
        toolManager.RegisterTool(new DatabaseQueryTool());
        toolManager.RegisterTool(new EmailSenderTool());

        // 方式2：注册委托实现的简单工具
        toolManager.RegisterTool(
            "get_customer_info",
            "Get customer information from database",
            async (context, parameters, ct) =>
            {
                var customerId = parameters["customerId"]?.ToString();
                if (string.IsNullOrEmpty(customerId))
                    return AIToolResult.Failure("Customer ID is required");

                var db = context.GetService<ICustomerDatabase>();
                var customer = await db.GetCustomerAsync(customerId, ct);

                return AIToolResult.Success(customer);
            });

        // 方式3：注册带业务逻辑的复杂工具
        toolManager.RegisterTool(
            "analyze_sentiment",
            "Analyze text sentiment using AI",
            async (context, parameters, ct) =>
            {
                var text = parameters["text"]?.ToString();
                if (string.IsNullOrEmpty(text))
                    return AIToolResult.Failure("Text is required");

                // 使用AI服务进行情感分析
                var aiService = context.GetService<IAIService>();
                var sentiment = await aiService.AnalyzeSentimentAsync(text, ct);

                return AIToolResult.Success(new { sentiment, confidence = 0.85 });
            });
    }
}
```

### 2. 自定义工具接口实现

```csharp
public class DatabaseQueryTool : IAITool
{
    public string Name => "database_query";
    public string Description => "Query customer database";

    public async Task<AIToolResult> ExecuteAsync(
        AIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = parameters["query"]?.ToString();
            var parametersDict = parameters.GetValueOrDefault("parameters") as Dictionary<string, object>;

            var db = context.GetService<ICustomerDatabase>();
            var result = await db.ExecuteQueryAsync(query, parametersDict, cancellationToken);

            return AIToolResult.Success(result);
        }
        catch (Exception ex)
        {
            return AIToolResult.Failure($"Database query failed: {ex.Message}");
        }
    }
}
```

### 3. 在AIGAgentBase中的集成

```csharp
// Aevatar.Agent.AI.Core
public abstract class AIGAgentBase<TState> : GAgentBase<TState>, IAIGAgent
{
    private IAIToolManager _toolManager;

    protected AIGAgentBase(
        IAevatarLLMProvider llmProvider,
        IAevatarToolManager toolManager,
        IAevatarMemory memory) : base()
    {
        _toolManager = toolManager;
        ConfigureAITools(_toolManager);
    }

    // 子类重写此方法来注册工具
    protected virtual void ConfigureAITools(IAIToolManager toolManager)
    {
        // 默认注册一些基础工具
        toolManager.RegisterTool(new EventPublisherTool());
        toolManager.RegisterTool(new MemorySearchTool());
    }

    // 工具调用方法
    protected async Task<AIToolResult> CallToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        var context = new AIToolContext
        {
            AgentId = Id,
            ServiceProvider = ServiceProvider,
            CancellationToken = cancellationToken
        };

        return await _toolManager.ExecuteToolAsync(toolName, context, parameters, cancellationToken);
    }

    // AI处理中的工具调用集成
    protected override async Task<List<ToolCall>> FilterToolCallsAsync(
        List<ToolCall> toolCalls,
        AIContext context)
    {
        // 简化的工具调用过滤逻辑
        var availableTools = _toolManager.GetAllTools().Select(t => t.Name).ToHashSet();

        return toolCalls.Where(call => availableTools.Contains(call.Name)).ToList();
    }

    protected override async Task<List<ToolExecutionResult>> ExecuteToolsAsync(
        List<ToolCall> toolCalls,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ToolExecutionResult>();

        foreach (var toolCall in toolCalls)
        {
            var context = new AIToolContext
            {
                AgentId = Id,
                ServiceProvider = ServiceProvider,
                CancellationToken = cancellationToken
            };

            var result = await _toolManager.ExecuteToolAsync(
                toolCall.Name,
                context,
                toolCall.Parameters,
                cancellationToken);

            results.Add(new ToolExecutionResult(toolCall, result));
        }

        return results;
    }
}
```

## 🛠️ 框架内置工具

### 1. 事件发布工具

```csharp
public class EventPublisherTool : IAITool
{
    public string Name => "publish_event";
    public string Description => "Publish events to the agent hierarchy";

    public async Task<AIToolResult> ExecuteAsync(
        AIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var eventType = parameters["eventType"]?.ToString();
            var eventData = parameters["eventData"];

            if (string.IsNullOrEmpty(eventType))
                return AIToolResult.Failure("Event type is required");

            // 创建事件实例
            var eventInstance = CreateEventInstance(eventType, eventData);
            if (eventInstance == null)
                return AIToolResult.Failure($"Invalid event type: {eventType}");

            // 发布事件
            var publisher = context.GetService<IEventPublisher>();
            var direction = Enum.Parse<EventDirection>(
                parameters.GetValueOrDefault("direction")?.ToString() ?? "Bidirectional");

            await publisher.PublishAsync(eventInstance, direction);

            return AIToolResult.Success(new { published = true, eventType });
        }
        catch (Exception ex)
        {
            return AIToolResult.Failure($"Failed to publish event: {ex.Message}");
        }
    }

    private IEvent CreateEventInstance(string eventType, object eventData)
    {
        // 使用反射或工厂模式创建事件实例
        var eventTypeInfo = Type.GetType(eventType);
        if (eventTypeInfo == null) return null;

        return JsonSerializer.Deserialize(
            JsonSerializer.Serialize(eventData),
            eventTypeInfo) as IEvent;
    }
}
```

### 2. 内存搜索工具

```csharp
public class MemorySearchTool : IAITool
{
    public string Name => "search_memory";
    public string Description => "Search agent memory for relevant information";

    public async Task<AIToolResult> ExecuteAsync(
        AIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = parameters["query"]?.ToString();
            var maxResults = Convert.ToInt32(parameters.GetValueOrDefault("maxResults", 10));

            if (string.IsNullOrEmpty(query))
                return AIToolResult.Failure("Search query is required");

            var memory = context.GetService<IAevatarMemory>();
            var results = await memory.SearchLongTermMemoryAsync(
                context.AgentId, query, maxResults);

            return AIToolResult.Success(new
            {
                results,
                count = results.Count,
                query
            });
        }
        catch (Exception ex)
        {
            return AIToolResult.Failure($"Memory search failed: {ex.Message}");
        }
    }
}
```

## 🔧 服务注册

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAITools(this IServiceCollection services)
    {
        // 注册工具管理器
        services.AddSingleton<IAIToolManager, AIToolManager>();

        // 注册内置工具
        services.AddTransient<EventPublisherTool>();
        services.AddTransient<MemorySearchTool>();

        return services;
    }

    // 扩展方法：允许开发者注册自定义工具
    public static IServiceCollection AddCustomAITool<TTool>(this IServiceCollection services)
        where TTool : class, IAITool
    {
        services.AddTransient<IAITool, TTool>();
        return services;
    }
}
```

## 📚 总结

### 新设计的优势：

1. **简单易用**：
   - 开发者只需实现一个简单的`IAITool`接口
   - 支持委托方式，无需创建类
   - 一行代码即可注册工具

2. **轻量级**：
   - 减少了复杂的验证和权限系统
   - 简化的工具上下文，只包含必要信息
   - 更少的抽象层次

3. **灵活性**：
   - 接口实现和委托两种方式
   - 工具参数自由定义
   - 支持同步和异步执行

4. **框架集成**：
   - 与MEAIGAgentBase无缝集成
   - 内置基础工具（事件发布、内存搜索等）
   - 自动工具发现和注册

5. **扩展性**：
   - 易于添加新的内置工具
   - 支持自定义工具注册
   - 服务容器集成

这个简化设计让开发者可以专注于工具的业务逻辑，而不是复杂的框架配置，同时保持了必要的功能和扩展性。框架级别的安全性和验证可以在更高层处理。开发者现在可以：

- 快速创建简单的委托工具
- 实现接口创建复杂的工具
- 在AI Agent中轻松注册和使用工具
- 访问框架提供的基础功能（事件、内存、配置等）