# IAevatarTool Interface Guide (Updated After Refactoring)

## Overview

The `IAevatarTool` interface provides a standardized contract for implementing tools in the Aevatar Agent Framework. This design promotes consistency, testability, and extensibility across all tool implementations.

> **Note**: This guide has been updated to reflect the recent refactoring. Key changes include:
> - `AevatarTool` → `ToolDefinition` (clearer purpose)
> - `AevatarAevatarToolParameters` → `ToolParameters` (fixed naming redundancy)
> - Simplified type names throughout the tool system

## Architecture

```
IAevatarTool (Interface)
    ├── AevatarToolBase (Base Class with default implementations)
    │   ├── EventPublisherTool
    │   ├── StateQueryTool  
    │   ├── MemorySearchTool
    │   └── HttpRequestTool (example)
    └── Custom implementations
```

## The IAevatarTool Interface

```csharp
public interface IAevatarTool
{
    // Metadata
    string Name { get; }
    string Description { get; }
    ToolCategory Category { get; }
    string Version { get; }
    IList<string> Tags { get; }
    
    // Factory Methods
    ToolDefinition CreateToolDefinition(ToolContext context, ILogger? logger = null);
    ToolParameters CreateParameters();
    
    // Execution
    Task<object?> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default);
    
    // Validation
    ToolParameterValidationResult ValidateParameters(Dictionary<string, object> parameters);
}
```

## Creating a Tool - Step by Step

### Step 1: Define Your Tool Class

```csharp
using Aevatar.Agents.AI.Abstractions;
using Microsoft.Extensions.Logging;

namespace MyProject.Tools;

public class DatabaseQueryTool : AevatarToolBase
{
    // Implement abstract properties and methods
    public override string Name => "database_query";
    public override string Description => "Execute SQL queries against a database";
    public override ToolCategory Category => ToolCategory.DataProcessing;
    
    // Optional overrides
    public override string Version => "2.0.0";
    public override IList<string> Tags => new[] { "sql", "database", "query" };
}
```

### Step 2: Define Parameters

```csharp
public override ToolParameters CreateParameters()
{
    return new ToolParameters
    {
        Items = new Dictionary<string, AevatarToolParameter>
        {
            ["query"] = new()
            {
                Type = "string",
                Required = true,
                Description = "SQL query to execute"
            },
            ["database"] = new()
            {
                Type = "string",
                Required = true,
                Description = "Target database name"
            },
            ["timeout"] = new()
            {
                Type = "integer",
                DefaultValue = 30,
                Description = "Query timeout in seconds"
            }
        },
        Required = new[] { "query", "database" }
    };
}
```

### Step 3: Implement Execution Logic

```csharp
public override async Task<object?> ExecuteAsync(
    Dictionary<string, object> parameters,
    ToolContext context,
    ILogger? logger,
    CancellationToken cancellationToken)
{
    // 1. Validate parameters
    var validation = ValidateParameters(parameters);
    if (!validation.IsValid)
    {
        return new { success = false, errors = validation.Errors };
    }
    
    // 2. Extract parameters
    var query = parameters["query"]?.ToString();
    var database = parameters["database"]?.ToString();
    var timeout = Convert.ToInt32(parameters.GetValueOrDefault("timeout", 30));
    
    // 3. Execute logic
    try
    {
        logger?.LogInformation("Executing query on {Database}", database);
        
        // Your database logic here
        var results = await ExecuteQueryAsync(query, database, timeout, cancellationToken);
        
        return new
        {
            success = true,
            rowCount = results.Count,
            data = results,
            executionTime = stopwatch.ElapsedMilliseconds
        };
    }
    catch (Exception ex)
    {
        logger?.LogError(ex, "Query execution failed");
        return new { success = false, error = ex.Message };
    }
}
```

### Step 4: Override Optional Methods

```csharp
// Control access and behavior
protected override bool RequiresInternalAccess() => true;  // Needs agent internals
protected override bool CanBeOverridden() => false;        // Cannot be replaced
protected override bool RequiresConfirmation() => true;    // Dangerous operation
protected override bool IsDangerous() => false;            // Not destructive

// Set limits
protected override int? GetRateLimit() => 10;              // 10 calls/minute
protected override TimeSpan? GetTimeout() => TimeSpan.FromSeconds(60);

// Custom validation
public override ToolParameterValidationResult ValidateParameters(
    Dictionary<string, object> parameters)
{
    var result = base.ValidateParameters(parameters);
    
    // Add custom validation
    if (parameters.TryGetValue("query", out var query))
    {
        var sql = query?.ToString() ?? "";
        if (sql.Contains("DROP") || sql.Contains("DELETE"))
        {
            result.Warnings.Add("Query contains potentially dangerous operations");
        }
    }
    
    return result;
}
```

## Using the Tool

### Direct Usage

