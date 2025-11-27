# Aevatar.Silo

🌌 **Orleans Silo for Aevatar Agent Framework**  
**Flexible Configuration | Multiple Providers | Production-Ready**

---

## 🎯 Overview

This project provides an Orleans Silo host for the Aevatar Agent Framework with **flexible provider configuration**:

### 📦 Storage Providers
- ✅ **Memory** (Default) - Fast, in-memory storage for development
- ✅ **MongoDB** - Production-ready persistent storage

### 🌊 Streaming Providers
- ✅ **Orleans Stream** (Default) - Built-in memory streaming, no external dependencies
- ✅ **Kafka** - High-throughput, production-grade message streaming

### 🏗️ Architecture
- ✅ **Event Sourcing** - Persistent event history
- ✅ **Health Checks** - Kubernetes-ready liveness/readiness probes
- ✅ **ABP Framework** - Shared App domain and application layers
- ✅ **Extension-Based** - Clean, modular configuration

## 🚀 Quick Start

### 1. Prerequisites

- **.NET 9.0 SDK**
- **MongoDB** (localhost:27017)
- **Apache Kafka** (localhost:9092) - Optional for Kafka features

### 2. Start MongoDB

```bash
# Using Docker
docker run -d -p 27017:27017 --name mongodb mongo:latest

# Or use existing MongoDB instance
```

### 3. Start Kafka (Optional)

```bash
# Navigate to KafkaStreamDemo for docker-compose
cd examples/KafkaStreamDemo
docker-compose up -d

# Wait for Kafka to be healthy
docker-compose ps
```

### 4. Build and Run Silo

```bash
cd src/Aevatar.App/src/Aevatar.Silo

# Build
dotnet build

# Run
dotnet run
```

### 5. Verify Startup

You should see logs like:

```
[16:30:00 INF] 🚀 Starting Aevatar Silo with Kafka & EventSourcing support
[16:30:01 INF] 📋 Orleans Configuration:
[16:30:01 INF]   ClusterId: aevatar-cluster
[16:30:01 INF]   ServiceId: aevatar-service
[16:30:01 INF]   SiloPort: 11111
[16:30:01 INF]   GatewayPort: 30000
[16:30:02 INF] 🗄️  Configuring MongoDB Storage:
[16:30:02 INF]   ConnectionString: mongodb://localhost:27017/AevatarBusiness
[16:30:02 INF] ✅ MongoDB storage configured
[16:30:03 INF] 📡 Configuring Kafka Streams:
[16:30:03 INF]   BootstrapServers: localhost:9092
[16:30:03 INF]   ConsumerGroupId: aevatar-silo-consumers
[16:30:03 INF]   StreamNamespace: agent-events
[16:30:03 INF] ✅ Kafka streams configured
[16:30:04 INF] 📚 Configuring EventSourcing:
[16:30:04 INF]   Enabled: True
[16:30:04 INF]   SnapshotFrequency: 10
[16:30:04 INF]   EventStoreType: MongoDB
[16:30:04 INF] ✅ EventSourcing configured
[16:30:05 INF] ✅ Orleans configuration completed
[16:30:06 INF] Orleans Silo is ready
```

---

## 📁 Project Structure

```
Aevatar.Silo/
├── Program.cs                    # Main entry point and Orleans configuration
├── Aevatar.Silo.csproj          # Project file with dependencies
├── appsettings.json             # Configuration file
├── Configuration/
│   └── KafkaConfiguration.cs    # Configuration models
├── Agents/
│   ├── KafkaProducerAgent.cs    # Kafka message producer
│   ├── KafkaConsumerAgent.cs    # Kafka message consumer (in KafkaProducerAgent.cs)
│   └── BankAccountAgent.cs      # EventSourcing demo agent
├── Protos/
│   └── silo_messages.proto      # Protobuf message definitions
└── README.md                     # This file
```

---

## 🔧 Configuration

### Default Configuration (Development)

By default, the Silo uses **Memory storage** and **Orleans Stream** for simplicity:

```json
{
  "Storage": {
    "Provider": "Memory"
  },
  "Streaming": {
    "Provider": "OrleansStream",
    "ProviderName": "Default",
    "DefaultNamespace": "agent-events"
  }
}
```

**Advantages:**
- ✅ No external dependencies (MongoDB, Kafka)
- ✅ Fast startup
- ✅ Perfect for development and testing

### Production Configuration

Switch to **MongoDB storage** and **Kafka streaming** for production:

