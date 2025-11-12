# Runtime Abstraction Demo

This demo showcases the runtime abstraction capabilities of the Aevatar Agent Framework, demonstrating how agents can run seamlessly across different runtime environments (Local, Orleans, ProtoActor).

## Features Demonstrated

1. **Runtime Independence**: Same agent code runs on all runtimes
2. **Hierarchical Agent Organization**: Parent-child relationships
3. **Event-Driven Communication**: Event propagation with directions (Up/Down)
4. **Host Management**: Creating and managing agent hosts
5. **Runtime Switching**: Easy switching between different runtime implementations

## Available Demos

### 1. Main Demo (`Program.cs`)
The main demo that demonstrates basic agent creation, hierarchy setup, and event communication.

```bash
# Run with Local runtime
dotnet run -- local

# Run with Orleans runtime
dotnet run -- orleans

# Run with ProtoActor runtime
dotnet run -- protoactor
```

### 2. Simple Runtime Demo (`SimpleRuntimeDemo.cs`)
A simplified demo focusing on core runtime abstraction concepts.

```bash
# Run from the demo class
dotnet run -- simple
```

### 3. Runtime Switching Demo (`RuntimeSwitchingDemo.cs`)
Demonstrates switching between different runtimes and benchmarking performance.

```bash
# Run the switching demo
dotnet run -- switch
```

### 4. Host Management Demo (`HostManagementDemo.cs`)
Shows advanced host management capabilities including multiple hosts and cross-host communication.

```bash
# Run the host management demo
dotnet run -- host
```

## Agent Hierarchy

The demo creates the following agent hierarchy:

```
Manager (ManagerAgent)
├── Worker 1 (GreeterAgent)
├── Worker 2 (GreeterAgent)
└── Worker 3 (GreeterAgent)
```

## Event Flow

1. **Down Direction**: Manager sends work requests to workers
2. **Up Direction**: Workers report completion back to manager
3. **Broadcast**: Manager can broadcast to all children

## Key Abstractions

### IAgentRuntime
- Creates and manages agent hosts
- Spawns agent instances
- Provides runtime health checks

### IAgentHost
- Manages agents within a specific runtime environment
- Handles agent lifecycle (start, stop)
- Provides host-level operations

### IAgentInstance
- Runtime-agnostic interface for agent interaction
- Handles event publishing
- Provides agent metadata

## Configuration

Each runtime can be configured through dependency injection:

```csharp
// Local Runtime
services.AddLocalAgentRuntime(config =>
{
    config.HostName = "my-local-host";
    config.MaxConcurrency = 10;
});

// Orleans Runtime
services.AddOrleansAgentRuntime(siloBuilder =>
{
    siloBuilder.UseLocalhostClustering();
    siloBuilder.AddMemoryStreams("AevatarStreams");
});

// ProtoActor Runtime
services.AddProtoActorAgentRuntime(config =>
{
    // ProtoActor configuration
});
```

## Architecture Benefits

1. **Portability**: Deploy the same agents to different environments
2. **Scalability**: Switch from local to distributed runtimes as needed
3. **Testing**: Test with local runtime, deploy with Orleans/ProtoActor
4. **Flexibility**: Choose the best runtime for your use case

## Requirements

- .NET 9.0 or higher
- Protocol Buffers compiler (for message definitions)
- Orleans SDK (for Orleans runtime)
- Proto.Actor SDK (for ProtoActor runtime)

## Troubleshooting

### Console Input Error
If you see "Cannot read keys when either application does not have a console", this is expected when running in certain environments. The demo still completes successfully.

### Port Conflicts (Orleans)
Orleans uses port 11111 by default. If this port is in use, configure a different port:

```csharp
siloBuilder.ConfigureEndpoints(
    siloPort: 11112,
    gatewayPort: 30001
);
```

### Memory Usage
For large-scale demos, increase the heap size:

```bash
export DOTNET_GCHeapHardLimit=2147483648  # 2GB
dotnet run -- orleans
```