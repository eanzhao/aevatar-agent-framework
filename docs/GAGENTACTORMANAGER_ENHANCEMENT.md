# Agent Management Interface Architecture

## Overview
The Agent management functionality has been separated into two distinct interfaces following the Single Responsibility Principle:
- `IGAgentManager` - Type discovery, registration, and metadata management
- `IGAgentActorManager` - Runtime actor instance management and monitoring

## Interface Separation Rationale

### IGAgentManager (New Interface)
**Purpose**: Static type information and plugin system support
- Type discovery and registration
- Event type management
- Metadata queries
- Plugin loading support

### IGAgentActorManager (Updated)
**Purpose**: Runtime actor instance management
- Actor lifecycle management
- Query and retrieval operations
- Health monitoring and diagnostics

## IGAgentManager Interface

### 1. 类型发现 (Type Discovery)
- `GetAvailableAgentTypes()` - Discover all available Agent types
- `GetAvailableEventTypes()` - Discover all available event types
- `GetSupportedEventTypes<TAgent>()` - Get events supported by specific Agent
- `GetSupportedEventTypes(Type)` - Non-generic version for dynamic discovery
- `IsValidAgentType(Type)` - Validate if type is a valid Agent
- `IsValidEventType(Type)` - Validate if type is a valid event

### 2. 类型注册 (Type Registration)
- `RegisterAgentType(Type)` - Register an Agent type
- `UnregisterAgentType(Type)` - Unregister an Agent type
- `RegisterEventType(Type)` - Register an event type
- `UnregisterEventType(Type)` - Unregister an event type

### 3. 元数据 (Metadata)
- `GetAgentMetadata(Type)` - Get metadata for Agent type
- `GetAgentMetadata<TAgent>()` - Generic version for metadata retrieval
- `GetAllAgentMetadata()` - Get all registered Agent metadata

### 4. 插件支持 (Plugin Support)
- `LoadAgentTypesFromAssembly(Assembly)` - Load types from assembly
- `LoadAgentTypesFromPath(string)` - Load types from assembly path
- `UnloadAgentTypesFromAssembly(Assembly)` - Unload types from assembly

## IGAgentActorManager Interface

### 1. 生命周期管理 (Lifecycle Management)
- `CreateAndRegisterAsync` - Create and register single actor
- `CreateBatchAsync` - Batch creation for improved efficiency
- `DeactivateAndUnregisterAsync` - Deactivate single actor
- `DeactivateBatchAsync` - Batch deactivation
- `DeactivateAllAsync` - Deactivate all actors

### 2. 查询和获取 (Query and Retrieval)
- `GetActorAsync` - Get single actor by ID
- `GetActorsAsync` - Batch retrieval by IDs
- `GetAllActorsAsync` - Get all actors
- `GetActorsByTypeAsync<TAgent>` - Filter by type
- `GetActorsByTypeNameAsync` - Filter by type name
- `ExistsAsync` - Check if actor exists
- `GetCountAsync` - Get total count
- `GetCountByTypeAsync<TAgent>` - Count by type

### 3. 监控和诊断 (Monitoring and Diagnostics)
- `GetHealthStatusAsync(Guid)` - Check actor health
- `GetStatisticsAsync()` - Get manager statistics

## Implementation Status

### LocalGAgentActorManager ✅
- Full implementation of all new methods
- Activity time tracking for health monitoring
- Reflection-based type discovery
- Thread-safe operations with `Lock`

### OrleansGAgentActorManager ⏳
- Needs updating to implement new interface methods
- Can leverage Orleans built-in features for:
  - Health monitoring via grain lifecycle
  - Type discovery via grain type resolver
  - Distributed statistics collection

### ProtoActorGAgentActorManager ⏳
- Needs updating to implement new interface methods
- Can utilize Proto.Actor features for:
  - Actor system statistics
  - Message type discovery
  - Cluster-aware operations

## Use Cases

### 1. Dynamic Plugin System
```csharp
// Discover and display available agent types
var availableTypes = manager.GetAvailableAgentTypes();
foreach (var type in availableTypes)
{
    var supportedEvents = manager.GetSupportedEventTypes(type);
    Console.WriteLine($"{type.Name} handles {supportedEvents.Count} event types");
}
```

### 2. Batch Operations
```csharp
// Create multiple agents efficiently
var ids = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid());
var actors = await manager.CreateBatchAsync<WorkerAgent, WorkerState>(ids);
```

### 3. Monitoring Dashboard
```csharp
// Get real-time statistics
var stats = await manager.GetStatisticsAsync();
Console.WriteLine($"Active Actors: {stats.ActiveActors}");
foreach (var (type, count) in stats.ActorsByType)
{
    Console.WriteLine($"  {type}: {count}");
}
```

### 4. Health Checks
```csharp
// Check actor health
var health = await manager.GetHealthStatusAsync(actorId);
if (!health.IsHealthy)
{
    Console.WriteLine($"Actor {actorId} is unhealthy: {health.ErrorMessage}");
}
```

## Benefits

1. **Improved Developer Experience**
   - Type discovery enables dynamic UI/tooling
   - Batch operations improve performance
   - Health monitoring aids debugging

2. **Better Observability**
   - Statistics integration with metrics systems
   - Health status for proactive monitoring
   - Activity tracking for idle detection

3. **Flexibility**
   - Query actors by various criteria
   - Support for dynamic plugin architectures
   - Runtime introspection capabilities

4. **Performance**
   - Batch operations reduce overhead
   - Efficient filtering and counting
   - Optimized for concurrent operations

## Migration Guide

Existing code using `IGAgentActorManager` will continue to work without changes. The interface is backward compatible with only additions, no breaking changes.

To leverage new features:
1. Update runtime-specific implementations
2. Add health monitoring integration
3. Utilize batch operations where applicable
4. Integrate type discovery for dynamic features

## Next Steps

1. Update `OrleansGAgentActorManager` implementation
2. Update `ProtoActorGAgentActorManager` implementation
3. Add unit tests for new functionality
4. Create example demonstrating type discovery
5. Integrate with metrics collection (AgentMetrics)
