# Orleans Kafka Stream Demo

## Overview

This demo demonstrates how to integrate **Orleans Persistent Streams** with **Apache Kafka** in the Aevatar Agent Framework. It shows the complete producer-consumer pattern using GAgent actors that communicate through Kafka-backed Orleans streams.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Orleans Silo                             │
│                                                              │
│  ┌──────────────────┐       Orleans Stream      ┌──────────────────┐
│  │ ProducerAgent    │──────────────────────────▶│ ConsumerAgent    │
│  │ (GAgentBase)     │       (Kafka backed)      │ (GAgentBase)     │
│  └──────────────────┘                           └──────────────────┘
│         │                                               │            │
│         │ Publish                                       │ Subscribe  │
│         ▼                                               ▼            │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │              Orleans Streaming Provider                        │ │
│  │              (Kafka Adapter)                                   │ │
│  └────────────────────────────────────────────────────────────────┘ │
│                              │                                      │
└──────────────────────────────┼──────────────────────────────────────┘
                               │
                               ▼
                    ┌────────────────────┐
                    │   Apache Kafka     │
                    │   (Topic: demo)    │
                    └────────────────────┘
```

## Key Concepts

### 1. Orleans Stream + Kafka Integration

Orleans Persistent Streams can use Kafka as the underlying message broker. **Critical**: The Kafka topic name MUST match the Stream Namespace configured in `StreamingOptions`.

```csharp
// Step 1: Configure StreamingOptions (MUST match Kafka topic name!)
services.Configure<StreamingOptions>(options =>
{
    options.DefaultStreamNamespace = "KafkaDemoTopic";  // This becomes the topic name
    options.StreamProviderName = "StreamProvider";
});

// Step 2: Configure Kafka with matching topic name
siloBuilder
    .AddPersistentStreams("StreamProvider", KafkaAdapterFactory.Create, b =>
    {
        b.ConfigureStreamPubSub(StreamPubSubType.ExplicitGrainBasedAndImplicit);
        b.Configure<KafkaStreamOptions>(ob => ob.Configure(options =>
        {
            options.BrokerList = new List<string> { "localhost:9092" };
            options.ConsumerGroupId = "aevatar-consumer-group";
            options.ConsumeMode = ConsumeMode.LastCommittedMessage;
            
            // Topic name MUST match DefaultStreamNamespace above!
            options.AddTopic("KafkaDemoTopic", new TopicCreationConfig
            {
                AutoCreate = true,
                Partitions = 8,
                ReplicationFactor = 1
            });
        }));
    });
```

**Why this matters**: Orleans routes messages based on Stream Namespace. If the Kafka topic name doesn't match, messages will be published to Kafka but consumers won't receive them.

### 2. Producer Agent

The `KafkaProducerAgent` publishes messages through Orleans Stream:

```csharp
public class KafkaProducerAgent : GAgentBase<KafkaProducerState>
{
    public async Task PublishMessageAsync(string content)
    {
        var kafkaMessage = new KafkaMessageEvent
        {
            MessageId = Guid.NewGuid().ToString(),
            Topic = _topic,
            Content = content,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            SenderId = AgentId
        };
        
        // PublishAsync writes to Orleans Stream → Kafka
        await PublishAsync(kafkaMessage);
        
        State.MessagesPublished++;
    }
}
```

### 3. Consumer Agent

The `KafkaConsumerAgent` uses event handlers to process messages:

```csharp
public class KafkaConsumerAgent : GAgentBase<KafkaConsumerState>
{
    // Automatically invoked when messages arrive from Kafka
    [EventHandler(Priority = 1)]
    public async Task HandleKafkaMessage(KafkaMessageEvent message)
    {
        Logger.LogInformation("Received message: {MessageId}", message.MessageId);
        
        await ProcessMessageAsync(message);
        
        State.MessagesConsumed++;
    }
}
```

### 4. Stream Subscription

Agents subscribe to streams to receive messages:

```csharp
// Producer subscribes to publish
await producerActor.SubscribeToStreamAsync(
    "AevatarKafka",    // Stream provider name
    "demo-topic",       // Namespace
    "demo-topic"        // Stream ID
);

// Consumer subscribes to receive
await consumerActor.SubscribeToStreamAsync(
    "AevatarKafka", 
    "demo-topic", 
    "demo-topic"
);
```

## Prerequisites

- **.NET 9.0 SDK** or later
- **Docker** and **Docker Compose**
- **Apache Kafka** (provided via docker-compose)

## Getting Started

### 1. Start Kafka Infrastructure

Start Kafka and Zookeeper using Docker Compose:

```bash
cd examples/KafkaStreamDemo
docker-compose up -d
```

Wait for services to be healthy (about 30 seconds):

```bash
docker-compose ps
```

You should see all services with status `healthy`.

**Optional**: Access Kafka UI at http://localhost:8080 to monitor topics and messages.

### 2. Build the Demo

```bash
dotnet build
```

### 3. Run the Demo

```bash
dotnet run
```

## Expected Output

```
=== Orleans Kafka Stream Demo ===

