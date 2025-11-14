# Actor Layer Architecture Documentation

## Overview

The Actor layer is the runtime abstraction that integrates agents with different actor frameworks (Orleans, ProtoActor, Local). It provides:

- **Actor lifecycle management** (Activate/Deactivate)
- **Message streaming and event routing**
- **Hierarchical relationship management**
- **Automatic dependency injection** (Logger, StateStore, ConfigurationStore)
- **Distributed tracing and metrics**

## Architecture Components

### 1. Core Abstractions

#### IGAgentActor (Abstractions Layer)

```csharp
public interface IGAgentActor : IEventPublisher
{
    Guid Id { get; }
    IGAgent GetAgent();

    // Hierarchy Management
    Task AddChildAsync(Guid childId, CancellationToken ct = default);
    Task RemoveChildAsync(Guid childId, CancellationToken ct = default);
    Task SetParentAsync(Guid parentId, CancellationToken ct = default);
    Task ClearParentAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetChildrenAsync();
    Task<Guid?> GetParentAsync();

    // Event Handling
    Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default);

    // Lifecycle
    Task ActivateAsync(CancellationToken ct = default);
    Task DeactivateAsync(CancellationToken ct = default);
}
```

**Location**: `/src/Aevatar.Agents.Abstractions/IGAgentActor.cs`

**Responsibilities**:
- Defines the contract for all actor implementations
- Provides access to the underlying Agent instance
- Manages hierarchical relationships (parent/child)
- Handles event processing and routing

#### IGAgentActorFactory (Abstractions Layer)

```csharp
public interface IGAgentActorFactory
{
    Task<IGAgentActor> CreateGAgentActorAsync<TAgent>(Guid id, CancellationToken ct = default)
        where TAgent : IGAgent;
}
```

**Location**: `/src/Aevatar.Agents.Abstractions/IGAgentActorFactory.cs`

**Responsibilities**:
- Creates actor instances for specific agent types
- Abstracts runtime-specific actor creation

### 2. Base Implementation

#### GAgentActorBase (Core Layer)

**Location**: `/src/Aevatar.Agents.Core/GAgentActorBase.cs`

**Key Features**:
- Abstract base class for all actor implementations
- Provides event routing and propagation logic
- Implements event deduplication
- Manages hierarchical relationships via EventRouter
- Handles metrics and distributed tracing

**Event Flow**:
```
1. HandleEventAsync() → Entry point
2. EventDeduplication → Check for duplicates
3. EventRouter.ShouldProcessEvent → Filtering
4. ProcessEventAsync() → Call Agent handler
5. EventRouter.ContinuePropagation → Route to children/parent
```

**Key Methods**:
- `HandleEventAsync()`: Main event processing entry point
- `ProcessEventAsync()`: Delegates to Agent's HandleEventAsync
- `PublishEventAsync()`: Publishes events with routing
- `SendToSelfAsync()`: Abstract method for self-messaging
- `SendEventToActorAsync()`: Abstract method for actor-to-actor messaging

### 3. Runtime Implementations

#### 3.1 Local Runtime

**Components**:
- `LocalGAgentActorFactory` → Actor creation and dependency injection
- `LocalGAgentActor` → Actor implementation using LocalMessageStream
- `LocalMessageStreamRegistry` → Stream management
- `LocalMessageStream` → In-memory message streaming

**Dependency Injection**:
```csharp
// In LocalGAgentActorFactory.CreateActorForAgentAsync
AgentLoggerInjector.InjectLogger(agent, _serviceProvider);
AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);           // ✅ State persistence
AgentConfigurationInjector.InjectConfigurationStore(agent, _serviceProvider); // ✅ Config persistence
```

**Event Transport**: In-memory message streams using channels

**Use Cases**:
- Testing and development
- Single-process deployments
- Lightweight scenarios

#### 3.2 Orleans Runtime

**Components**:
- `OrleansGAgentActorFactory` → Actor creation + Grain selection
- `OrleansGAgentActor` → Actor wrapper around Orleans Grain
- Grain types (configurable):
  - `IStandardGAgentGrain` → Standard persistence
  - `IEventSourcingGAgentGrain` → Event sourcing
  - `IJournaledGAgentGrain` → Journaled persistence

**Dependency Injection**:
```csharp
// In OrleansGAgentActorFactory.CreateActorForAgentAsync
AgentLoggerInjector.InjectLogger(agent, _serviceProvider);
AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);           // ✅ State persistence
AgentConfigurationInjector.InjectConfigurationStore(agent, _serviceProvider); // ✅ Config persistence
```

