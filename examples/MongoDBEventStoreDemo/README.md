# MongoDB EventStore Demo

Demonstrates **OrleansEventStore** with **MongoDB persistence** for EventSourcing in Aevatar Agent Framework.

## 🎯 Features

- ✅ **OrleansEventStore**: Orleans Grain-based event coordination
- ✅ **MongoDB Persistence**: Orleans GrainStorage with MongoDB backend
- ✅ **EventSourcing V2**: Batch events + pure functional state transitions
- ✅ **Snapshot Optimization**: Auto-save snapshots every N events
- ✅ **Auto Recovery**: State restored from MongoDB on grain reactivation

---

## 🚀 Quick Start

### Default (Memory Storage)

```bash
cd examples/MongoDBEventStoreDemo
dotnet run
```

No MongoDB required! Uses in-memory storage by default.

### With MongoDB

**1. Start MongoDB:**

```bash
docker-compose up -d
```

**2. Enable MongoDB in `Program.cs` (line 67):**

Replace:
```csharp
.AddMemoryGrainStorage("EventStoreStorage");
```

With:
```csharp
.UseMongoDBClient(provider =>
{
    var settings = MongoClientSettings.FromConnectionString("mongodb://localhost:27017");
    settings.MaxConnectionPoolSize = 100;
    return settings;
})
.AddMongoDBGrainStorage("EventStoreStorage", options =>
{
    options.DatabaseName = "OrleansEventStore";
    options.CollectionPrefix = "BankAccounts";
});
```

**3. Run:**

```bash
dotnet run
```

**4. View data:**
- Mongo Express: http://localhost:8081
- Collections: 
  - `OrleansEventStore.BankAccountsevents` (Event storage)
  - `OrleansEventStore.BankAccountssnapshots` (Snapshot storage)

---

## 📊 Architecture

```
BankAccountAgent (Business Logic)
    ↓
OrleansEventStore (IEventStore impl)
    ↓
IEventStorageGrain (Orleans Grain - concurrency)
    ↓
Orleans GrainStorage (MongoDB Provider)
    ↓
MongoDB (Persistence)
```

**Collection Naming**: 
- Events: `{CollectionPrefix}events`
- Snapshots: `{CollectionPrefix}snapshots`

**Document Structure**:

Events Collection (`BankAccountsevents`):
```json
{
  "_id": "eventstorage/{AgentId}",
  "_doc": {
    "Events": [byte[], byte[], ...]        // All events for this agent
  }
}
```

Snapshots Collection (`BankAccountssnapshots`):
```json
{
  "_id": "eventstorage/{AgentId}",
  "_doc": {
    "Snapshot": byte[],                    // Latest snapshot
    "Version": 10                          // Snapshot version
  }
}
```

**Benefits of Separate Collections**:
- ✅ Faster snapshot reads (~764 bytes vs ~4KB combined)
- ✅ No need to load events when reading snapshot
- ✅ Independent scaling and indexing
- ✅ Avoid 16MB MongoDB document size limit

---

## 💻 Core API Usage

### 1. Create EventSourcing Agent

```csharp
public class BankAccountAgent : GAgentBaseWithEventSourcing<BankAccountState>
{
    // Enable snapshot every 10 events (default: 100)
    protected override ISnapshotStrategy SnapshotStrategy => 
        new IntervalSnapshotStrategy(10);

    // Pure functional state transition
    protected override void TransitionState(BankAccountState state, IMessage evt)
    {
        switch (evt)
        {
            case MoneyDeposited deposited:
                state.Balance += deposited.Amount;
                state.TransactionCount++;
                break;
            // ...
        }
    }

    public async Task DepositAsync(decimal amount, string description)
    {
        RaiseEvent(new MoneyDeposited 
        { 
            Amount = (double)amount, 
            Description = description 
        });
        await ConfirmEventsAsync();
    }
}
```

### 2. Configure Orleans + MongoDB

```csharp
builder.UseOrleans((context, siloBuilder) =>
{
    siloBuilder
        .UseLocalhostClustering()
        .UseMongoDBClient(provider => /* ... */)
        .ConfigureServices(services =>
        {
            // Required: Protobuf serialization
            services.AddSerializer(sb => sb.AddProtobufSerializer());
        })
        .AddMongoDBGrainStorage("EventStoreStorage", options =>
        {
            options.DatabaseName = "OrleansEventStore";
            options.CollectionPrefix = "BankAccounts";
        });
});

// Register EventStore
services.AddSingleton<IEventStore, OrleansEventStore>();
services.AddSingleton<OrleansGAgentActorFactory>();
```