Kafka Brokers: localhost:9092
Orleans configured with Kafka Stream support
Starting Orleans Silo...
Orleans Silo started successfully!
Waiting for silo to be ready...

=== Starting Kafka Stream Demo ===

1. Creating Kafka Producer Agent...
   ✓ Producer created: abc-123

2. Creating Kafka Consumer Agent...
   ✓ Consumer created: def-456

3. Publishing messages to Kafka...
   [KafkaProducer] Published message xxx to topic demo-topic, total: 1
   [KafkaProducer] Published message yyy to topic demo-topic, total: 2
   [KafkaProducer] Published message zzz to topic demo-topic, total: 3
   ✓ Messages published

   [KafkaConsumer] Received message xxx from abc-123 on topic demo-topic
   [KafkaConsumer] Received message yyy from abc-123 on topic demo-topic
   [KafkaConsumer] Received message zzz from abc-123 on topic demo-topic

4. Publishing batch of messages...
   ✓ Batch published

5. Checking Producer State...
   • Messages Published: 8
   • Total Bytes Sent: 456
   • Last Publish: 10:30:45

6. Checking Consumer State...
   • Messages Consumed: 8
   • Total Bytes Received: 456
   • Subscription Status: active
   • Last Consume: 10:30:45

=== Demo Completed Successfully ===

Key Takeaways:
✓ Orleans Stream seamlessly integrates with Kafka
✓ Agents use event handlers to process Kafka messages
✓ Stream subscription enables automatic message routing
✓ State is maintained across message processing
✓ Supports batch publishing and metrics tracking

Press any key to shutdown...
```

## Configuration Options

### 1. Custom Stream Namespace / Kafka Topic

**Option A: Use Default (Recommended for quick start)**

Don't configure anything - uses default `"AevatarAgents"`:

```csharp
// No StreamingOptions configuration needed
// Kafka topic will be "AevatarAgents"
```

**Option B: Custom Topic Name**

Configure `StreamingOptions` to use a custom topic:

```csharp
services.Configure<StreamingOptions>(options =>
{
    options.DefaultStreamNamespace = "MyCustomTopic";  // Your custom topic name
    options.StreamProviderName = "StreamProvider";     // Provider name
});

// IMPORTANT: Kafka configuration must match!
options.AddTopic("MyCustomTopic", new TopicCreationConfig { ... });
```

**Option C: From Configuration File (appsettings.json)**

```json
{
  "StreamingOptions": {
    "DefaultStreamNamespace": "ProductionTopic",
    "StreamProviderName": "StreamProvider",
    "AllowCustomNamespaces": false
  }
}
```

```csharp
// Load from configuration
services.Configure<StreamingOptions>(
    context.Configuration.GetSection("StreamingOptions"));
```

### 2. Kafka Brokers

Modify broker list in `Program.cs`:

```csharp
options.BrokerList = new List<string> { "broker1:9092", "broker2:9092", "broker3:9092" };
```

### 3. Consumer Group

Set custom consumer group:

```csharp
options.ConsumerGroupId = "your-custom-group";
```

### 4. Multiple Topics (Advanced)

Add multiple topics for different message types:

```csharp
// Configure first topic
options.AddTopic("events-topic", new TopicCreationConfig
{
    AutoCreate = true,
    Partitions = 8,
    ReplicationFactor = 2
});

// Configure second topic
options.AddTopic("commands-topic", new TopicCreationConfig
{
    AutoCreate = true,
    Partitions = 4,
    ReplicationFactor = 2
});
```

**Note**: When using multiple topics, you need to configure agents to use different stream namespaces.

### 5. Consume Mode

- `ConsumeMode.LastCommittedMessage` - Resume from last committed offset (recommended for production)
- `ConsumeMode.StreamEnd` - Start from latest messages
- `ConsumeMode.StreamStart` - Start from beginning (useful for replay)

### 6. Performance Tuning

```csharp
// Adjust polling intervals
b.ConfigurePullingAgent(ob => ob.Configure(options =>
{
    options.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(50);  // Adjust based on throughput
}));

