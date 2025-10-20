# Aevatar Agent Framework

A distributed multi-agent system framework built for .NET 9.0, designed to simulate hierarchical organizational structures with event-driven architecture and support for multiple runtime environments.

## 🌟 Key Features

- **Distributed Multi-Agent System**: Tree-like hierarchical structure simulating organizational management
- **Event-Driven Architecture**: Pure event-driven model with independent streams per agent
- **Multiple Runtime Support**: Local, Orleans, and Proto.Actor runtime implementations
- **High-Performance Serialization**: Google.Protobuf for microsecond-level serialization
- **Event Sourcing**: Asynchronous event storage with version management and snapshots
- **Type-Safe Communication**: Typed event subscriptions with polymorphic payloads
- **Dependency Injection**: Runtime switching through DI configuration
- **Comprehensive Testing**: Full unit test coverage with xUnit and Moq

## 🏗️ Architecture Principles

### Decoupling
- `Aevatar.Agents.Core` and business logic are completely decoupled from specific runtimes
- Runtime logic is isolated in separate projects (`Local`, `Orleans`, `ProtoActor`)

### Modularity
- Projects are split by responsibility (core, serialization, business, runtimes, tests)
- Clean separation of concerns with well-defined interfaces

### Performance
- Protobuf serialization with .NET 9.0 optimizations (including AOT support)
- Low-latency message passing with `System.Threading.Channels` (Local) and Orleans Streams

### Extensibility
- Business logic extends through `GAgentBase<TState>` inheritance
- Runtime switching through dependency injection
- Support for custom state types and event types

## 📁 Project Structure

```
src/
├── Aevatar.Agents.Abstractions/     # Core interfaces and contracts
├── Aevatar.Agents.Core/             # Base agent implementation
├── Aevatar.Agents.Local/            # Local runtime implementation
├── Aevatar.Agents.Orleans/          # Orleans distributed runtime
├── Aevatar.Agents.ProtoActor/       # Proto.Actor runtime implementation
├── Aevatar.Agents.GAgents/          # Business agent implementations
└── Aevatar.Agents.Serialization/    # Protobuf serialization

test/
├── Aevatar.Agents.Core.Tests/       # Core functionality tests
├── Aevatar.Agents.Local.Tests/      # Local runtime tests
├── Aevatar.Agents.ProtoActor.Tests/ # Proto.Actor runtime tests
└── Aevatar.Agents.GAgents.Tests/    # Business agent tests
```

## 🔧 Core Components

### Interfaces

- **`IGAgent<TState>`**: Business logic interface for agents
- **`IGAgentActor`**: Runtime wrapper interface
- **`IMessageStream`**: Message passing interface with typed subscriptions
- **`IMessageSerializer`**: Serialization interface for Protobuf messages
- **`IGAgentFactory`**: Factory interface for dynamic agent creation

### Base Classes

- **`GAgentBase<TState>`**: Abstract base class providing common agent functionality
- **`ProtobufSerializer`**: Dynamic Protobuf serialization implementation

### Message Types

- **`EventEnvelope`**: Event wrapper with version and polymorphic payload
- **`MessageEnvelope`**: Message wrapper for inter-agent communication
- **`LLMEvent`**, **`CodeValidationEvent`**: Business-specific event types

## 🚀 Quick Start

### Prerequisites

- .NET 9.0 SDK
- MongoDB (for Orleans runtime)

### Installation

1. Clone the repository:
```bash
git clone <repository-url>
cd aevatar-agent-framework
```

2. Restore dependencies:
```bash
dotnet restore
```

3. Build the solution:
```bash
dotnet build
```

### Basic Usage

```csharp
// Configure services
services.AddSingleton<IMessageSerializer, ProtobufSerializer>();

// Local runtime
services.AddScoped<IMessageStream>(sp => 
    new LocalMessageStream(sp.GetRequiredService<IMessageSerializer>(), Guid.NewGuid()));
services.AddScoped<IGAgentFactory, LocalGAgentFactory>();

// Register business agents
services.AddScoped<IGAgent<LLMAgentState>, LlmGAgent>();
services.AddScoped<IGAgent<CodingAgentState>, CodingGAgent>();

// Create and use agents
var factory = serviceProvider.GetRequiredService<IGAgentFactory>();
var llmAgent = await factory.CreateAgentAsync<LlmGAgent, LLMAgentState>(Guid.NewGuid());
await llmAgent.AddSubAgentAsync<CodingGAgent, CodingAgentState>();
```

