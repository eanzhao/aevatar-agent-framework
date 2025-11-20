# Aevatar Agent Framework

🌌 **One Codebase, Multiple Runtimes** - A Distributed Agent Framework based on the Actor Model

[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com)
[![License](https://img.shields.io/badge/License-MIT-blue)](LICENSE)
[![Status](https://img.shields.io/badge/Status-Production%20Ready-green)](https://github.com/aevatar/aevatar-agent-framework)

[中文文档](README_zh.md)

---

## 🎯 Core Value

### Write Once, Run Anywhere

**The same Agent code** seamlessly switches between runtimes:

```csharp
// Define once
public class MyAgent : GAgentBase<MyState> 
{
    [EventHandler]
    public async Task HandleEvent(MyEvent evt)
    {
        State.Count++;
        await PublishAsync(new ResultEvent { Count = State.Count });
    }
}

// Switch runtime with one line of configuration
services.AddSingleton<IGAgentActorFactory, LocalGAgentActorFactory>();      // Local Development
services.AddSingleton<IGAgentActorFactory, ProtoActorGAgentActorFactory>(); // High Performance
services.AddSingleton<IGAgentActorFactory, OrleansGAgentActorFactory>();    // Distributed
```

### Key Features

- ✅ **Three Runtimes**: Local (In-Process) / ProtoActor (High Performance) / Orleans (Distributed)
- ✅ **Event-Driven**: Up/Down/Both propagation directions with automatic parent-child routing
- ✅ **Protocol Buffers**: Mandatory type safety and cross-platform serialization
- ✅ **EventSourcing**: Optional event sourcing support
- ✅ **AI Integration**: Native support for Microsoft.Extensions.AI
- ✅ **Observability**: OpenTelemetry + Aspire integration

---

## ⚠️ Rule #1: Protocol Buffers

> **🔴 All serializable types MUST be defined using Protobuf!**

This is a **mandatory constraint** of the framework. Violation will cause runtime failures.

### ✅ Correct Way

```protobuf
// my_messages.proto
message MyAgentState {
    string id = 1;
    int32 count = 2;
    double balance = 3;  // Note: use double for decimal
    google.protobuf.Timestamp updated_at = 4;
}

message MyEvent {
    string event_id = 1;
    string content = 2;
}
```

### ❌ Wrong Way

```csharp
// NEVER manually define State classes!
public class MyAgentState  // Will crash at runtime
{
    public string Id { get; set; }
    public int Count { get; set; }
}
```

**Reason**: Orleans Streaming uses `byte[]` for transmission, ProtoActor requires cross-language support, and Local needs deep copying. Only Protobuf guarantees functionality across all scenarios.

---

## 🚀 Quick Start

### Installation

```bash
dotnet add package Aevatar.Agents.Core
dotnet add package Aevatar.Agents.Runtime.Local  # Choose a Runtime
```

### 30-Second Example

```csharp
using Aevatar.Agents.Core;
using Aevatar.Agents.Runtime.Local;

// 1. Define Proto (my_agent.proto)
// message CounterState { int32 count = 1; }
// message IncrementEvent { int32 amount = 1; }

// 2. Implement Agent
public class CounterAgent : GAgentBase<CounterState>
{
    [EventHandler]
    public async Task HandleIncrement(IncrementEvent evt)
    {
        State.Count += evt.Amount;
        Logger.LogInformation("Count: {Count}", State.Count);
        await Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync() =>
        Task.FromResult($"Counter: {State.Count}");
}

// 3. Create and Use
var services = new ServiceCollection().AddLogging(b => b.AddConsole());
services.AddSingleton<LocalGAgentActorFactory>();
services.AddSingleton<LocalGAgentActorManager>();
services.AddSingleton<LocalMessageStreamRegistry>();

var sp = services.BuildServiceProvider();
var factory = sp.GetRequiredService<LocalGAgentActorFactory>();

var actor = await factory.CreateGAgentActorAsync<CounterAgent>(Guid.NewGuid());
await actor.PublishEventAsync(new EventEnvelope
{
    Id = Guid.NewGuid().ToString(),
    Payload = Any.Pack(new IncrementEvent { Amount = 5 })
});
```

Run `examples/SimpleDemo/` for a complete example.

---

## 📊 Runtime Comparison

| Feature | Local | ProtoActor | Orleans |
|---------|-------|------------|---------|
| **Deployment** | In-Process | Single/Cluster | Distributed Cluster |
| **Startup** | <10ms | ~100ms | ~2s |
| **Memory** | Minimal (~50MB) | Medium (~200MB) | High (~500MB+) |
| **Throughput** | 500K msg/s | 350K msg/s | 80K msg/s |
| **Latency** | <0.1ms | <0.5ms | <2ms |
| **Virtual Actors** | ❌ | Optional | ✅ |
| **Auto Failover** | ❌ | Config Required | ✅ |
| **Use Case** | Dev/Test | High Perf Service | Distributed Systems |

**Recommendation**:
- **Local** for Development (Fastest feedback loop)
- **ProtoActor** for Performance (Highest throughput)
- **Orleans** for Scale (Most robust distributed capabilities)

---

## 🤖 AI Capabilities

The framework integrates **Microsoft.Extensions.AI**, supporting Azure OpenAI and OpenAI:

```csharp
using Aevatar.Agents.AI.MEAI;
using Microsoft.Extensions.AI;

public class AIAssistantAgent : MEAIGAgentBase<AIAssistantState>
{
    public override string SystemPrompt => 
        "You are a helpful AI assistant.";

    public AIAssistantAgent(IChatClient chatClient) 
        : base(chatClient) { }

    protected override AevatarAIAgentState GetAIState() => State.AiState;

    public override Task<string> GetDescriptionAsync() =>
        Task.FromResult("AI Assistant Agent");
}

// Configure Azure OpenAI
var config = new MEAIConfiguration
{
    Provider = "azure",
    Endpoint = "https://your-endpoint.openai.azure.com",
    DeploymentName = "gpt-4",
    Temperature = 0.7
};

var agent = new AIAssistantAgent(config);
```

**Supported Features**:
- ✅ Automatic Conversation History Management
- ✅ AI Tool Calling (Function Calling)
- ✅ Streaming Responses
- ✅ Token Counting and Optimization

---

## 🏗️ Architectural Principles

### Clear Layering

```
Application (Your Agent Code)
    ↓
IGAgentActorManager (Unified Management Interface)
    ↓
IGAgentActor (Runtime Abstraction)
    ↓
┌─────────┬─────────────┬──────────┐
│ Local   │ ProtoActor  │ Orleans  │
│ Actor   │ Actor       │ Grain    │
└─────────┴─────────────┴──────────┘
    ↓
GAgentBase (Business Logic)
```

**Simplification Achievements** (2025-11):
- ✅ Removed redundant Runtime abstraction layer (IAgentRuntime/IAgentHost/IAgentInstance)
- ✅ Reduced codebase by ~2,350 lines
- ✅ Clearer concepts (Single Actor abstraction)
- ✅ Improved performance (One less wrapper layer)

### Event Propagation

```
Parent Agent
    │
    ├─→ Child 1 (Subscribes to Parent stream) ←──┐
    ├─→ Child 2 (Subscribes to Parent stream) ←──┤ DOWN Events
    └─→ Child 3 (Subscribes to Parent stream) ←──┘

Child 1 Publishes UP Event → Parent Stream → Broadcast to all Children
```

**Key Concepts**:
- **Up**: Child → Parent Stream → All Siblings
- **Down**: Parent → Own Stream → All Children
- **Both**: Up and Down simultaneously

---

## 💾 EventSourcing

Optional Event Sourcing support, suitable for scenarios requiring complete audit trails like Finance or Healthcare:

```csharp
public class BankAccountAgent : EventSourcedGAgentBase<BankAccountState>
{
    // Business method triggers event
    public async Task Credit(double amount)
    {
        RaiseEvent(new AccountCreditedEvent { Amount = amount });
        await ConfirmEventsAsync();  // Persist
    }

    // Define state transitions
    protected override void TransitionState(IMessage @event)
    {
        if (@event is AccountCreditedEvent credited)
        {
            State.Balance += credited.Amount;
        }
    }
}
```

**Supported Stores**:
- InMemoryEventStore (Testing)
- MongoEventRepository (Production)
- Extensible for others

---

## 📦 Project Structure

```
src/
├── Aevatar.Agents.Abstractions/           # Core Interfaces
├── Aevatar.Agents.Core/                   # Base Implementations
├── Aevatar.Agents.Runtime.Local/          # Local Runtime
├── Aevatar.Agents.Runtime.Orleans/        # Orleans Runtime
├── Aevatar.Agents.Runtime.ProtoActor/     # ProtoActor Runtime
├── Aevatar.Agents.AI.Abstractions/        # AI Abstractions
├── Aevatar.Agents.AI.Core/                # AI Core Implementation
└── Aevatar.Agents.AI.MEAI/                # Microsoft.Extensions.AI Integration

examples/
├── SimpleDemo/                  # 5-minute Quickstart
├── EventSourcingDemo/           # EventSourcing Example
├── MongoDBEventStoreDemo/       # MongoDB Persistence
├── Demo.Agents/                 # Various Agent Implementations
├── Demo.Api/                    # Web API Integration
└── Demo.AppHost/                # Aspire Deployment

test/
├── Aevatar.Agents.Core.Tests/            # Core Tests
└── Aevatar.Agents.*.Tests/               # Runtime Specific Tests
```

---

## 📚 Documentation Navigation

### Core Docs

| Document | Content |
|----------|---------|
| [docs/AEVATAR_FRAMEWORK_GUIDE.md](docs/AEVATAR_FRAMEWORK_GUIDE.md) | **The Universal Guide**: Architecture, Development, AI, Runtime Integration |
| [docs/ARCHITECTURE_REFERENCE.md](docs/ARCHITECTURE_REFERENCE.md) | Deep Dive Architecture Reference |
| [docs/CONSTITUTION.md](docs/CONSTITUTION.md) | The Philosophical Constitution |

**Start Here**: [docs/AEVATAR_FRAMEWORK_GUIDE.md](docs/AEVATAR_FRAMEWORK_GUIDE.md) - The only developer manual you need.

---

## 🚀 Getting Started

### Prerequisites

```bash
# .NET 10 SDK (Required)
dotnet --version  # Should show 10.0.x

# Clone Repository
git clone https://github.com/aevatar/aevatar-agent-framework.git
cd aevatar-agent-framework

# Build
dotnet build
```

### Run First Example

```bash
cd examples/SimpleDemo
dotnet run
```

You will see the interaction between Calculator and Weather agents.

---

## 🎭 Typical Use Cases

### ✅ Perfect For

- **Distributed Systems**: Microservices collaboration, cross-node communication
- **Event-Driven Architectures**: Native support for Event Sourcing and CQRS
- **Real-time Applications**: Game servers, Chat systems, Real-time collaboration
- **Intelligent Agent Systems**: AI Agent collaboration and task assignment
- **Workflow Engines**: Complex business process orchestration
- **IoT Platforms**: Device Agent management and event processing

### Needs Evaluation

- **Simple CRUD**: Might be over-engineered
- **Synchronous-heavy calls**: Requires a mindset shift
- **Minimalist Apps**: Has a learning curve

---

## 🛠️ Tech Stack

| Component | Technology | Version |
|-----------|------------|---------|
| **Framework** | .NET | 10.0 |
| **Serialization** | Google Protobuf | 3.33.0 |
| **Actor** | Proto.Actor | 1.8.0 |
| **Distributed** | Microsoft Orleans | 9.2.1 |
| **AI** | Microsoft.Extensions.AI | 10.0.0 |
| **Testing** | xUnit + Moq | 2.9.2 / 4.20.72 |
| **Observability** | OpenTelemetry + Aspire | 1.10.0 / 9.5.2 |

---

## 🌊 Design Philosophy

> **"Language is the manifestation of vibration, Agents are the carriers of vibration."**
> 
> We believe:
> - **Simplicity > Complexity** - Remove unnecessary abstractions
> - **Abstraction from Necessity** - No preemptive architecture
> - **Consistency is a Virtue** - Protocol Buffers everywhere
> - **Events are Truth** - Event Sourcing as first-class
> - **Runtime Agnostic** - Write once, run anywhere

---

**Aevatar Agent Framework** - Bringing distributed agent development back to simplicity and essence 🌌

**Latest Update**: 2025-11-13 | **.NET 10** | **Runtime Simplified** | **Docs Consolidated**