// Adjust Kafka poll timeout
options.PollTimeout = TimeSpan.FromMilliseconds(100);  // Lower for faster delivery, higher for efficiency
```

## Project Structure

```
KafkaStreamDemo/
├── kafka_messages.proto          # Protobuf message definitions
├── KafkaProducerAgent.cs         # Producer agent implementation
├── KafkaConsumerAgent.cs         # Consumer agent implementation
├── Program.cs                     # Main demo program
├── KafkaStreamDemo.csproj        # Project file
├── docker-compose.yml            # Kafka infrastructure
└── README.md                     # This file
```

## Key Features Demonstrated

### ✅ Orleans Stream Integration
- Configure Orleans to use Kafka as stream provider
- Multiple topic support with auto-creation
- Partition and replication configuration

### ✅ Producer Pattern
- Publish messages to Kafka through Orleans Stream
- Batch message publishing
- State tracking (message count, bytes sent)
- Metrics publishing

### ✅ Consumer Pattern
- Event handler-based message processing
- Automatic message routing from Kafka
- Multiple event handler support with priorities
- State tracking (message count, bytes received)

### ✅ Stream Management
- Stream subscription management
- Provider and topic configuration
- Graceful handling of connection issues

## Troubleshooting

### ❌ Messages Published but Not Consumed (MOST COMMON)

**Symptom**: Producer shows messages published, but consumer receives nothing.

**Root Cause**: Stream Namespace doesn't match Kafka topic name.

**Solution**:

1. Check your `StreamingOptions` configuration:
   ```csharp
   services.Configure<StreamingOptions>(options =>
   {
       options.DefaultStreamNamespace = "KafkaDemoTopic";  // ← This value
   });
   ```

2. Ensure Kafka topic configuration matches exactly:
   ```csharp
   options.AddTopic("KafkaDemoTopic", ...);  // ← Must match above!
   ```

3. Verify topics in Kafka:
   ```bash
   docker exec kafka-demo-broker kafka-topics --list --bootstrap-server localhost:9092
   ```

4. Check Orleans logs for stream subscription confirmations.

**Golden Rule**: `DefaultStreamNamespace` == Kafka Topic Name

### Kafka Connection Issues

If you see connection errors:

1. Verify Kafka is running: `docker-compose ps`
2. Check Kafka logs: `docker-compose logs kafka`
3. Ensure port 9092 is not blocked by firewall
4. Try connecting manually:
   ```bash
   docker exec -it kafka-demo-broker kafka-console-consumer \
     --bootstrap-server localhost:9092 \
     --topic KafkaDemoTopic \
     --from-beginning
   ```

### Messages Not Being Consumed (Other Causes)

1. **Check Event Handler Registration**:
   - Ensure `[EventHandler]` attribute is present
   - Verify method signature: `Task HandleXxx(EventType evt)`
   - Check priority and self-handling settings

2. **Verify Topic Exists**: 
   - Access Kafka UI at http://localhost:8080
   - Check topic partitions and message count

3. **Ensure Consumer Group is Active**:
   - Check logs for subscription confirmation
   - Verify consumer group in Kafka UI

4. **Check Stream Subscription**:
   - Verify `SetParentAsync` was called
   - Check for subscription errors in logs

### Build Errors

1. Ensure .NET 9.0 SDK is installed
2. Restore packages: `dotnet restore`
3. Check Protobuf generation: `dotnet build` should generate C# files from `.proto`
4. Verify Orleans packages are compatible (Orleans 9.x with Orleans.Streams.Kafka 8.0.2)

### Performance Issues

1. **High Latency**: Reduce `GetQueueMsgsTimerPeriod` and `PollTimeout`
2. **Low Throughput**: Increase partition count and consumer instances
3. **Memory Issues**: Reduce batch sizes and implement back-pressure

## Cleanup

Stop and remove Kafka infrastructure:

```bash
docker-compose down -v
```

## Next Steps

### Extend the Demo

1. **Add More Event Types**: Define additional Protobuf messages
2. **Multi-Consumer**: Create multiple consumer agents with different processing logic
3. **Error Handling**: Implement retry logic and dead letter queues
4. **Monitoring**: Add OpenTelemetry tracing and metrics
5. **Performance Testing**: Benchmark throughput and latency

### Production Considerations

#### Infrastructure

- Use **persistent storage** for Kafka data (not in-memory)
- Configure **proper replication** (factor >= 3 for critical topics)
- Deploy Kafka cluster with **at least 3 brokers** for high availability
- Use **dedicated Zookeeper ensemble** (or KRaft mode in Kafka 3.x+)
- Configure **proper retention policies** based on business requirements

#### Configuration Management

- Store `StreamingOptions` in **configuration files** (appsettings.json, environment variables)
- Use **different topics** for different environments (dev/staging/prod)
- Implement **topic naming conventions**: `{environment}.{domain}.{entity}`
- Example:
  ```csharp
  options.DefaultStreamNamespace = $"{Environment}.Agents.Events";  
  // Produces: "prod.Agents.Events", "staging.Agents.Events"
  ```

#### Message Guarantees

- Use `ConsumeMode.LastCommittedMessage` for **at-least-once delivery**
- Implement **idempotency** in event handlers (use message IDs for deduplication)
- Configure **acknowledgment strategy** based on consistency requirements
- Consider **exactly-once semantics** for financial transactions

#### Monitoring & Observability

- Set up **monitoring and alerting** on:
  - Consumer lag
  - Message throughput
  - Error rates
  - Partition rebalancing
- Integrate with **OpenTelemetry** for distributed tracing
- Monitor Orleans metrics: grain activations, stream subscriptions
- Use Kafka UI or monitoring tools (Prometheus + Grafana)

#### Security

- Enable **SSL/TLS** for broker connections
- Configure **SASL authentication** (PLAIN, SCRAM, OAuth)
- Implement **ACLs** for topic access control
- Encrypt sensitive message content

#### Schema Evolution

- Use **schema registry** (Confluent Schema Registry, Apicurio)
- Version your Protobuf messages carefully
- Follow backward/forward compatibility rules:
  - Never change field numbers
  - Add new fields as optional
  - Use reserved fields for deleted fields

#### Performance Optimization

- Tune **partition count** based on consumer parallelism
  - Rule of thumb: partitions = desired_throughput / consumer_throughput
- Optimize **batch sizes** and **linger time** for producers
- Configure **compression** (snappy, lz4, zstd)
- Monitor and adjust **GetQueueMsgsTimerPeriod** based on load

## Custom Topic Configuration - Complete Example

Here's a complete example showing how to configure a custom Kafka topic:

```csharp
// Program.cs

