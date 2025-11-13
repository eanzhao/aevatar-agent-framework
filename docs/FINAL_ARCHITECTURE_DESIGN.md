# GAgent 最终架构设计：一份代码，多种运行时

## 核心理念

> **"写一次 Agent 代码，在多种运行时中自由切换"**

Agent 应该专注于业务逻辑，不关心状态存储位置。

---

## 关键约束（不可违背）

### 1. 所有可序列化类型必须是 Protobuf
```protobuf
// ✅ 正确
syntax = "proto3";

message AgentState {
    string id = 1;
    int32 count = 2;
}

message TranslateRequest {
    string text = 1;
}

message TranslateResult {
    string original = 1;
    string translated = 2;
}
```

### 2. Agent 定义不关心 State 存储
```csharp
// ✅ Agent 只定义业务逻辑
public class TranslateAgent : GAgentBase<TranslateState>
{
    [EventHandler]
    public async Task HandleTranslateRequest(TranslateRequest request)
    {
        var lang = State.TargetLang;
        var translated = await TranslateAsync(request.Text, lang);
        State.Cache[request.Text] = translated;
        await PublishAsync(new TranslateResult { Original = request.Text, Translated = translated });
    }
}

// ❌ Agent 不应该配置 StateStore
// 错误示例：
public class BadAgent : GAgentBase<MyState>
{
    public BadAgent() 
    {
        StateStore = new MongoDBStateStore<MyState>(...); // ❌ 不应该在这里
    }
}
```

### 3. State 存储在 Component Root 统一配置
```csharp
// ✅ 在 Composition Root 统一配置
services.ConfigGAgent<TranslateAgent, TranslateState>(options => 
{
    options.StateStore = _ => new InMemoryStateStore<TranslateState>();
});

services.ConfigGAgent<BankAccountAgent, AccountState>(options =>
{
    options.StateStore = sp => new MongoDBStateStore<AccountState>(
        sp.GetRequiredService<IMongoDatabase>());
});

services.ConfigGAgent<AuditAgent, AuditState>(options =>
{
    options.StateStore = sp => new EventSourcingStateStore<AuditState>(
        sp.GetRequiredService<IEventStore>());
});
```

### 4. EventSourcing 也是配置，不是强制
```csharp
// ✅ 普通 Agent - 不使用 EventSourcing
services.ConfigGAgent<TranslateAgent, TranslateState>();

// ✅ EventSourcing Agent - 配置 EventStore
services.ConfigGAgent<BankAccountAgent, AccountState>(options =>
{
    options.UseEventSourcing = true;
    options.EventStore = sp => sp.GetRequiredService<IEventStore>();
    options.SnapshotInterval = 100;
});
```

---

## 架构分层

```
┌─────────────────────────────────────────────────────────┐
│                    业务应用层                            │
│  - Agent 定义（GAgentBase）                              │
│  - State 定义（Protobuf）                                │
│  - Event 定义（Protobuf）                                │
├─────────────────────────────────────────────────────────┤
│          状态存储抽象层（IStateStore，可配置）            │
├─────────────────────────────────────────────────────────┤
│                    Agent 基类                            │
│              GAgentBase<TState>                          │
│              - State 自动管理                            │
│              - Event 自动路由                            │
├─────────────────────────────────────────────────────────┤
│                运行时抽象层（IGAgentActor）              │
├──────────────┬──────────────────┬──────────────────────┤
│    Local     │   ProtoActor     │      Orleans         │
└──────────────┴──────────────────┴──────────────────────┘

配置层（Component Root）：
├─ State Store 配置（内存/MongoDB/EventSourcing）
├─ 是否使用 EventSourcing
├─ Snapshot 策略
└─ 运行时选择（Local/ProtoActor/Orleans）
```

---

## Agent 定义方式

### 示例 1：简单 Agent（翻译）

