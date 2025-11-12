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

Orleans Persistent Streams can use Kafka as the underlying message broker:

```csharp
siloBuilder
    .AddKafka("AevatarKafka")
    .WithOptions(options =>
    {
        options.BrokerList = ["localhost:9092"];
        options.ConsumerGroupId = "aevatar-demo-group";
        options.ConsumeMode = ConsumeMode.StreamEnd;
        
        options.AddTopic("demo-topic", new TopicCreationConfig
        {
            AutoCreate = true,
            Partitions = 3,
            ReplicationFactor = 1
        });
    })
    .AddJson()
    .Build();
```

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

### Kafka Brokers

Set custom Kafka brokers via environment variable:

```bash
export KAFKA_BROKERS="broker1:9092,broker2:9092"
dotnet run
```

### Consumer Group

Modify in `Program.cs`:

```csharp
options.ConsumerGroupId = "your-custom-group";
```

### Topics

Add additional topics:

```csharp
options.AddTopic("events-topic", new TopicCreationConfig
{
    AutoCreate = true,
    Partitions = 5,
    ReplicationFactor = 2
});
```

### Consume Mode

- `ConsumeMode.StreamEnd` - Start from latest messages (default)
- `ConsumeMode.StreamStart` - Start from beginning
- `ConsumeMode.LastCommittedMessage` - Resume from last committed offset

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

### Kafka Connection Issues

If you see connection errors:

1. Verify Kafka is running: `docker-compose ps`
2. Check Kafka logs: `docker-compose logs kafka`
3. Ensure port 9092 is not blocked by firewall
4. Try connecting manually: `docker exec -it kafka-demo-broker kafka-console-consumer --bootstrap-server localhost:9092 --topic demo-topic`

### Messages Not Being Consumed

1. Check consumer subscription logs
2. Verify topic exists: Access Kafka UI at http://localhost:8080
3. Ensure consumer group is active
4. Check for event handler registration

### Build Errors

1. Ensure .NET 9.0 SDK is installed
2. Restore packages: `dotnet restore`
3. Check Protobuf generation: `dotnet build` should generate C# files from `.proto`

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

- Use **persistent storage** for Kafka data
- Configure **proper replication** (factor >= 3)
- Implement **exactly-once semantics** if needed
- Set up **monitoring and alerting**
- Configure **proper retention policies**
- Use **schema registry** for message evolution

## References

- [Orleans Streaming Documentation](https://learn.microsoft.com/en-us/dotnet/orleans/streaming/)
- [Aevatar Agent Framework](../../README.md)
- [Apache Kafka Documentation](https://kafka.apache.org/documentation/)
- [Protocol Buffers](https://protobuf.dev/)

## Related Examples

- **EventSourcingDemo**: Event sourcing with MongoDB
- **Demo.Agents**: Parent-child streaming patterns
- **SimpleDemo**: Basic agent communication

---

**Built with Aevatar Agent Framework** 🌊

