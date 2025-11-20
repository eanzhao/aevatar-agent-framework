# Kafka Stream Demo - Technical Notes

## 🎯 Design Intent

This demo was created to showcase how Orleans Persistent Streams can integrate with Apache Kafka in the Aevatar Agent Framework, based on the integration patterns used in Aevatar Station.

## 📋 Current Status

**Status**: ✅ **Production Ready** (Updated: 2025-11-12)

The demo is fully functional with:
- ✅ Protobuf message definitions (kafka_messages.proto)
- ✅ Producer and Consumer agent implementations
- ✅ Docker Compose setup for Kafka infrastructure
- ✅ Comprehensive README documentation
- ✅ **Custom Topic Configuration Support** (NEW)
- ✅ Memory Stream and Kafka Stream both tested and working
- ✅ Performance optimizations (reflection caching, state query fixes)

## 🆕 Latest Updates (2025-11-12)

### Custom Topic Configuration Feature

**Problem Solved**: Framework previously hardcoded stream namespace to `"AevatarAgents"`, preventing custom topic names.

**Solution**: Introduced `StreamingOptions` configuration class:

```csharp
// New: Aevatar.Agents.Abstractions/StreamingOptions.cs
public class StreamingOptions
{
    public string DefaultStreamNamespace { get; set; } = "AevatarAgents";
    public string StreamProviderName { get; set; } = "StreamProvider";
    public bool AllowCustomNamespaces { get; set; } = false;
}
```

**Usage**:
```csharp
services.Configure<StreamingOptions>(options =>
{
    options.DefaultStreamNamespace = "MyApp.Production.Events";
});

// Kafka topic must match!
kafkaOptions.AddTopic("MyApp.Production.Events", ...);
```

**Key Changes**:
- Modified `OrleansGAgentGrain` to load `StreamingOptions` from DI
- Replaced hardcoded `AevatarAgentsOrleansConstants.StreamNamespace` with configurable value
- Updated in 3 locations: `OnActivateAsync`, `AddChildAsync`, `SetParentAsync`

### Critical Design Rule Discovered

**Golden Rule**: `StreamingOptions.DefaultStreamNamespace` **MUST** equal Kafka Topic Name

```
StreamingOptions.DefaultStreamNamespace ⟺ Kafka Topic Name
                    ↓
         Orleans Stream ID Namespace = Kafka Topic
```

**Why This Matters**:
- Orleans routes messages based on Stream Namespace
- Kafka organizes messages by Topic
- If mismatched: messages publish successfully but consumers never receive them (silent failure!)

**Tested Scenarios**:
| Scenario | Namespace | Topic | Result |
|----------|-----------|-------|--------|
| Default | `AevatarAgents` | `AevatarAgents` | ✅ Works |
| Custom | `KafkaDemoTopic` | `KafkaDemoTopic` | ✅ Works |
| Mismatch | `TopicA` | `TopicB` | ❌ Silent failure |

### Performance Optimizations

1. **Reflection Caching in AgentWrapper**:
   - Cached `MethodInfo` for `HandleEventAsync`, `GetState`, `GetDescriptionAsync`
   - Reduced reflection overhead from per-call to per-initialization
   - Improved event processing throughput

2. **State Query Fix**:
   - Changed from local `GetAgent().GetStateAsync()` to `GetStateFromGrainAsync<TState>()`
   - Ensures state is fetched from remote Orleans Grain, not local wrapper
   - Fixed "Messages Consumed: 0" bug

3. **Stream Publishing Optimization**:
   - `OrleansGAgentGrain.PublishEventAsync` now passes `byte[]` directly
   - Removed unnecessary deserialization/serialization roundtrip
   - Only deserializes for logging/debugging

## 🏗️ Architecture Insights

### What We Learned

1. **Agent Stream Model**: Aevatar agents already have built-in event publishing/subscription through `PublishAsync()` and event handlers. The Orleans Stream layer provides the transport mechanism.

2. **Actor-Agent Separation**: 
   - `GAgent`: Business logic (event handlers, state management)
   - `IGAgentActor`: Runtime wrapper (stream management, activation)

3. **Event Propagation**: 
   - Agents publish events via `PublishAsync()`
   - Orleans Streams can be backed by Kafka for distributed scenarios
   - Event handlers use `[EventHandler]` attribute for auto-discovery

### Integration Pattern

```
Agent.PublishAsync(event)
    ↓
Actor.PublishEventAsync(event)
    ↓
Orleans Stream Provider
    ↓
Kafka Topic (if configured)
    ↓
Orleans Stream Consumer
    ↓
Actor.HandleEventAsync(envelope)
    ↓
Agent.[EventHandler](event)
```