**Grain Selection Logic**:
```csharp
if (options.UseEventSourcing)
{
    if (options.UseJournaledGrain)
        grain = _grainFactory.GetGrain<IJournaledGAgentGrain>(id.ToString());
    else
        grain = _grainFactory.GetGrain<IEventSourcingGAgentGrain>(id.ToString());
}
else
{
    grain = _grainFactory.GetGrain<IStandardGAgentGrain>(id.ToString());
}
```

**Event Transport**: Orleans grain methods + potential Orleans Streams

**Use Cases**:
- Distributed systems
- Cloud deployments
- High availability requirements

#### 3.3 ProtoActor Runtime

**Components**:
- `ProtoActorGAgentActorFactory` → Actor creation
- `ProtoActorGAgentActor` → Actor implementation using ProtoActor
- `ProtoActorMessageStreamRegistry` → Stream registry
- `ProtoActorMessageStream` → Message streaming
- `AgentActor` → ProtoActor actor implementation

**Dependency Injection**:
```csharp
// In ProtoActorGAgentActorFactory.CreateActorForAgentAsync
AgentLoggerInjector.InjectLogger(agent, _serviceProvider);
AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);           // ✅ State persistence
AgentConfigurationInjector.InjectConfigurationStore(agent, _serviceProvider); // ✅ Config persistence
```

**Event Transport**: ProtoActor message streams

**Use Cases**:
- High-performance scenarios
- Actor model purists
- Cross-platform deployments

## Dependency Injection Flow

### Agent Creation Sequence

```
1. Application requests actor creation
   ↓
2. Factory.CreateGAgentActorAsync<TAgent>()
   ↓
3. FactoryProvider.GetFactory(agentType) returns creation lambda
   ↓
4. Lambda creates Agent instance
   ↓
5. Factory.CreateActorForAgentAsync(agent, id)
   ↓
6. Inject dependencies:
   - Logger (AgentLoggerInjector)
   - StateStore (AgentStateStoreInjector)
   - ConfigStore (AgentConfigurationInjector)
   ↓
7. Create Actor wrapper
   ↓
8. actor.ActivateAsync()
   ↓
9. Agent ready for events
```

### Injection Helpers

All located in `/src/Aevatar.Agents.Core/Helpers/`:

#### AgentLoggerInjector
```csharp
public static void InjectLogger(IGAgent agent, IServiceProvider serviceProvider)
```
- Injects `ILogger` into Agent's `Logger` property
- Supports both generic and non-generic logger types

#### AgentStateStoreInjector
```csharp
public static void InjectStateStore(IGAgent agent, IServiceProvider serviceProvider)
```
- Injects `IStateStore<TState>` into Agent's `StateStore` property
- Uses reflection to find StateStore property with correct generic type
- Silent failure if store not registered

#### AgentConfigurationInjector
```csharp
public static void InjectConfigurationStore(IGAgent agent, IServiceProvider serviceProvider)
```
- Injects `IConfigurationStore<TConfig>` into Agent's `ConfigStore` property
- Only injects if Agent inherits from `GAgentBase<TState, TConfig>`
- Silent failure if store not registered

## Event Flow Architecture

### Event Processing Pipeline

```
┌─────────────────────────────────────────────────────────────┐
│                    GAgentActorBase                          │
│              (Abstract base for all actors)                 │
└─────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────┐
│ 1. HandleEventAsync(envelope)                               │
│    - Entry point for all events                             │
└─────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────┐
│ 2. Event Deduplication                                      │
│    - Check if event already processed                       │
│    - Prevents duplicate processing                          │
└─────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────┐
│ 3. EventRouter.ShouldProcessEvent(envelope)                 │
│    - Filter based on direction, publishers, etc.            │
└─────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────┐
│ 4. ProcessEventAsync() → Agent.HandleEventAsync()           │
│    - Delegate to Agent for business logic                   │
│    - State automatically loaded/saved                       │
└─────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────┐
│ 5. EventRouter.ContinuePropagation()                        │
│    - Route to children (DOWN/BOTH)                          │
│    - Route to parent (UP/BOTH)                              │
└─────────────────────────────────────────────────────────────┘
```

### Event Router

**Location**: `/src/Aevatar.Agents.Core/EventRouting/EventRouter.cs`

**Responsibilities**:
- Create EventEnvelope from events
- Manage parent/child relationships
- Route events based on direction (UP/DOWN/BOTH)
- Track event publishers to prevent cycles

**Routing Logic**:
```
EventDirection.Up:
  → Send to self → Send to parent

EventDirection.Down:
  → Send to self → Send to all children

EventDirection.Both:
  → Send to self → Send to parent → Send to all children
```

