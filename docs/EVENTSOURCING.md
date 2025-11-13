# Aevatar Agent Framework - EventSourcing 指南

## 🌌 概述

EventSourcing是Aevatar Agent Framework的可选特性，允许Agent的状态通过事件流来重建。本文档整合了EventSourcing的设计原理和使用指南。

---

## 🏗️ 架构设计

### 核心原则

1. **Event as Source of Truth**: 事件是唯一的真实来源
2. **Immutable Events**: 事件一旦发生不可修改
3. **State Reconstruction**: 状态可通过事件回放重建
4. **Snapshot Support**: 支持快照以提升性能

### 组件架构

```
┌────────────────────────────────────────┐
│     EventSourcedGAgentBase<TState>     │
│  - RaiseEvent()                        │
│  - ConfirmEventsAsync()                │
│  - TransitionState()                   │
└───────────────┬────────────────────────┘
                ↓
┌───────────────▼────────────────────────┐
│       IEventStore (抽象)                │
│  - AppendEventAsync()                  │
│  - GetEventsAsync()                    │
│  - GetEventsRangeAsync()               │
└───────────────┬────────────────────────┘
                ↓
     ┌──────────┴──────────┐
     ↓                     ↓
┌────────────┐      ┌────────────────┐
│  InMemory  │      │  MongoDB       │
│ EventStore │      │ EventStore     │
└────────────┘      └────────────────┘
```

---

## 🚀 快速开始

### 1. 定义Proto消息

```protobuf
// bank_events.proto
syntax = "proto3";

import "google/protobuf/timestamp.proto";

// State定义
message BankAccountState {
    string account_id = 1;
    double balance = 2;
    int32 version = 3;
}

// Event定义
message AccountCreditedEvent {
    string account_id = 1;
    double amount = 2;
    string transaction_id = 3;
    google.protobuf.Timestamp timestamp = 4;
}

message AccountDebitedEvent {
    string account_id = 1;
    double amount = 2;
    string transaction_id = 3;
    google.protobuf.Timestamp timestamp = 4;
}
```

### 2. 实现EventSourced Agent

```csharp
public class BankAccountAgent : EventSourcedGAgentBase<BankAccountState>
{
    public BankAccountAgent() : base() { }

    // 业务方法：信用额度
    public async Task Credit(double amount, string transactionId)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive");

        // Raise event（暂存到uncommitted）
        RaiseEvent(new AccountCreditedEvent
        {
            AccountId = State.AccountId,
            Amount = amount,
            TransactionId = transactionId,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        // 可以在这里做更多业务逻辑...
        
        // 最后确认事件（持久化并应用）
        await ConfirmEventsAsync();
    }

    // 状态转换：如何应用Event到State
    protected override void TransitionState(IMessage @event)
    {
        switch (@event)
        {
            case AccountCreditedEvent credited:
                State.Balance += credited.Amount;
                State.Version++;
                break;
                
            case AccountDebitedEvent debited:
                State.Balance -= debited.Amount;
                State.Version++;
                break;
        }
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Bank Account {State.AccountId}, Balance: ${State.Balance}");
    }
}
```

### 3. 配置EventStore

#### 使用InMemory（测试）

```csharp
services.AddSingleton<IEventStore, InMemoryEventStore>();
```

#### 使用MongoDB（生产）

```csharp
services.AddSingleton<IEventStore>(sp =>
{
    var options = new MongoEventRepositoryOptions
    {
        ConnectionString = "mongodb://localhost:27017",
        DatabaseName = "aevatar_events"
    };
    return new MongoEventRepository(options, sp.GetRequiredService<ILogger<MongoEventRepository>>());
});
```

### 4. 使用Agent

```csharp
var manager = services.GetRequiredService<LocalGAgentActorManager>();
var actor = await manager.CreateAndRegisterAsync<BankAccountAgent>(accountId);
var account = (BankAccountAgent)actor.GetAgent();

// 业务操作
await account.Credit(100.50, "txn-001");
await account.Debit(50.25, "txn-002");

// 状态已通过事件持久化
Console.WriteLine($"Balance: {account.GetState().Balance}");  // 50.25
```

---

## 🔄 事件生命周期

### RaiseEvent流程

```
1. RaiseEvent(event)
   ↓ 
2. event → UncommittedEvents list
   ↓
3. TransitionState(event)  // 乐观更新内存State
   ↓
4. ConfirmEventsAsync()
   ↓
5. foreach (event in UncommittedEvents)
   {
       await EventStore.AppendEventAsync(Id, event);
   }
   ↓
6. UncommittedEvents.Clear()
```