```csharp
// Create tool instance
var tool = new DatabaseQueryTool();

// Create context
var context = new ToolContext
{
    AgentId = "agent-123",
    // ... other context setup
};

// Create tool definition for registration
var toolDefinition = tool.CreateToolDefinition(context, logger);

// Register with tool manager
await toolManager.RegisterToolAsync(toolDefinition);
```

### In a Custom Provider

```csharp
public class MyToolProvider : DefaultToolProvider
{
    private readonly DatabaseQueryTool _dbTool = new();
    
    protected override async Task<IEnumerable<ToolDefinition>> GetCustomToolsAsync(
        ToolContext context)
    {
        var tools = new List<ToolDefinition>();
        
        // Add custom tool
        tools.Add(_dbTool.CreateToolDefinition(context, Logger));
        
        // Add other tools
        tools.Add(new FileSystemTool().CreateToolDefinition(context, Logger));
        
        return tools;
    }
}
```

### Using the Registry Pattern

```csharp
public static class CustomToolsRegistry
{
    private static readonly Lazy<DatabaseQueryTool> _dbTool = 
        new(() => new DatabaseQueryTool());
        
    public static IAevatarTool DatabaseQuery => _dbTool.Value;
    
    public static IEnumerable<IAevatarTool> GetAllTools()
    {
        yield return DatabaseQuery;
        // ... other tools
    }
}
```

## Core Tool Examples

### EventPublisherTool
- **Purpose**: Publish events to agent streams
- **Category**: Core
- **Key Features**: Event routing, direction control, dynamic message creation

### StateQueryTool
- **Purpose**: Query agent state with JSON Path support
- **Category**: Core
- **Key Features**: Reflection-based access, JSON Path navigation

### MemorySearchTool
- **Purpose**: Semantic search in agent memory
- **Category**: Memory
- **Key Features**: Top-K results, relevance filtering, metadata preservation

### HttpRequestTool (Example)
- **Purpose**: Make HTTP requests to external APIs
- **Category**: Integration
- **Key Features**: Full HTTP control, timeout management, response parsing

## Best Practices

### 1. Parameter Design
- Keep parameters simple and well-documented
- Use enums for limited choices
- Provide sensible defaults
- Validate thoroughly

### 2. Error Handling
- Always validate parameters first
- Return consistent error structures
- Log errors with context
- Handle cancellation properly

### 3. Return Values
- Use consistent success/error patterns
- Include relevant metadata
- Keep responses JSON-serializable
- Add timestamps when relevant

### 4. Security
- Mark dangerous operations appropriately
- Require confirmation for destructive actions
- Validate inputs to prevent injection
- Use `RequiresInternalAccess` wisely

### 5. Performance
- Set appropriate timeouts
- Implement rate limiting
- Cache when possible
- Use async/await properly

## Testing Your Tool

```csharp
[Test]
public async Task TestDatabaseQueryTool()
{
    // Arrange
    var tool = new DatabaseQueryTool();
    var context = new ToolContext { /* setup */ };
    var parameters = new Dictionary<string, object>
    {
        ["query"] = "SELECT * FROM users",
        ["database"] = "testdb"
    };
    
    // Act
    var result = await tool.ExecuteAsync(
        parameters, 
        context, 
        logger, 
        CancellationToken.None);
    
    // Assert
    Assert.IsNotNull(result);
    var response = result as dynamic;
    Assert.IsTrue(response.success);
}

[Test]
public void TestParameterValidation()
{
    var tool = new DatabaseQueryTool();
    var invalidParams = new Dictionary<string, object>
    {
        // Missing required 'query' parameter
        ["database"] = "testdb"
    };
    
    var result = tool.ValidateParameters(invalidParams);
    
    Assert.IsFalse(result.IsValid);
    Assert.Contains("Required parameter 'query' is missing", result.Errors);
}
```

## Migration from Static Methods

### Before (Static Pattern)
```csharp
public static class CoreTools
{
    public static class EventPublisher
    {
        public static ToolDefinition CreateToolDefinition(ToolContext context) { ... }
        public static Task<object?> ExecuteAsync(...) { ... }
    }
}
```

### After (Interface Pattern)
```csharp
public class EventPublisherTool : AevatarToolBase
{
    public override string Name => "publish_event";
    public override ToolDefinition CreateToolDefinition(ToolContext context, ILogger? logger) { ... }
    public override Task<object?> ExecuteAsync(...) { ... }
}
```

## Advantages of the Interface Approach

1. **Consistency**: All tools follow the same pattern
2. **Discoverability**: Tools can be discovered through reflection
3. **Testability**: Easy to mock and test
4. **Extensibility**: Clear extension points
5. **Validation**: Built-in parameter validation
6. **Metadata**: Rich tool metadata for documentation
7. **Type Safety**: Compile-time checking
8. **Dependency Injection**: Tools can be registered as services

## Summary

The `IAevatarTool` interface provides a robust foundation for tool development in the Aevatar Agent Framework. By following this pattern, developers can create consistent, well-documented, and easily testable tools that integrate seamlessly with the framework.