```json
{
  "ConnectionStrings": {
    "MongoDB": "mongodb://mongodb-cluster:27017/AevatarBusiness?replicaSet=rs0"
  },
  "Storage": {
    "Provider": "MongoDB",
    "DatabaseName": "AevatarBusiness"
  },
  "Streaming": {
    "Provider": "Kafka",
    "ProviderName": "KafkaStreamProvider",
    "DefaultNamespace": "agent-events"
  },
  "Kafka": {
    "BootstrapServers": "kafka-broker-1:9092,kafka-broker-2:9092",
    "ConsumerGroupId": "aevatar-production-consumers",
    "DefaultPartitions": 16,
    "DefaultReplicationFactor": 3
  }
}
```

**Advantages:**
- ✅ Persistent storage (survives restarts)
- ✅ High-throughput messaging
- ✅ Horizontal scalability

### Key Configuration Options

#### MongoDB
- **ConnectionString**: MongoDB connection string
- Used for grain state persistence and event store

#### Orleans
- **ClusterId**: Cluster identifier for Orleans
- **ServiceId**: Service identifier
- **SiloPort**: Port for silo-to-silo communication
- **GatewayPort**: Port for client-to-silo communication

#### Kafka
- **BootstrapServers**: Kafka broker addresses
- **ConsumerGroupId**: Consumer group for stream processing
- **Topics**: Kafka topic configurations
  - **Name**: Topic name (MUST match StreamNamespace!)
  - **Partitions**: Number of partitions
  - **ReplicationFactor**: Replication factor

#### Streaming
- **DefaultStreamNamespace**: Default namespace for Orleans streams
  - ⚠️ **CRITICAL**: Must match Kafka topic name!
- **StreamProviderName**: Name of the stream provider

#### EventSourcing
- **Enabled**: Enable/disable event sourcing
- **SnapshotFrequency**: Create snapshot every N events
- **EventStoreType**: Type of event store (MongoDB)

---

## 🌊 Kafka Stream Integration

### How It Works

```
Agent → PublishAsync() → Orleans Stream → Kafka Topic → Orleans Stream → Other Agents
```

### Key Concepts

1. **Stream Namespace = Kafka Topic**
   - `StreamingOptions.DefaultStreamNamespace` MUST match Kafka topic name
   - Example: `"agent-events"` → Kafka topic `"agent-events"`

2. **Producer Pattern**
   ```csharp
   await PublishAsync(new KafkaMessageEvent { ... });
   ```

3. **Consumer Pattern**
   ```csharp
   [EventHandler]
   public async Task HandleKafkaMessage(KafkaMessageEvent evt)
   {
       // Process message
   }
   ```

### Adding Custom Topics

1. Update `appsettings.json`:
   ```json
   "Topics": {
     "MyCustomTopic": {
       "Name": "my-custom-topic",
       "Partitions": 4,
       "ReplicationFactor": 1
     }
   }
   ```

2. Use in agents:
   ```csharp
   await actor.SubscribeToStreamAsync(
       "KafkaStreamProvider",
       "my-custom-topic",  // Namespace
       "my-custom-topic"   // Stream ID
   );
   ```

---

## 📚 Event Sourcing Integration

### How It Works

```
Agent → RaiseEvent() → Pending Events → ConfirmEventsAsync() → EventStore (MongoDB)
                                      ↓
                               State Transition (Pure Function)
```

### Key Concepts

1. **Batch Event Commit**
   ```csharp
   RaiseEvent(event1);
   RaiseEvent(event2);
   RaiseEvent(event3);
   await ConfirmEventsAsync();  // Single I/O operation
   ```

2. **Pure Functional State Transition**
   ```csharp
   protected override BankAccountState TransitionState(BankAccountState state, IMessage evt)
   {
       var newState = state.Clone();  // Don't modify original
       newState.Balance += amount;
       return newState;
   }
   ```

3. **Automatic Crash Recovery**
   - Events are replayed on agent activation
   - State is reconstructed from event history
   - Snapshots improve performance

### Event Store Structure

MongoDB collections:
- `EventStore_Events`: All domain events
- `EventStore_Snapshots`: State snapshots
- `Orleans_GrainState`: Orleans grain state

---

## 🤖 Demo Agents

### 1. KafkaProducerAgent

Publishes messages to Kafka through Orleans Stream.

**Features:**
- Message publishing to Kafka
- Batch message support
- State tracking (message count, bytes sent)

**Usage:**
```csharp
var producer = await actorManager.CreateAndRegisterAsync<KafkaProducerAgent>(id);
await producer.PublishMessageAsync("Hello, Kafka!");
```

### 2. KafkaConsumerAgent

Consumes messages from Kafka through Orleans Stream.