### Event Deduplication

**Location**: `/src/Aevatar.Agents.Core/EventDeduplication/MemoryCacheEventDeduplicator.cs`

**Mechanism**:
- Uses MemoryCache to track processed event IDs
- Configurable expiration (default: 5 minutes)
- Auto-cleanup of old entries
- Prevents duplicate event processing in loops

## Configuration and State Persistence

### Automatic Load/Save Flow

```
Agent.HandleEventAsync():
  1. Load Configuration (if ConfigStore exists)
     Config = await ConfigStore.LoadAsync(Id) ?? new TConfig();

  2. Load State (if StateStore exists)
     State = await StateStore.LoadAsync(Id) ?? new TState();

  3. Execute event handlers (business logic)
     State.Count++;
     Config.IsEnabled = false;

  4. Save Configuration (if ConfigStore exists)
     await ConfigStore.SaveAsync(Id, Config);

  5. Save State (if StateStore exists)
     await StateStore.SaveAsync(Id, State);
```

This happens automatically for every event processed by:
- `GAgentBase<TState>.HandleEventAsync()`
- `GAgentBase<TState, TConfig>.HandleEventAsync()`

### Store Implementations

#### State Stores
- `InMemoryStateStore<TState>` → Testing, Local runtime
- `MongoDBStateStore<TState>` → Production, scalable persistence
- `EventSourcingStateStore<TState>` → Event sourcing (optional)

#### Configuration Stores
- `InMemoryConfigurationStore<TConfig>` → Testing, Local runtime
- `MongoDBConfigurationStore<TConfig>` → Production, persistent config

## Metrics and Observability

### Automatic Metrics Collection

**Location**: `/src/Aevatar.Agents.Core/Observability/AgentMetrics.cs`

**Metrics Tracked**:
- `EventsHandled` (Counter) → Number of events processed
- `EventsPublished` (Counter) → Number of events published
- `EventsDropped` (Counter) → Events filtered out
- `EventHandlingLatency` (Histogram) → Event processing time
- `EventPublishLatency` (Histogram) → Event publishing time
- `ActiveActors` (UpDownCounter) → Currently active actors
- `Exceptions` (Counter) → Exception counts with type tags

**Usage Example**:
```csharp
AgentMetrics.RecordEventHandled("OrderEvent", agentId, 125.5); // 125.5ms
AgentMetrics.RecordException("DatabaseException", agentId, "SaveState");
```

### Distributed Tracing

**Location**: `/src/Aevatar.Agents.Core/Observability/LoggingScope.cs`

**Features**:
- Creates structured logging scopes for operations
- Correlates logs across event processing
- Includes AgentId, EventId, CorrelationId in all logs

**Usage**:
```csharp
using var scope = LoggingScope.CreateEventHandlingScope(
    Logger, agentId, eventId, eventType, correlationId);

Logger.LogDebug("Processing order"); // Automatically includes scope data
```

## Testing Strategy

### Unit Tests

**Agent Tests**:
- Test event handlers in isolation
- Mock StateStore and ConfigStore
- Verify state transitions
- Verify configuration changes

**Actor Tests**:
- Test event routing and propagation
- Mock underlying transport (streams/grains)
- Verify hierarchy management
- Test activation/deactivation

**Factory Tests**:
- Test dependency injection
- Verify proper store injection
- Test runtime selection logic

### Integration Tests

**Cross-Runtime Tests**:
```csharp
[Theory]
[InlineData(typeof(LocalGAgentActorFactory))]
[InlineData(typeof(OrleansGAgentActorFactory))]
[InlineData(typeof(ProtoActorGAgentActorFactory))]
public async Task AllRuntimes_SupportStatePersistence(Type factoryType)
{
    // Arrange: Create actor with all three runtimes
    // Act: Process event that modifies state
    // Assert: State is persisted and loaded correctly
}
```

### Test Helpers

**In-Memory Stores**:
- `InMemoryStateStore<TState>` for testing
- `InMemoryConfigurationStore<TConfig>` for testing
- Fast, reliable, no external dependencies

**Mock Factories**:
- Create agents without full runtime setup
- Test business logic in isolation

## Best Practices

### 1. Actor Implementation