```protobuf
// messages.proto
message TranslateState {
    string target_lang = 1;
    map<string, string> cache = 2;
}

message TranslateRequest {
    string text = 1;
}

message TranslateResult {
    string original = 1;
    string translated = 2;
}
```

```csharp
// TranslateAgent.cs
public class TranslateAgent : GAgentBase<TranslateState>
{
    [EventHandler]
    public async Task HandleTranslateRequest(TranslateRequest request)
    {
        var lang = State.TargetLang;
        var translated = await TranslateAsync(request.Text, lang);
        State.Cache[request.Text] = translated;
        await PublishAsync(new TranslateResult { Original = request.Text, Translated = translated });
    }
}
```

**关键：**
- Agent 代码 **完全不关心** State 存储在哪里
- 不配置 StateStore
- 不配置 EventSourcing

---

### 示例 2：银行账户 Agent（需要持久化）

```protobuf
message AccountState {
    string account_number = 1;
    double balance = 2;
    repeated string transaction_history = 3;
}

message MoneyDeposited {
    double amount = 1;
    string transaction_id = 2;
    int64 timestamp = 3;
}

message MoneyWithdrawn {
    double amount = 1;
    string transaction_id = 2;
    int64 timestamp = 3;
}

message BalanceUpdatedEvent {
    string account_number = 1;
    double new_balance = 2;
}
```

```csharp
public class BankAccountAgent : GAgentBase<AccountState>
{
    [EventHandler]
    public async Task HandleDeposit(MoneyDeposited evt)
    {
        if (evt.Amount <= 0)
            throw new InvalidOperationException("Invalid amount");
        
        State.Balance += evt.Amount;
        State.TransactionHistory.Add($"Deposit: +{evt.Amount}");
        
        await PublishAsync(new BalanceUpdatedEvent
        {
            AccountNumber = State.AccountNumber,
            NewBalance = State.Balance
        });
    }
    
    [EventHandler]
    public async Task HandleWithdraw(MoneyWithdrawn evt)
    {
        if (State.Balance < evt.Amount)
            throw new InsufficientFundsException();
        
        State.Balance -= evt.Amount;
        State.TransactionHistory.Add($"Withdraw: -{evt.Amount}");
        
        await PublishAsync(new BalanceUpdatedEvent
        {
            AccountNumber = State.AccountNumber,
            NewBalance = State.Balance
        });
    }
}
```

---

## State Store 配置（Composition Root）

### 1. 统一配置默认值（所有未显式配置的 Agent）

```csharp
// Program.cs / Startup.cs
// 所有没有单独配置的 Agent 都会使用这个存储方式
services.ConfigGAgentStateStore(options =>
{
    // 默认：内存存储 - 快速简单
    options.StateStore = _ => new InMemoryStateStore();
});
```

**规则**:
- 必须先调用 `ConfigGAgentStateStore` 设置默认值
- 没有显式调用 `ConfigGAgent<TAgent, TState>()` 注册的 Agent，自动使用此配置
- Agent 代码中不需要关心 State 存储

**配置流程**:
```
1. ConfigGAgentStateStore → 设置全局默认值
2. ConfigGAgent<TAgent, TState>() → 注册特定 Agent（可选覆盖）
3. 未注册的 Agent → 自动使用全局默认值
```

### 2. 内存存储（开发/测试）

```csharp
// 显式配置某个 Agent（会覆盖默认值）
services.ConfigGAgent<TranslateAgent, TranslateState>(); // 使用默认值

// 或使用 lambda 配置
services.ConfigGAgent<TranslateAgent, TranslateState>(options =>
{
    // 内存存储 - 快速简单
    options.StateStore = _ => new InMemoryStateStore<TranslateState>();
});
```

### 3. MongoDB 存储（生产）

