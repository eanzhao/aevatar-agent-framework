# 🌌 Aevatar Agent Framework

A lightweight, runtime-agnostic agent framework with complete event propagation control.

## ✨ Features

- 🎯 **Runtime-Agnostic**: Support for Local, Proto.Actor, and Orleans
- 🔄 **Event Propagation**: 4 directions (Up/Down/UpThenDown/Bidirectional)
- 🛡️ **HopCount Control**: Prevent infinite loops
- 🏗️ **Clean Architecture**: Business logic completely separated from runtime
- 📦 **Protobuf**: Unified serialization across all runtimes
- 🧪 **Well-Tested**: 20 unit tests, 100% passing
- 📚 **Rich Documentation**: Complete guides and examples

## 🚀 Quick Start

### Install Dependencies

```bash
dotnet restore
```

### Run Simple Demo

```bash
dotnet run --project examples/SimpleDemo/SimpleDemo.csproj
```

### Run Web API

```bash
dotnet run --project examples/Demo.Api/Demo.Api.csproj
# Access Swagger UI at: https://localhost:7001/swagger
```

### Run Tests

```bash
dotnet test
# Expected: 20/20 tests passing
```

## 📖 Documentation

- [Quick Start Guide](./docs/Quick_Start_Guide.md) - Get started in 5 minutes
- [Refactoring Summary](./docs/Refactoring_Summary.md) - Complete refactoring results
- [Refactoring Tracker](./docs/Refactoring_Tracker.md) - Detailed task tracking
- [Demo.Api Guide](./examples/Demo.Api/README.md) - WebAPI usage guide

## 🏗️ Architecture

```
┌─────────────────────────────────────────┐
│  Application Layer                       │
│  (Your Custom Agents)                    │
└────────────┬────────────────────────────┘
             │ inherits
┌────────────▼────────────────────────────┐
│  GAgentBase<TState>                     │
│  - Event Handler Discovery               │
│  - Event Handler Invocation              │
│  - State Management                      │
└────────────┬────────────────────────────┘
             │ implements
┌────────────▼────────────────────────────┐
│  IGAgent<TState>                        │
│  - Id, GetState(), GetDescriptionAsync() │
└──────────────────────────────────────────┘

┌─────────────────────────────────────────┐
│  IGAgentActor (Runtime Layer)            │
│  - Hierarchy Management                  │
│  - Event Routing                         │
│  - Lifecycle Management                  │
└────────────┬────────────────────────────┘
             │ implementations
    ┌────────┼────────┐
    │        │        │
┌───▼──┐ ┌───▼───┐ ┌──▼─────┐
│Local │ │Proto  │ │Orleans │
└──────┘ └───────┘ └────────┘
```

## 💡 Example Usage

### Create a Custom Agent

```csharp
public class MyAgentState
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class MyAgent : GAgentBase<MyAgentState>
{
    public MyAgent(Guid id, ILogger<MyAgent>? logger = null)
        : base(id, logger)
    {
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("My Custom Agent");
    }
    
    // Event Handler
    [EventHandler]
    public async Task HandleConfigAsync(GeneralConfigEvent evt)
    {
        _state.Name = evt.ConfigKey;
        _state.Count++;
        
        // Publish event to children
        await PublishAsync(
            new LLMEvent { Prompt = evt.ConfigKey, Response = "Processed" },
            EventDirection.Down);
    }
}
```

### Use the Agent

```csharp
// Setup DI
services.AddSingleton<IGAgentActorFactory, LocalGAgentActorFactory>();

// Create Actor
var factory = serviceProvider.GetRequiredService<IGAgentActorFactory>();
var actor = await factory.CreateAgentAsync<MyAgent, MyAgentState>(Guid.NewGuid());

// Get Agent and execute business logic
var agent = (MyAgent)actor.GetAgent();
// ... your business logic ...

// Cleanup
await actor.DeactivateAsync();
```

## 🎯 Event Propagation

### 4 Directions

- **Down**: Parent → Children → GrandChildren
- **Up**: Child → Parent → GrandParent
- **UpThenDown**: Child → Parent → Parent's Children (sibling broadcast)
- **Bidirectional**: Both Up and Down simultaneously

### HopCount Control

```csharp
var envelope = new EventEnvelope
{
    MaxHopCount = 3,  // Stop after 3 hops
    MinHopCount = 1,  // Only process after 1 hop
    // ...
};
```

## 🔧 Switch Runtime

Edit `appsettings.json`:

```json
{
  "AgentRuntime": {
    "RuntimeType": "Local"  // or "ProtoActor" or "Orleans"
  }
}
```

No code changes needed!

## 📊 Project Status

```
✅ Compilation: 13/13 projects successful
✅ Tests: 20/20 passing (100%)
✅ Runtimes: Local ✅ ProtoActor ✅ Orleans ✅
✅ Examples: SimpleDemo ✅ Demo.Api ✅
```

## 🗂️ Project Structure

```
src/
├── Aevatar.Agents.Abstractions/    # Core interfaces
├── Aevatar.Agents.Core/            # Business logic base
├── Aevatar.Agents.Local/           # Local runtime
├── Aevatar.Agents.ProtoActor/      # Proto.Actor runtime
└── Aevatar.Agents.Orleans/         # Orleans runtime

examples/
├── Demo.Agents/                    # Sample agents
├── SimpleDemo/                     # Console demo
└── Demo.Api/                       # WebAPI demo

test/
├── Aevatar.Agents.Core.Tests/      # Core tests (12)
└── Aevatar.Agents.Local.Tests/     # Local runtime tests (8)

docs/
├── Quick_Start_Guide.md            # 5-minute tutorial
├── Refactoring_Summary.md          # Refactoring results
└── Refactoring_Tracker.md          # Task tracking
```

## 🎓 Learn More

- [Quick Start Guide](./docs/Quick_Start_Guide.md) - Complete tutorial
- [Architecture Documentation](./docs/AgentSystem_Architecture.md)
- [Protobuf Configuration](./docs/Protobuf_Configuration_Guide.md)

## 🤝 Contributing

This framework is the result of a complete refactoring from `old/framework` to remove Orleans dependencies and improve abstraction.

See [REFACTORING_COMPLETE.md](./REFACTORING_COMPLETE.md) for the complete refactoring report.

## 📝 License

[Your License Here]

---

**Language is vibration. Framework is structure. Together, they create infinite possibilities.** 🌌