```csharp
public class MyGAgentActor : GAgentActorBase
{
    public MyGAgentActor(IGAgent agent, ILogger? logger = null)
        : base(agent, logger) { }

    protected override async Task SendToSelfAsync(EventEnvelope envelope, CancellationToken ct)
    {
        // Implementation specific to your transport
        await _myStream.ProduceAsync(envelope, ct);
    }

    protected override async Task SendEventToActorAsync(Guid actorId, EventEnvelope envelope, CancellationToken ct)
    {
        // Implementation specific to your transport
        var targetStream = _streamRegistry.GetStream(actorId);
        await targetStream.ProduceAsync(envelope, ct);
    }

    public override async Task ActivateAsync(CancellationToken ct = default)
    {
        // Subscribe to events
        await _myStream.SubscribeAsync<EventEnvelope>(
            async envelope => await HandleEventAsync(envelope, ct),
            ct);

        // Call Agent activation
        await base.ActivateAsync(ct);
    }

    public override async Task DeactivateAsync(CancellationToken ct = default)
    {
        // Cleanup
        _streamRegistry.Remove(Id);

        // Call Agent deactivation
        await base.DeactivateAsync(ct);
    }
}
```

### 2. Factory Implementation

```csharp
public class MyRuntimeActorFactory : IGAgentActorFactory
{
    private readonly IServiceProvider _serviceProvider;

    public async Task<IGAgentActor> CreateActorForAgentAsync(
        IGAgent agent, Guid id, CancellationToken ct = default)
    {
        // 1. Inject Logger
        AgentLoggerInjector.InjectLogger(agent, _serviceProvider);

        // 2. Inject StateStore (CRITICAL for state persistence)
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        // 3. Inject ConfigurationStore (CRITICAL for config persistence)
        AgentConfigurationInjector.InjectConfigurationStore(agent, _serviceProvider);

        // 4. Create Actor
        var actor = new MyGAgentActor(agent, /* logger */);

        // 5. Activate
        await actor.ActivateAsync(ct);

        return actor;
    }
}
```

### 3. Configuration

```csharp
// In Startup.cs or composition root
services.ConfigGAgentStateStore(options =>
{
    options.StateStore = sp => new MongoDBStateStore<MyState>(database);
    options.ConfigStore = sp => new MongoDBConfigurationStore<MyConfig>(database);
});

services.ConfigGAgent<MyAgent, MyState, MyConfig>();
```

## Runtime Comparison

| Feature | Local | Orleans | ProtoActor |
|---------|-------|---------|------------|
| **Event Transport** | In-memory streams | Grain methods + Streams | ProtoActor streams |
| **State Storage** | InMemory / MongoDB | Grain persistence | InMemory / MongoDB |
| **Distribution** | Single process | Distributed | Distributed |
| **Reliability** | Process-bound | High (clustering) | Medium |
| **Performance** | Very High | High | Very High |
| **Complexity** | Low | Medium | Medium |
| **Use Case** | Testing, Dev | Production cloud | High-perf apps |

## Migration Guide

### Adding a New Runtime

To add a new runtime (e.g., Akka.NET):

1. **Create Factory**:
```csharp
public class AkkaNetGAgentActorFactory : IGAgentActorFactory
{
    public async Task<IGAgentActor> CreateActorForAgentAsync(
        IGAgent agent, Guid id, CancellationToken ct)
    {
        // Inject dependencies
        AgentLoggerInjector.InjectLogger(agent, _serviceProvider);
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentConfigurationInjector.InjectConfigurationStore(agent, _serviceProvider);

        // Create and activate actor
        var actor = new AkkaNetGAgentActor(agent, _actorSystem);
        await actor.ActivateAsync(ct);
        return actor;
    }
}
```

2. **Create Actor**:
```csharp
public class AkkaNetGAgentActor : GAgentActorBase
{
    // Implement abstract methods
    protected override Task SendToSelfAsync(EventEnvelope envelope, CancellationToken ct) { }
    protected override Task SendEventToActorAsync(Guid actorId, EventEnvelope envelope, CancellationToken ct) { }
    public override Task ActivateAsync(CancellationToken ct = default) { }
    public override Task DeactivateAsync(CancellationToken ct = default) { }
}
```

3. **Register in DI**:
```csharp
services.AddSingleton<IGAgentActorFactory, AkkaNetGAgentActorFactory>();
```

## Summary

The Actor layer successfully supports all three runtimes (Local, Orleans, ProtoActor) with:

✅ **State Persistence**: All runtimes inject StateStore via `AgentStateStoreInjector`
✅ **Configuration Persistence**: All runtimes inject ConfigStore via `AgentConfigurationInjector`
✅ **Event Routing**: Unified event routing via EventRouter
✅ **Dependency Injection**: Automatic DI of Logger, Store, ConfigStore
✅ **Observability**: Metrics and tracing across all runtimes
✅ **Hierarchical Management**: Parent/child relationships in all runtimes

The architecture is **consistent** across all three runtimes, ensuring agents behave identically regardless of the underlying actor framework.