```csharp
// 配置 MongoDB
services.AddSingleton<IMongoDatabase>(sp =>
{
    var client = new MongoClient("mongodb://localhost:27017");
    return client.GetDatabase("aevatar");
});

// 为特定 Agent 配置 MongoDB 存储（覆盖默认值）
services.ConfigGAgent<BankAccountAgent, AccountState>(options =>
{
    options.StateStore = sp => new MongoDBStateStore<AccountState>(
        sp.GetRequiredService<IMongoDatabase>());
});
```

**完整示例**:
```csharp
// 1. 先配置默认值（所有 Agent 通用）
services.ConfigGAgentStateStore(options =>
{
    options.StateStore = _ => new InMemoryStateStore(); // 默认内存
});

// 2. 注册特定 Agent（可覆盖默认值）
services.ConfigGAgent<TranslateAgent, TranslateState>(); // 使用默认值（内存）

services.ConfigGAgent<BankAccountAgent, AccountState>(options => // 覆盖：MongoDB
{
    options.StateStore = sp => new MongoDBStateStore<AccountState>(
        sp.GetRequiredService<IMongoDatabase>());
});

// 3. EventSourcing 配置（需要审计的 Agent）
services.AddSingleton<IEventStore>(sp =>
{
    var repository = sp.GetRequiredService<IEventRepository>();
    var serializer = sp.GetRequiredService<ISerializer>();
    return new OrleansEventStoreAdapter(repository, serializer);
});

services.ConfigGAgent<AuditAgent, AuditState>(options => // 覆盖：EventSourcing
{
    options.StateStore = sp => new EventSourcingStateStore<AuditState>(
        sp.GetRequiredService<IEventStore>(),
        new IntervalSnapshotStrategy(100));
});
```

### 4. EventSourcing 存储（审计需求）

```csharp
// 配置 EventStore
services.AddSingleton<IEventStore>(sp =>
{
    var repository = sp.GetRequiredService<IEventRepository>();
    var serializer = sp.GetRequiredService<ISerializer>();
    return new OrleansEventStoreAdapter(repository, serializer);
});

// 为需要审计的 Agent 配置 EventSourcing
services.ConfigGAgent<AuditAgent, AuditState>(options =>
{
    // EventSourcing 存储（覆盖默认值）
    options.StateStore = sp => new EventSourcingStateStore<AuditState>(
        sp.GetRequiredService<IEventStore>(),
        new IntervalSnapshotStrategy(100));
});
```

---

## IEventStore 兼容设计

### 适配器模式

```csharp
/// <summary>
/// 事件存储接口（抽象，统一接口）
/// </summary>
public interface IEventStore
{
    Task<long> AppendEventsAsync(Guid agentId, IEnumerable<IEvent> events, CancellationToken ct);
    Task<IReadOnlyList<IEvent>> GetEventsAsync(Guid agentId, long? fromVersion, CancellationToken ct);
    Task<IEvent?> GetLatestSnapshotAsync(Guid agentId, CancellationToken ct);
    Task SaveSnapshotAsync(Guid agentId, IEvent snapshot, long version, CancellationToken ct);
}

/// <summary>
/// 适配现有的 Orleans IEventRepository
/// 不破坏现有代码
/// </summary>
public class OrleansEventStoreAdapter : IEventStore
{
    private readonly IEventRepository _repository;
    private readonly ISerializer _serializer;
    
    public OrleansEventStoreAdapter(IEventRepository repository, ISerializer serializer)
    {
        _repository = repository;
        _serializer = serializer;
    }
    
    public async Task<long> AppendEventsAsync(Guid agentId, IEnumerable<IEvent> events, CancellationToken ct)
    {
        var agentEvents = events.Select(evt => new AgentStateEvent
        {
            AgentId = agentId,
            EventType = evt.GetType().Name,
            EventData = await _serializer.SerializeAsync(evt),
            Version = evt.Version
        });
        
        return await _repository.AppendEventsAsync(agentId, agentEvents, ct);
    }
    
    public async Task<IReadOnlyList<IEvent>> GetEventsAsync(
        Guid agentId,
        long? fromVersion,
        CancellationToken ct)
    {
        var events = await _repository.GetEventsAsync(agentId, fromVersion, ct: ct);
        
        return events.Select(evt => 
            (IEvent)_serializer.Deserialize(evt.EventData, Type.GetType(evt.EventType)!)
        ).ToList();
    }
}
```

