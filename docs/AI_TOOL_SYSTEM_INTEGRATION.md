# Aevatar AI Tool System Integration Guide

## Overview

The new Aevatar AI Tool System provides a simplified, developer-friendly approach to registering and using AI tools in agents that inherit from `MEAIGAgentBase`. This system supports both interface-based and delegate-based tool implementations, making it easy for developers to extend agent capabilities.

## Key Features

1. **Dual Implementation Support**: Register tools either by implementing `IAevatarAITool` interface or using simple delegates
2. **Built-in Tools**: Pre-registered core tools for memory search and event publishing
3. **Lightweight Context**: `AevatarAIToolContext` provides only framework-related information
4. **Simple Registration**: Override `ConfigureAevatarAITools()` method to register custom tools
5. **Thread-Safe**: Concurrent registration and execution support

## Architecture

### Core Interfaces

```csharp
// Simplified AI tool interface
public interface IAevatarAITool
{
    string Name { get; }
    string Description { get; }
    Task<AevatarAIToolResult> ExecuteAsync(
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default);
}

// Tool execution result
public class AevatarAIToolResult
{
    public bool Success { get; init; }
    public object Data { get; init; }
    public string ErrorMessage { get; init; }
    public Dictionary<string, object> Metadata { get; init; }

    public static AevatarAIToolResult Success(object data = null);
    public static AevatarAIToolResult Failure(string errorMessage);
}

// Lightweight tool context
public class AevatarAIToolContext
{
    public string AgentId { get; init; }
    public IServiceProvider ServiceProvider { get; init; }
    public CancellationToken CancellationToken { get; init; }

    public T GetService<T>() where T : notnull;
    public T? GetConfiguration<T>(string? section = null) where T : class, new();
}
```

### Tool Manager

```csharp
public interface IAevatarAIToolManager
{
    void RegisterAevatarAITool(IAevatarAITool tool);
    void RegisterAevatarAITool(
        string name,
        string description,
        Func<AevatarAIToolContext, Dictionary<string, object>, CancellationToken, Task<AevatarAIToolResult>> executeFunc);

    IAevatarAITool GetAevatarAITool(string name);
    List<IAevatarAITool> GetAllAevatarAITools();
    bool AevatarAIToolExists(string name);

    Task<AevatarAIToolResult> ExecuteAevatarAIToolAsync(
        string toolName,
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default);
}
```

## Built-in Tools

### 1. Memory Search Tool (`search_memory`)

Searches agent memory for relevant information.

**Parameters:**
- `query` (string, required): Search query
- `maxResults` (int, optional): Maximum results to return (default: 10)
- `memoryType` (string, optional): Type of memory to search (`all`, `working`, `conversation`, `longterm`, default: `all`)

**Example Usage:**
```csharp
var result = await ExecuteAevatarAIToolAsync(
    "search_memory",
    new Dictionary<string, object>
    {
        { "query", "important information" },
        { "maxResults", 5 },
        { "memoryType", "all" }
    });
```

### 2. Event Publisher Tool (`publish_event`)

Publishes events to the agent hierarchy.

**Parameters:**
- `eventType` (string, required): Full type name of the event
- `eventData` (object, required): Event data payload
- `direction` (string, optional): Event direction (`Up`, `Down`, `Bidirectional`, default: `Bidirectional`)

**Example Usage:**
```csharp
var result = await ExecuteAevatarAIToolAsync(
    "publish_event",
    new Dictionary<string, object>
    {
        { "eventType", "MyNamespace.MyEvent" },
        { "eventData", new { message = "Hello", timestamp = DateTime.UtcNow } },
        { "direction", "Bidirectional" }
    });
```

## Implementation Guide

### Step 1: Create Your Agent

```csharp
public class MyAgent : MEAIGAgentBase<MyAgentState>
{
    protected override string SystemPrompt => "You are a helpful assistant with tool access.";

    public MyAgent(IChatClient chatClient, ILogger? logger = null)
        : base(chatClient, logger)
    {
    }
}
```

### Step 2: Configure AI Tools

Override the `ConfigureAevatarAITools()` method to register your custom tools:

```csharp
protected override void ConfigureAevatarAITools()
{
    // Method 1: Implement IAevatarAITool interface
    var customTool = new MyCustomTool(Logger);
    AevatarAIToolManager.RegisterAevatarAITool(customTool);

    // Method 2: Use delegate registration (simpler)
    AevatarAIToolManager.RegisterAevatarAITool(
        "get_weather",
        "Get weather information for a location",
        async (context, parameters, cancellationToken) =>
        {
            var location = parameters.GetValueOrDefault("location")?.ToString();
            if (string.IsNullOrEmpty(location))
                return AevatarAIToolResult.Failure("Location parameter is required");

            // Your weather API logic here
            var weather = await GetWeatherAsync(location);
            return AevatarAIToolResult.Success(new { location, weather });
        });

    // Method 3: Another delegate example
    AevatarAIToolManager.RegisterAevatarAITool(
        "calculate",
        "Perform mathematical calculations",
        async (context, parameters, cancellationToken) =>
        {
            var expression = parameters.GetValueOrDefault("expression")?.ToString();
            if (string.IsNullOrEmpty(expression))
                return AevatarAIToolResult.Failure("Expression parameter is required");

            try
            {
                var result = EvaluateExpression(expression);
                return AevatarAIToolResult.Success(new { expression, result });
            }
            catch (Exception ex)
            {
                return AevatarAIToolResult.Failure($"Calculation error: {ex.Message}");
            }
        });
}
```