**Features:**
- Event-driven message processing
- Automatic message routing
- State tracking (message count, bytes received)

**Usage:**
```csharp
var consumer = await actorManager.CreateAndRegisterAsync<KafkaConsumerAgent>(id);
// Messages are automatically routed via event handlers
```

### 3. BankAccountAgent

Event-sourced bank account with full transaction history.

**Features:**
- Account creation with initial balance
- Deposit and withdrawal operations
- Batch transaction processing
- Event sourcing with snapshots
- Crash recovery

**Usage:**
```csharp
var account = await actorManager.CreateAndRegisterAsync<BankAccountAgent>(id)
    .WithEventSourcingAsync(eventStore);

await account.CreateAccountAsync("Alice", 1000.0);
await account.DepositAsync(500.0, "Salary");
await account.WithdrawAsync(200.0, "Rent");

var balance = await account.GetBalanceAsync();
```

---

## 🔗 Integration with App

This Silo project is part of the Aevatar.App solution and shares:

1. **Domain Layer** (`Aevatar.App.Domain`)
   - Domain entities and aggregates
   - Business rules and validation

2. **Application Layer** (`Aevatar.App.Application`)
   - Application services
   - DTOs and mappings

3. **MongoDB Layer** (`Aevatar.App.MongoDB`)
   - Repository implementations
   - Database context

### Shared Database

Both Silo and AuthServer/HttpApi.Host use the same MongoDB database:
- Database: `AevatarBusiness`
- Grain State: `Orleans_*` collections
- Event Store: `EventStore_*` collections
- ABP Data: Standard ABP collections

---

## 📊 Monitoring

### Logs

Logs are written to:
- **Console**: Immediate feedback during development
- **File**: `logs/silo-{Date}.log` for persistent logging

### Orleans Dashboard (Future)

To enable Orleans Dashboard:

1. Add package:
   ```xml
   <PackageReference Include="OrleansDashboard" />
   ```

2. Configure in Program.cs:
   ```csharp
   siloBuilder.UseDashboard(options =>
   {
       options.Port = 8080;
   });
   ```

3. Access at: `http://localhost:8080`

---

## 🧪 Testing

### Unit Testing

Create test projects for agents:

```bash
cd test
dotnet new xunit -n Aevatar.Silo.Tests
cd Aevatar.Silo.Tests
dotnet add reference ../../src/Aevatar.Silo/Aevatar.Silo.csproj
```

### Integration Testing

Use Orleans TestCluster for integration tests:

```csharp
var testCluster = new TestClusterBuilder()
    .AddSiloBuilderConfigurator<TestSiloConfigurator>()
    .Build();

await testCluster.DeployAsync();
```

---

## 🚧 TODO

- [ ] Integrate Aevatar Agent Framework
  - [ ] Add `GAgentBase` implementations
  - [ ] Add `IGAgentActorManager`
  - [ ] Add `OrleansGAgentActorManager`
- [ ] Enable agent implementations
  - [ ] Uncomment agent code
  - [ ] Add event handlers
  - [ ] Add stream subscriptions
- [ ] Add MongoDB EventStore implementation
- [ ] Add demo scenarios
- [ ] Add health checks
- [ ] Add Orleans Dashboard
- [ ] Add OpenTelemetry metrics

---

## 📖 References

### Aevatar Framework
- [Agent Framework Rules](../../README.md)
- [EventSourcing Demo](../../../../examples/EventSourcingDemo/)
- [Kafka Stream Demo](../../../../examples/KafkaStreamDemo/)

### Orleans Documentation
- [Orleans Documentation](https://learn.microsoft.com/en-us/dotnet/orleans/)
- [Orleans Streams](https://learn.microsoft.com/en-us/dotnet/orleans/streaming/)
- [Orleans Persistence](https://learn.microsoft.com/en-us/dotnet/orleans/grains/grain-persistence/)

### Third-Party
- [Apache Kafka](https://kafka.apache.org/documentation/)
- [MongoDB Documentation](https://docs.mongodb.com/)
- [Protocol Buffers](https://protobuf.dev/)

---

## 🌟 Architecture Principles

This Silo implementation follows the Aevatar Agent Framework principles:

1. **Event-Driven**: All communication through events
2. **Actor Model**: Agents run within Orleans grains
3. **Stream-Based**: Kafka-backed Orleans streams
4. **Event Sourcing**: Persistent event history
5. **Pure Functional**: Immutable state transitions
6. **Scalable**: Horizontal scaling through Orleans

---

**Built with Aevatar Agent Framework** 🌊  
**Version**: 1.0.0  
**Date**: 2025-11-20