### 状态重建流程

```
1. Agent激活
   ↓
2. events ← EventStore.GetEventsAsync(Id)
   ↓
3. State ← new TState()
   ↓
4. foreach (event in events)
   {
       TransitionState(event);
   }
   ↓
5. Agent就绪，State已重建
```

---

## 📊 MongoDB实现详解

### Collection结构

```
DatabaseName: aevatar_events
├── BankAccountAgent_events
│   ├── { _id, AgentId, EventType, EventData, Timestamp, Version }
│   ├── { _id, AgentId, EventType, EventData, Timestamp, Version }
│   └── ...
└── OrderAgent_events
    └── ...
```

**设计特点**:
- 每个Agent类型一个Collection
- 每个Event一个Document
- Eager Indexing（启动时创建索引）

### 索引策略

```csharp
// MongoDB创建的索引
- AgentId + Version (唯一索引，确保顺序)
- AgentId + Timestamp (范围查询)
- EventType (类型查询)
```

### 查询示例

```csharp
// 获取所有事件
var events = await eventStore.GetEventsAsync(agentId);

// 范围查询
var recentEvents = await eventStore.GetEventsRangeAsync(
    agentId, 
    fromVersion: 10, 
    toVersion: 20
);

// 时间范围查询
var eventsToday = await eventStore.GetEventsByTimeRangeAsync(
    agentId,
    from: DateTime.Today,
    to: DateTime.Now
);
```

---

## 🎯 高级特性

### 1. 快照支持（Snapshot）

```csharp
public class SnapshotConfig
{
    public int SnapshotInterval { get; set; } = 100;  // 每100个事件一个快照
}

// EventStore自动管理快照
// 重建时：LoadSnapshot() + ReplayEvents(since snapshot)
```

### 2. 事件版本控制

```protobuf
message AccountEventV2 {
    string account_id = 1;
    double amount = 2;
    string currency = 3;  // 新增字段
    // Protobuf自动处理版本兼容
}
```

### 3. 事件溯源查询

```csharp
// 查询历史状态
public async Task<BankAccountState> GetStateAtVersion(int version)
{
    var events = await eventStore.GetEventsRangeAsync(Id, 0, version);
    var state = new BankAccountState();
    foreach (var evt in events)
    {
        TransitionState(evt);
    }
    return state;
}
```

---

## 🔧 性能优化

### 1. 批量事件

```csharp
// ✅ 好 - 批量操作
RaiseEvent(event1);
RaiseEvent(event2);
RaiseEvent(event3);
await ConfirmEventsAsync();  // 一次性持久化

// ❌ 差 - 逐个持久化
await ConfirmEventsAsync(event1);
await ConfirmEventsAsync(event2);
await ConfirmEventsAsync(event3);
```

### 2. 快照策略

```csharp
// 配置快照间隔
options.SnapshotInterval = 100;  // 每100个事件

// 大幅减少重建时间：
// 无快照: 重放10000个事件 (慢)
// 有快照: 加载快照 + 重放100个事件 (快)
```

### 3. 事件压缩

对于长期运行的Agent，定期压缩历史事件：

```csharp
// 压缩策略：保留快照 + 最近N个事件
await eventStore.CompactAsync(agentId, keepRecentCount: 1000);
```

---

## 📝 完整示例

参见：
- `examples/EventSourcingDemo/BankAccountAgent.cs` - 完整的银行账户示例
- `examples/MongoDBEventStoreDemo/Program.cs` - MongoDB配置示例
- `test/Aevatar.Agents.Orleans.Tests/EventSourcing/*` - EventSourcing测试

---

## 🎭 EventSourcing vs Regular Agent

| 特性 | Regular Agent | EventSourced Agent |
|------|---------------|-------------------|
| State管理 | 直接修改State | 通过Event修改 |
| 持久化 | 可选（State snapshot） | 必须（Event log） |
| 历史追踪 | 不支持 | 完整事件历史 |
| 状态重建 | 从快照加载 | 从事件回放 |
| 审计 | 需要额外日志 | 事件即审计日志 |
| 复杂度 | 低 | 中等 |
| 性能 | 最快 | 略慢（取决于事件数） |

**何时使用EventSourcing**:
- ✅ 需要完整审计日志
- ✅ 需要时间旅行（查看历史状态）
- ✅ 金融、医疗等关键业务
- ❌ 简单CRUD操作
- ❌ 对性能极度敏感的场景

---

**EventSourcing = 事件即真相，状态即投影** 🌊