using Aevatar.Agents;
using Aevatar.Agents.Orleans;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Orleans.Streams.Kafka.Config;
using Orleans.Streams.Kafka.Core;

var host = Host.CreateDefaultBuilder(args)
    .UseOrleans((context, siloBuilder) =>
    {
        siloBuilder
            .UseLocalhostClustering()
            .AddMemoryGrainStorage("PubSubStore")
            
            // Configure Kafka Streams with custom topic
            .AddPersistentStreams("StreamProvider", KafkaAdapterFactory.Create, b =>
            {
                b.ConfigureStreamPubSub(StreamPubSubType.ExplicitGrainBasedAndImplicit);
                b.Configure<KafkaStreamOptions>(ob => ob.Configure(options =>
                {
                    options.BrokerList = new List<string> { "localhost:9092" };
                    options.ConsumerGroupId = "my-app-consumers";
                    options.ConsumeMode = ConsumeMode.LastCommittedMessage;
                    
                    // Custom topic - MUST match StreamingOptions!
                    options.AddTopic("MyApp.Production.Events", new TopicCreationConfig
                    {
                        AutoCreate = true,
                        Partitions = 16,
                        ReplicationFactor = 3
                    });
                }));
            });
    })
    .ConfigureServices((context, services) =>
    {
        // Configure streaming options - CRITICAL: Must match Kafka topic!
        services.Configure<StreamingOptions>(options =>
        {
            options.DefaultStreamNamespace = "MyApp.Production.Events";  // ← Same as Kafka topic
            options.StreamProviderName = "StreamProvider";
        });
        
        services.AddOrleansAgents();
        services.AddSingleton<IGAgentActorManager, OrleansGAgentActorManager>();
        services.AddSingleton<IGAgentActorFactoryProvider, AutoDiscoveryGAgentActorFactoryProvider>();
    })
    .Build();

await host.RunAsync();
```

**Key Points:**
1. ✅ `StreamingOptions.DefaultStreamNamespace` = `"MyApp.Production.Events"`
2. ✅ Kafka Topic Name = `"MyApp.Production.Events"`
3. ✅ Both configured in the same application
4. ✅ Topic naming convention: `{app}.{env}.{purpose}`

## References

- [Orleans Streaming Documentation](https://learn.microsoft.com/en-us/dotnet/orleans/streaming/)
- [Aevatar Agent Framework](../../README.md)
- [Apache Kafka Documentation](https://kafka.apache.org/documentation/)
- [Protocol Buffers](https://protobuf.dev/)
- [Orleans.Streams.Kafka Package](https://www.nuget.org/packages/Orleans.Streams.Kafka/)

## Related Examples

- **EventSourcingDemo**: Event sourcing with MongoDB
- **Demo.Agents**: Parent-child streaming patterns
- **SimpleDemo**: Basic agent communication

---

**Built with Aevatar Agent Framework** 🌊