### Step 3: Implement Interface-Based Tools

```csharp
public class MyCustomTool : IAevatarAITool
{
    private readonly ILogger _logger;

    public string Name => "my_custom_tool";
    public string Description => "Performs custom data processing";

    public MyCustomTool(ILogger? logger)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<AevatarAIToolResult> ExecuteAsync(
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get parameters
            var input = parameters.GetValueOrDefault("input")?.ToString();
            var operation = parameters.GetValueOrDefault("operation")?.ToString() ?? "default";

            if (string.IsNullOrWhiteSpace(input))
            {
                return AevatarAIToolResult.Failure("Input parameter is required");
            }

            // Process data
            var result = await ProcessDataAsync(input, operation, cancellationToken);

            _logger.LogInformation("Custom tool processed {Input} with operation {Operation}",
                input, operation);

            return AevatarAIToolResult.Success(new
            {
                input,
                result,
                operation,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Custom tool execution failed");
            return AevatarAIToolResult.Failure($"Tool execution failed: {ex.Message}");
        }
    }

    private async Task<string> ProcessDataAsync(string input, string operation, CancellationToken cancellationToken)
    {
        // Your processing logic here
        await Task.Delay(10, cancellationToken); // Simulate async work

        return operation.ToLower() switch
        {
            "uppercase" => input.ToUpper(),
            "lowercase" => input.ToLower(),
            "reverse" => new string(input.Reverse().ToArray()),
            _ => input
        };
    }
}
```

### Step 4: Execute Tools

```csharp
// In your agent methods or event handlers
public async Task<string> ProcessWithCustomTools(string userInput)
{
    // Execute built-in memory search
    var searchResult = await ExecuteAevatarAIToolAsync(
        "search_memory",
        new Dictionary<string, object>
        {
            { "query", userInput },
            { "maxResults", 3 }
        });

    if (searchResult.Success)
    {
        Logger?.LogInformation("Found {Count} memory items",
            ((dynamic)searchResult.Data).count);
    }

    // Execute custom tool
    var customResult = await ExecuteAevatarAIToolAsync(
        "my_custom_tool",
        new Dictionary<string, object>
        {
            { "input", userInput },
            { "operation", "uppercase" }
        });

    if (customResult.Success)
    {
        var result = ((dynamic)customResult.Data).result;
        return $"Processed result: {result}";
    }

    return "Processing failed";
}
```

## Best Practices

### 1. Error Handling
Always implement proper error handling in your tools:

```csharp
public async Task<AevatarAIToolResult> ExecuteAsync(
    AevatarAIToolContext context,
    Dictionary<string, object> parameters,
    CancellationToken cancellationToken = default)
{
    try
    {
        // Validate parameters
        var requiredParam = parameters.GetValueOrDefault("requiredParam")?.ToString();
        if (string.IsNullOrEmpty(requiredParam))
        {
            return AevatarAIToolResult.Failure("requiredParam is required");
        }

        // Your logic here
        var result = await DoWorkAsync(requiredParam, cancellationToken);

        return AevatarAIToolResult.Success(result);
    }
    catch (OperationCanceledException)
    {
        _logger.LogWarning("Tool execution was cancelled");
        return AevatarAIToolResult.Failure("Tool execution was cancelled");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Tool execution failed");
        return AevatarAIToolResult.Failure($"Tool execution failed: {ex.Message}");
    }
}
```

### 2. Parameter Validation
Validate all input parameters thoroughly:

```csharp
private bool ValidateParameters(Dictionary<string, object> parameters, out string errorMessage)
{
    errorMessage = string.Empty;

    if (!parameters.TryGetValue("threshold", out var thresholdObj))
    {
        errorMessage = "threshold parameter is required";
        return false;
    }

    if (!double.TryParse(thresholdObj.ToString(), out var threshold))
    {
        errorMessage = "threshold must be a valid number";
        return false;
    }

    if (threshold < 0 || threshold > 1)
    {
        errorMessage = "threshold must be between 0 and 1";
        return false;
    }

    return true;
}
```

### 3. Logging
Use appropriate logging levels:

```csharp
_logger.LogDebug("Executing tool {ToolName} with parameters {@Parameters}", Name, parameters);

// For important operations
_logger.LogInformation("Tool {ToolName} completed successfully", Name);

// For errors
_logger.LogError(ex, "Tool {ToolName} failed", Name);

// For warnings
_logger.LogWarning("Tool {ToolName} received invalid parameter {Parameter}", Name, paramName);
```

### 4. Async Operations
Properly handle async operations and cancellation:

```csharp
public async Task<AevatarAIToolResult> ExecuteAsync(
    AevatarAIToolContext context,
    Dictionary<string, object> parameters,
    CancellationToken cancellationToken = default)
{
    try
    {
        // Use cancellation token for all async operations
        var data = await FetchDataAsync(cancellationToken);

        // Check for cancellation periodically
        cancellationToken.ThrowIfCancellationRequested();

        var processed = await ProcessDataAsync(data, cancellationToken);

        return AevatarAIToolResult.Success(processed);
    }
    catch (OperationCanceledException)
    {
        _logger.LogWarning("Tool execution was cancelled");
        return AevatarAIToolResult.Failure("Tool execution was cancelled");
    }
}
```

## Migration from Old System

If you have existing tools using the old system, here's how to migrate:

### Old System (ToolDefinition)
```csharp
protected override void RegisterTools()
{
    var oldTool = new ToolDefinition
    {
        Name = "old_tool",
        Description = "Old tool implementation",
        Category = ToolCategory.Custom,
        ExecuteAsync = async (parameters, context, cancellationToken) =>
        {
            // Old implementation
            return "result";
        }
    };

    RegisterTool(oldTool);
}
```

### New System (Delegate-based)
```csharp
protected override void ConfigureAevatarAITools()
{
    AevatarAIToolManager.RegisterAevatarAITool(
        "old_tool",
        "Old tool implementation",
        async (context, parameters, cancellationToken) =>
        {
            // New implementation - same logic
            return AevatarAIToolResult.Success("result");
        });
}
```

### New System (Interface-based)
```csharp
public class OldToolAdapter : IAevatarAITool
{
    public string Name => "old_tool";
    public string Description => "Old tool implementation";

    public async Task<AevatarAIToolResult> ExecuteAsync(
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        // Adapt old logic to new interface
        var result = await OldToolLogic(parameters, cancellationToken);
        return AevatarAIToolResult.Success(result);
    }
}

protected override void ConfigureAevatarAITools()
{
    AevatarAIToolManager.RegisterAevatarAITool(new OldToolAdapter());
}
```

## Testing

### Unit Testing Tools

```csharp
[Fact]
public async Task Test_MyCustomTool_Success()
{
    // Arrange
    var tool = new MyCustomTool(NullLogger.Instance);
    var context = new AevatarAIToolContext
    {
        AgentId = "test-agent",
        ServiceProvider = EmptyServiceProvider.Instance,
        CancellationToken = CancellationToken.None
    };

    var parameters = new Dictionary<string, object>
    {
        { "input", "test data" },
        { "operation", "uppercase" }
    };

    // Act
    var result = await tool.ExecuteAsync(context, parameters);

    // Assert
    Assert.True(result.Success);
    Assert.NotNull(result.Data);

    dynamic data = result.Data;
    Assert.Equal("TEST DATA", data.result);
}

[Fact]
public async Task Test_MyCustomTool_InvalidParameter()
{
    // Arrange
    var tool = new MyCustomTool(NullLogger.Instance);
    var context = new AevatarAIToolContext
    {
        AgentId = "test-agent",
        ServiceProvider = EmptyServiceProvider.Instance,
        CancellationToken = CancellationToken.None
    };

    var parameters = new Dictionary<string, object>(); // Missing required parameters

    // Act
    var result = await tool.ExecuteAsync(context, parameters);

    // Assert
    Assert.False(result.Success);
    Assert.NotNull(result.ErrorMessage);
    Assert.Contains("required", result.ErrorMessage);
}
```

### Integration Testing

```csharp
[Fact]
public async Task Test_AgentToolIntegration()
{
    // Arrange
    var mockChatClient = new Mock<IChatClient>();
    var agent = new MyTestAgent(mockChatClient.Object);

    // Act
    var result = await agent.ExecuteAevatarAIToolAsync(
        "my_custom_tool",
        new Dictionary<string, object>
        {
            { "input", "test" },
            { "operation", "reverse" }
        });

    // Assert
    Assert.True(result.Success);
    dynamic data = result.Data;
    Assert.Equal("tset", data.result);
}
```

## Summary

The new Aevatar AI Tool System provides:

1. **Simplicity**: Easy registration through `ConfigureAevatarAITools()`
2. **Flexibility**: Support for both interface and delegate implementations
3. **Built-in Tools**: Memory search and event publishing out of the box
4. **Lightweight**: Minimal context with only essential information
5. **Developer-Friendly**: Clear error messages and comprehensive logging

This system makes it straightforward for developers to add AI capabilities to their agents while maintaining clean, testable code.