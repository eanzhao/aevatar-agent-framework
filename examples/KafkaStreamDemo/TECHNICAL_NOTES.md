# Kafka Stream Demo - Technical Notes

## 🎯 Design Intent

This demo was created to showcase how Orleans Persistent Streams can integrate with Apache Kafka in the Aevatar Agent Framework, based on the integration patterns used in Aevatar Station.

## 📋 Current Status

**Status**: Work in Progress (WIP)

The demo framework is complete with:
- ✅ Protobuf message definitions (kafka_messages.proto)
- ✅ Producer and Consumer agent implementations
- ✅ Docker Compose setup for Kafka infrastructure
- ✅ Comprehensive README documentation

## ⚠️ Known Issues

### 1. Orleans.Streams.Kafka Version Conflict

```
Orleans.Streams.Kafka 2.0.0 (resolved) vs Orleans 9.x (used by framework)
```

**Problem**: The `Orleans.Streams.Kafka` package version 2.0.0 depends on Orleans 2.x runtime, which conflicts with Orleans 9.x used in the Aevatar Agent Framework.

**Root Cause**: The Orleans Kafka Streaming library hasn't been updated to support Orleans 9.x yet.

### 2. API Compatibility Issues

Several API incompatibilities were discovered:
- `ISiloBuilder` type exists in multiple assemblies (Orleans 2.x vs 9.x)
- `SubscribeToStreamAsync` method not available on `IGAgentActor`
- Kafka namespace alias conflicts

### 3. Agent Property Naming

- Used `AgentId` instead of correct property `Id`
- Fixed in commit but shows learning curve with framework

## 🔧 Possible Solutions

### Option 1: Use Aevatar Station's Custom Adapter

The Aevatar Station project uses a custom `AevatarKafkaAdapterFactory` that wraps the standard Kafka adapter with monitoring capabilities. This approach might work better:

```csharp
.AddPersistentStreams("Aevatar", 
    Aevatar.Core.Streaming.Kafka.AevatarKafkaAdapterFactory.Create, b =>
{
    // Configure Kafka options
});
```

**Pro**: Proven to work in Aevatar Station  
**Con**: Requires adding dependency on Aevatar.Core package

### Option 2: Wait for Orleans.Streams.Kafka Orleans 9.x Support

Track: https://github.com/OrleansContrib/Orleans.Streams.Kafka

### Option 3: Use Memory Streams as Fallback

Create a simpler demo using Orleans Memory Streams to demonstrate the agent pattern, then document how to swap in Kafka when it's available:

```csharp
.AddMemoryStreams("Aevatar", b =>
{
    builder.ConfigurePullingAgent(ob => ob.Configure(options =>
    {
        options.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(50);
    }));
});
```

**Pro**: Works immediately, demonstrates core concepts  
**Con**: Doesn't show actual Kafka integration

### Option 4: Create Custom Kafka Integration

Implement a lightweight Kafka producer/consumer wrapper that works with Orleans 9.x:
- Use Confluent.Kafka directly
- Integrate with Orleans Streams manually
- Handle serialization/deserialization

**Pro**: Full control, Orleans 9.x compatible  
**Con**: Significant development effort

## 📊 Recommended Approach

### Short Term
1. Simplify demo to use Orleans Memory Streams
2. Document the conceptual equivalence to Kafka
3. Provide configuration examples for when Kafka support is available

### Medium Term
1. Add dependency on Aevatar.Core
2. Use AevatarKafkaAdapterFactory
3. Test with actual Kafka broker

### Long Term
1. Contribute to Orleans.Streams.Kafka to add Orleans 9.x support
2. Or maintain fork with Orleans 9.x compatibility

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

## 📚 References

### Aevatar Station Examples
- `Aevatar.Silo/Extensions/OrleansHostExtension.cs`: Lines 304-362
- Shows production-grade Kafka configuration
- Includes monitoring and performance tuning

### Framework Documentation
- [EVENTSOURCING_DESIGN.md](../../docs/EVENTSOURCING_DESIGN.md)
- [Aevatar Station README](../../../aevatar-station/station/README.md)

### External Resources
- [Orleans Streaming Documentation](https://learn.microsoft.com/en-us/dotnet/orleans/streaming/)
- [Orleans.Streams.Kafka GitHub](https://github.com/OrleansContrib/Orleans.Streams.Kafka)
- [Confluent Kafka .NET Client](https://docs.confluent.io/kafka-clients/dotnet/current/overview.html)

## 🎓 Educational Value

Despite the current compilation issues, this demo provides significant educational value:

1. **Message Design**: Shows proper Protobuf message structure for streaming
2. **Agent Patterns**: Demonstrates producer-consumer pattern with agents
3. **Infrastructure Setup**: Complete Docker Compose for Kafka
4. **Documentation**: Comprehensive guide with architecture diagrams
5. **Real-World Integration**: Based on actual production code patterns

## 🚀 Next Steps

1. Choose solution approach (recommend Option 1 or Option 3)
2. Update dependencies or simplify demo
3. Resolve compilation errors
4. Test end-to-end functionality
5. Add to main examples documentation

## 💡 Alternative Demo Ideas

If Kafka integration proves too complex for a standalone demo:

1. **Simple Streaming Demo**: Use Memory Streams to show core concepts
2. **Event Sourcing Demo**: Already exists, extend with more patterns
3. **Parent-Child Communication**: Demonstrate hierarchical agent patterns
4. **Multi-Runtime Demo**: Show same agents across Local/Orleans/ProtoActor

---

**Created**: November 12, 2025  
**Branch**: feature/kafka-stream-demo  
**Status**: WIP - Awaiting resolution of dependency conflicts