## 🔄 Runtime Support

### Local Runtime
- **Implementation**: `System.Threading.Channels` for message passing
- **Storage**: In-memory event log
- **Use Case**: Development, testing, single-machine deployment
- **Performance**: Near-zero overhead, microsecond latency

### Orleans Runtime
- **Implementation**: Orleans Grains with `JournaledGrain`
- **Storage**: MongoDB for event persistence
- **Use Case**: Production distributed systems
- **Performance**: High throughput, <10ms latency

### Proto.Actor Runtime
- **Implementation**: Proto.Actor framework
- **Storage**: Configurable (in-memory or persistent)
- **Use Case**: Actor model-based distributed systems
- **Performance**: High concurrency, fault tolerance

## 🧪 Testing

The framework includes comprehensive unit tests covering:

- **Core Functionality**: Agent lifecycle, event handling, sub-agent management
- **Serialization**: Protobuf serialization/deserialization with error handling
- **Runtime Implementations**: Local, Proto.Actor runtime behavior
- **Business Agents**: LLM and Coding agent event processing

Run tests:
```bash
dotnet test
```

## ⚡ Performance Optimizations

### Serialization
- Protobuf (3.27.5/3.33.0) for microsecond-level serialization
- Dynamic message type handling
- .NET 9.0 AOT compilation support

### Message Passing
- **Local**: `System.Threading.Channels` for high concurrency
- **Orleans**: Streams for high throughput
- **Proto.Actor**: Actor model for fault tolerance

### Event Sourcing
- **Orleans**: MongoDB asynchronous writes, <10ms latency
- **Local**: In-memory event log, near-zero overhead
- **Snapshots**: Every 100 events to optimize replay performance

## 🔌 Extensibility

### Business Extensions
Create new agent types by inheriting from `GAgentBase<TState>`:

```csharp
public class AnalyticsAgent : GAgentBase<AnalyticsAgentState>
{
    public override async Task RegisterEventHandlersAsync(IMessageStream stream, CancellationToken ct = default)
    {
        await stream.SubscribeAsync<AnalyticsEvent>(async evt =>
        {
            // Process analytics event
            await RaiseEventAsync(evt, ct);
        }, ct);
    }

    public override async Task ApplyEventAsync(EventEnvelope evt, CancellationToken ct = default)
    {
        // Apply event to state
    }
}
```

### Runtime Extensions
Implement new runtimes by creating:
- `IGAgentActor` implementation
- `IMessageStream` implementation  
- `IGAgentFactory` implementation

### Storage Extensions
Support additional storage backends by implementing event persistence interfaces.

## 📊 Event Sourcing

The framework implements Event Sourcing with:

- **Event Generation**: Business logic generates events during processing
- **State Reconstruction**: Agents replay events to rebuild state on activation
- **Version Management**: Events include incremental version numbers for consistency
- **Snapshots**: Periodic state snapshots to reduce replay overhead
- **Optimistic Concurrency**: Version checking prevents data inconsistencies

## 🔧 Configuration

### Local Mode
```json
{
  "EnvironmentMode": "Local"
}
```

### Orleans Mode
```json
{
  "EnvironmentMode": "Orleans",
  "MongoDB": {
    "ConnectionString": "mongodb://localhost:27017"
  }
}
```

## 📈 Monitoring and Observability

- Event version tracking for consistency monitoring
- Stream ID binding for agent communication tracing
- Comprehensive logging through .NET logging infrastructure
- Performance metrics through built-in .NET diagnostics

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Add tests for new functionality
4. Ensure all tests pass
5. Submit a pull request

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 🔗 Related Projects

- [Orleans](https://github.com/dotnet/orleans) - Distributed virtual actor framework
- [Proto.Actor](https://github.com/asynkron/protoactor-dotnet) - Actor model framework
- [Google.Protobuf](https://github.com/protocolbuffers/protobuf) - Protocol Buffers

## 📚 Documentation

- [Architecture Documentation](docs/AgentSystem_Architecture.md) - Detailed system design
- [API Reference](docs/api/) - Complete API documentation
- [Examples](examples/) - Usage examples and tutorials

---

**Built with ❤️ using .NET 9.0 and modern C# features**