---

## 运行时实现概览

```csharp
// 1. Local Runtime - 简单直接
public class LocalGAgentActor : GAgentActorBase
{
    protected override Task SendEventToActorAsync(Guid actorId, EventEnvelope envelope, CancellationToken ct)
    {
        // 使用 LocalMessageStream
        var stream = _registry.GetStream(actorId);
        return stream.PublishAsync(envelope);
    }
}

// 2. ProtoActor Runtime - 高性能
public class ProtoActorGAgentActor : GAgentActorBase
{
    // 使用 ProtoActorMessageStream
}

// 3. Orleans Runtime - 分布式
public class OrleansGAgentActorFactory : IGAgentActorFactory
{
    public async Task<IGAgentActor> CreateGAgentActorAsync<TAgent>(Guid id, CancellationToken ct)
        where TAgent : IGAgent
    {
        // 创建 Grain
        var grain = _grainFactory.GetGrain<TAgent>(id);
        
        // 适配为 IGAgentActor
        return new OrleansGAgentActorAdapter(grain);
    }
}
```

---

## 核心原则

### 1. **Agent 不关心存储**
```csharp
// ✅ Agent 只定义业务逻辑
public class MyAgent : GAgentBase<MyState>
{
    [EventHandler]
    public async Task HandleEvent(MyEvent evt) { /* ... */ }
}

// ✅ 在 Composition Root 配置
services.ConfigGAgent<MyAgent, MyState>(options =>
{
    options.StateStore = sp => new MongoDBStateStore<MyState>(...);
});
```

### 2. **EventSourcing 是配置，不是强制**
```csharp
// 普通 Agent
services.ConfigGAgent<SimpleAgent, SimpleState>();

// 审计 Agent
services.ConfigGAgent<AuditAgent, AuditState>(options =>
{
    options.UseEventSourcing = true;
});
```

### 3. **Protobuf 优先**
```protobuf
// 所有消息都用 proto 定义
syntax = "proto3";
message MyState { string id = 1; }
```

### 4. **运行时切换只需一行**
```csharp
// Agent 代码不变
services.AddSingleton<IGAgentActorFactory, LocalGAgentActorFactory>();
// services.AddSingleton<IGAgentActorFactory, ProtoActorGAgentActorFactory>();
// services.AddSingleton<IGAgentActorFactory, OrleansGAgentActorFactory>();
```

---

## 设计优势

| 对比项 | 旧设计 | 新设计 |
|--------|-------|--------|
| **Agent 职责** | ❌ 关心存储 | ✅ 只关心业务 |
| **配置位置** | ❌ 分散在 Agent | ✅ Composition Root 统一 |
| **存储方式** | ❌ 强制 EventSourcing | ✅ 自由选择 |
| **Runtime 切换** | ❌ Agent 代码不同 | ✅ Agent 代码相同 |
| **IEventRepository** | ❌ 不兼容 | ✅ 完美兼容 |
| **Protobuf** | ❌ 混合 | ✅ 统一 |
| **Orleans 支持** | ❌ 受影响 | ✅ 不受影响 |

---

## 总结

✅ **Agent 不关心 State 存储** - 只定义业务逻辑  
✅ **统一在 Composition Root 配置** - 集中管理  
✅ **EventSourcing 是配置项** - 自由选择  
✅ **运行时切换一行代码** - "一份代码，多种运行时"  
✅ **完美兼容 IEventRepository** - 适配器模式  
✅ **Orleans 不受影响** - 现有代码继续工作  
✅ **全部 Protobuf** - 跨平台、高性能  

**最终实现："写一次 Agent 代码，在多种运行时中自由切换"**
