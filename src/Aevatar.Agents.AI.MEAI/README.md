# Aevatar.Agents.AI.MEAI

Microsoft.Extensions.AI Integration for Aevatar Agent Framework

## Overview

This project provides a simplified bridge between Microsoft.Extensions.AI and the Aevatar Agent Framework, enabling seamless integration of agents built with Microsoft's AI abstractions into the Aevatar event-driven distributed framework.

## Key Features

- **SimpleMEAIGAgentBase**: Base class that inherits directly from `GAgentBase` for minimal complexity
- **Direct IChatClient Support**: Use `Microsoft.Extensions.AI.IChatClient` directly without complex adapters
- **Native AITool Support**: Register and use `Microsoft.Extensions.AI.AITool` instances with `AIFunctionFactory`
- **Event-Driven Integration**: Full support for Aevatar's event system via `[EventHandler]` attributes
- **Minimal Migration**: Existing agents require minimal changes to work with Aevatar

## Architecture

```
Your Agent (using IChatClient, AITools)
    ↓
SimpleMEAIGAgentBase (Lightweight Bridge)
    ↓
GAgentBase (Core Aevatar Framework)
    ↓
Event-Driven Distributed Execution
```

## Quick Start

### 1. Create Your Agent

```csharp
using Aevatar.Agents.AI.MEAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

public class MySmartHomeAgent : SimpleMEAIGAgentBase<MyAgentState>
{
    protected override string SystemPrompt => """
        You are a smart home control agent.
        Execute device commands immediately.
        """;
    
    public MySmartHomeAgent(IChatClient chatClient, ILogger<MySmartHomeAgent> logger)
        : base(chatClient, logger)
    {
    }
    
    protected override void RegisterTools()
    {
        // Register your AITools
        Tools.Add(AIFunctionFactory.Create(
            async (string deviceId, string action) =>
            {
                // Your control logic
                await ControlDevice(deviceId, action);
                return $"Device {deviceId} {action}";
            },
            "ControlDevice",
            "Control a smart device"));
    }
    
    // Your existing methods work as-is
    public async Task<string> ProcessCommandAsync(string command, CancellationToken ct = default)
    {
        return await ProcessWithAIAsync(command, ct);
    }
    
    // Handle Aevatar events
    [Aevatar.Agents.Abstractions.EventHandler]
    public async Task HandleDeviceEvent(DeviceControlEvent evt)
    {
        var result = await ProcessCommandAsync(evt.Command);
        await PublishAsync(new DeviceResultEvent { Result = result });
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Smart Home Control Agent");
    }
}
```

### 2. Define State with Protobuf

Define your state and events in a `.proto` file:

```protobuf
syntax = "proto3";

message MyAgentState {
    map<string, string> device_states = 1;
    repeated string recent_commands = 2;
}

message DeviceControlEvent {
    string device_id = 1;
    string command = 2;
}

message DeviceResultEvent {
    string result = 1;
    bool success = 2;
}
```

### 3. Configure the Agent

```csharp
// Option 1: Use existing IChatClient
var chatClient = new AzureOpenAIClient(endpoint, credential)
    .AsChatClient("gpt-4");

var agent = new MySmartHomeAgent(chatClient, logger);

// Option 2: Use configuration
var config = new MEAIConfiguration
{
    Provider = "azure",
    Endpoint = "https://your-instance.openai.azure.com",
    DeploymentName = "gpt-4",
    UseAzureCliAuth = true  // or provide ApiKey
};

var agent = new MySmartHomeAgent(config, logger);
```

## Key Classes

### SimpleMEAIGAgentBase<TState>

The simplified base class that bridges Microsoft.Extensions.AI with Aevatar:

- Inherits from `GAgentBase<TState>` (not the complex `AIGAgentBase`)
- Directly exposes `IChatClient` and `List<AITool>`
- Provides `ProcessWithAIAsync()` for chat completions
- Supports streaming with `ProcessWithAIStreamingAsync()`
- Manages conversation history automatically

### MEAIConfiguration

Simple configuration class for creating ChatClient:

```csharp
public class MEAIConfiguration
{
    public IChatClient? ChatClient { get; set; }  // Pre-configured client
    public string? Provider { get; set; }         // "azure" or "openai"
    public string? Model { get; set; }            // Model name
    public string? Endpoint { get; set; }         // API endpoint
    public string? ApiKey { get; set; }           // API key
    public bool UseAzureCliAuth { get; set; }     // Use Azure CLI auth
}
```

## Benefits

1. **Simplicity**: No complex AI abstractions to implement
2. **Compatibility**: Works directly with Microsoft.Extensions.AI patterns
3. **Event-Driven**: Full access to Aevatar's distributed event system
4. **Minimal Changes**: Your existing agent logic remains unchanged
5. **Performance**: Lightweight bridge with minimal overhead

## Migration Guide

### From Pure Microsoft.Extensions.AI Agent

Before:
```csharp
public class MyAgent
{
    private IChatClient _chatClient;
    
    public async Task<string> ProcessAsync(string input)
    {
        var response = await _chatClient.CompleteAsync(...);
        return response.Message?.Text ?? "";
    }
}
```

After:
```csharp
public class MyAgent : SimpleMEAIGAgentBase<MyState>
{
    protected override string SystemPrompt => "Your prompt";
    
    public async Task<string> ProcessAsync(string input)
    {
        return await ProcessWithAIAsync(input);
    }
    
    public override Task<string> GetDescriptionAsync() => Task.FromResult("My Agent");
}
```

## Important Notes

1. **State Must Use Protobuf**: All state types must be defined in `.proto` files
2. **EventHandler Attribute**: Use `[Aevatar.Agents.Abstractions.EventHandler]`
3. **AITool Registration**: Register tools in `RegisterTools()` override
4. **Conversation Management**: History is managed automatically

## Example Scenarios

### Smart Home Control
- Direct device control via AITools
- Event-driven state updates
- Distributed execution across rooms/zones

### Customer Service Bot
- Natural language processing with IChatClient
- Tool calling for order lookups
- Event streaming for real-time updates

### Workflow Automation
- Process orchestration with events
- AI-powered decision making
- Distributed task execution

## Limitations

This simplified bridge focuses on compatibility over features. For advanced AI capabilities:
- Use Aevatar.Agents.AI.Core abstractions directly
- Implement custom LLM providers
- Build complex memory systems

## See Also

- [Microsoft.Extensions.AI Documentation](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai)
- [Aevatar Agent Framework Documentation](../../README.md)
- [Protobuf Documentation](https://protobuf.dev/)