### 3. Use Agent with EventSourcing

```csharp
var factory = sp.GetRequiredService<OrleansGAgentActorFactory>();
var eventStore = sp.GetRequiredService<IEventStore>();

// Create and enable EventSourcing
var actor = await factory.CreateGAgentActorAsync<BankAccountAgent>(agentId)
    .WithEventSourcingAsync(eventStore, sp);

var agent = actor.GetAgent() as BankAccountAgent;

// Execute operations
await agent.DepositAsync(1000m, "Salary");
await agent.WithdrawAsync(300m, "Rent");

// State automatically persisted to MongoDB!
```

### 4. Recovery (Automatic)

```csharp
// Create agent with same ID
var actor2 = await factory.CreateGAgentActorAsync<BankAccountAgent>(agentId)
    .WithEventSourcingAsync(eventStore, sp);

// State automatically recovered from MongoDB:
// 1. Load latest snapshot (if exists)
// 2. Replay incremental events
// 3. Rebuild complete state
```

---

## 📦 Required Packages

```xml
<ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Server" Version="9.2.1" />
    <PackageReference Include="Microsoft.Orleans.Serialization.Protobuf" Version="9.2.1" />
    <PackageReference Include="Orleans.Providers.MongoDB" Version="8.2.0" />
    <PackageReference Include="MongoDB.Driver" Version="3.0.0" />
</ItemGroup>
```

---

## 🎯 Demo Flow

1. **Part 1**: Create account + 3 transactions → v4
2. **Part 2**: Batch 3 transactions → v7
3. **Part 2.5**: 5 more transactions → v12, **Snapshot saved at v10**
4. **Part 3**: Simulate restart, recover from snapshot (v10) + 2 incremental events
5. **Part 4**: Show complete transaction history

**Performance**: 
- Without snapshot: Replay 12 events (~12-15ms)
- With snapshot: Load snapshot + replay 2 events (~2-3ms)
- **5-6x faster recovery** 🚀

---

## 🔍 MongoDB Data

**View data:**

```bash
mongosh

use OrleansEventStore

# List collections
show collections

# Query events
db.BankAccountsevents.find().pretty()
db.BankAccountsevents.countDocuments()

# Query snapshots
db.BankAccountssnapshots.find().pretty()
db.BankAccountssnapshots.countDocuments()

# Check document sizes
db.BankAccountsevents.stats().avgObjSize      // ~4KB
db.BankAccountssnapshots.stats().avgObjSize   // ~764 bytes
```

**Events Collection Document:**
- `_id`: `eventstorage/{AgentId}`
- `Events`: Array of serialized Protobuf events (byte arrays)

**Snapshots Collection Document:**
- `_id`: `eventstorage/{AgentId}`
- `Snapshot`: Serialized Protobuf snapshot (byte array)
- `Version`: Snapshot version number

---

## ✅ What You Get

- ✅ **Event Persistence**: All events stored in dedicated MongoDB collection
- ✅ **Snapshot Optimization**: Faster recovery with snapshots in separate collection
- ✅ **Separated Storage**: Events and snapshots in different collections for better performance
- ✅ **Distributed Concurrency**: Orleans Grain ensures consistency
- ✅ **Automatic Recovery**: State rebuilt on grain reactivation
- ✅ **Scalable Design**: No 16MB document limit, independent collection scaling
- ✅ **Production Ready**: Uses Orleans official MongoDB provider

---

## 🛠️ Troubleshooting

**MongoDB connection failed:**
```bash
# Check MongoDB is running
docker ps | grep mongo

# Start if not running
docker-compose up -d
```

**Collection not created:**
- MongoDB creates collections on first write
- Run demo once to create collection

**Protobuf serialization error:**
- Ensure `AddProtobufSerializer()` is configured
- Check all event types are defined in `.proto` files

---

## 📚 Related Files

- `Program.cs` - Complete demo implementation
- `BankAccountAgent.cs` - EventSourcing agent example
- `bank_events.proto` - Protobuf event definitions
- `docker-compose.yml` - MongoDB + Mongo Express setup

---

**🎉 Ready for Production!**
