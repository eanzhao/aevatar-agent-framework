# Aevatar Agent Framework - EventSourcing 指南

## 🌌 概述
EventSourcing 是 Aevatar Agent Framework 的可选特性,允许 Agent 的状态通过事件流来重建。本文档整合 EventSourcing 的设计原理和使用指南。

---

## 🏗️ 核心设计架构

现代 EventSourcing 重构后采用以下架构:

```
┌────────────────────────────────────────┐
│     GAgentBaseWithEventSourcing<T>    │
│  - RaiseEvent()                        │
│  - ConfirmEventsAsync()                │
│  - TransitionState(纯函数)            │
│  - ReplayEventsAsync()                 │
└─────────────────┬──────────────────────┘
                  ↓
┌─────────────────▼──────────────────────┐
│  IEventStore (抽象接口)                │
│  - SaveEventAsync()                    │
│  - LoadEventsAsync()                   │
└─────────────────┬──────────────────────┘
                  ↓
┌─────────────────▼──────────────────────┐
│  OrleansEventStore（Orleans实现）      │
│  - 基于 IEventStorageGrain             │
│  - 支持所有 Orleans Storage Provider   │
└─────────────────▼──────────────────────┘
                  ↓
┌─────────────────▼──────────────────────┐
│  IEventRepository（持久化抽象）        │
│  - MongoDB EventRepository              │
│  - InMemory EventRepository            │
└────────────────────────────────────────┘
```

**关键改进**:
1. ✅ **Agent 层原生日志**: `GAgentBaseWithEventSourcing` 直接继承 `GAgent<TState>`
2. ✅ **Actor 层触发回放**: 事件回放由 Actor 在激活时触发,不污染 Agent 层
3. ✅ **统一 IEventStore**: 所有公开实现使用相同接口
4. ✅ **生产就绪**: 使用 Orleans Grain Storage 作为后端 (弹性、分布式、持久化)

---

## ✅ EventSourcing 的正确使用方式

### 1. 定义 Protobuf 消息

```protobuf
// bank_events.proto
syntax = "proto3";

import "google/protobuf/timestamp.proto";

// State定义message BankAccountState {
    string account_holder = 1;
    double balance = 2;
    repeated string transaction_history = 3;
}

// 事件定义
message AccountCreated {
    string account_holder = 1;
    double initial_balance = 2;
}

message MoneyDeposited {
    double amount = 1;
    string description = 2;
}

message MoneyWithdrawn {
    double amount = 1;
    string description = 2;
}
```

### 2. 实现 EventSourced Agent

```csharp
public class BankAccountAgent : GAgentBaseWithEventSourcing<BankAccountState>
{
    public async Task CreateAccountAsync(string holder, decimal initialBalance)
    {
        var evt = new AccountCreated {
            AccountHolder = holder,
            InitialBalance = (double)initialBalance
        };

        RaiseEvent(evt, new Dictionary<string, string> {
            ["Operation"] = "CreateAccount",
            ["Holder"] = holder
        });

        await ConfirmEventsAsync();
    }

    public async Task DepositAsync(decimal amount, string description = "")
    {
        var evt = new MoneyDeposited {
            Amount = (double)amount,
            Description = description
        };

        RaiseEvent(evt);
        await ConfirmEventsAsync();
    }

    public async Task WithdrawAsync(decimal amount, string description = "")
    {
        if (GetState().Balance < amount)
            throw new InvalidOperationException("Insufficient balance");

        var evt = new MoneyWithdrawn {
            Amount = (double)amount,
            Description = description
        };

        RaiseEvent(evt);
        await ConfirmEventsAsync();
    }

    // State 转换（纯函数）
    protected override void TransitionState(BankAccountState state, IMessage evt)
    {
        switch (evt)
        {
            case AccountCreated created:
                state.AccountHolder = created.AccountHolder;
                state.Balance = created.InitialBalance;
                state.TransactionHistory.Add($"[{DateTime.UtcNow}] Account created for {created.AccountHolder}");
                break;

            case MoneyDeposited deposited:
                state.Balance += deposited.Amount;
                state.TransactionHistory.Add($"[{DateTime.UtcNow}] Deposited ${deposited.Amount:F2} - {deposited.Description}");
                break;

            case MoneyWithdrawn withdrawn:
                state.Balance -= withdrawn.Amount;
                state.TransactionHistory.Add($"[{DateTime.UtcNow}] Withdrew ${withdrawn.Amount:F2} - {withdrawn.Description}");
                break;
        }
    }
}
```

### 3. 配置 EventStore

```csharp
// 在 Orleans Silo 配置
var host = Host.CreateDefaultBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            .AddMongoDBGrainStorage("EventStoreStorage", options => // 用于 EventSourcing
            {
                options.DatabaseName = "OrleansEventStore";
                options.CollectionPrefix = "Test";
                options.ConfigureJsonSerializerSettings = settings =>
                {
                    settings.NullValueHandling = NullValueHandling.Include;
                    settings.DefaultValueHandling = DefaultValueHandling.Populate;
                    settings.ObjectCreationHandling = ObjectCreationHandling.Replace;
                };
            });
    })
    .ConfigureServices(services =>
    {
        // 配置 EventStore（必需）
        services.AddSingleton<IEventStore, OrleansEventStore>();

        // 如果不配置 EventStore，Agent 正常工作但不持久化事件
        // services.AddSingleton<IEventStore, InMemoryEventStore>(); // 测试用
    })
    .Build();
```

### 4. 使用 Agent

```csharp
// 创建 Actor（自动启用事件溯源）
var actor = await factory.CreateGAgentActorAsync<BankAccountAgent>(accountId);
var agent = (BankAccountAgent)actor.GetAgent();

// 执行业务操作，事件自动持久化
await agent.CreateAccountAsync("Alice Smith", 1000m);
await agent.DepositAsync(500m, "Salary");
await agent.WithdrawAsync(200m, "Rent");

// 查询状态
var state = agent.GetState();
Console.WriteLine($"Balance: {state.Balance:F2}");  // $1300.00

// Grain 停用后自动保存快照
// 重新激活时：自动加载快照 + 回放增量事件
```

---

## 🔄 EventSourcing 生命周期（新实现）

### Actor 激活流程

```
1. 创建 Actor
   ↓
2. 激活 Actor (调用 ActivateAsync)
   ↓
3. [在 Actor 层] 检查 Agent 是否继承自 GAgentBaseWithEventSourcing<T>
   ↓
4. 如果是，调用 Agent.ReplayEventsAsync()
   ↓
5. ReplayEventsAsync()
   ├── 加载最新快照
   ├── 加载快照后的所有事件
   └── 对每个事件调用 TransitionState()
   ↓
6. State 重建完成
   ↓
7. Actor 就绪，处理新事件
```

### 事件持久化流程

```
1. 业务方法调用 RaiseEvent(evt)
   ↓
2. evt → _pendingEvents 列表
   ↓
3. ConfirmEventsAsync()
   ├── foreach evt in _pendingEvents
   │   └── await _eventStore.SaveEventAsync(Id, evt)
   └── _pendingEvents.Clear()
   ↓
4. 事件持久化完成（到 MongoDB）
```

**关键改进**:
- ✅ 事件回放在 **Actor 层** 触发，Agent 层保持纯净
- ✅ 开发者不需要手动调用 ReplayEventsAsync()
- ✅ 事件存储通过依赖注入自动配置

---

## 📊 MongoDB 后端详解

### 架构

```
┌─────────────────────────┐
│     IEventStore         │  ← 框架抽象接口
└──────┬──────────────────┘
       ↓
┌─────────────────────────┐
│   OrleansEventStore     │  ← Orleans 实现
│  - AppendEventToGrain() │
│  - LoadEventsFromGrain()│
└──────┬──────────────────┘
       ↓
┌─────────────────────────┐
│ IEventStorageGrain<T>   │  ← Orleans Grain
└──────┬──────────────────┘
       ↓
┌─────────────────────────┐
│ Orleans GrainStorage    │  ← Orleans 存储抽象
└──────┬──────────────────┘
       ↓
┌─────────────────────────┐
│  MongoDB Provider       │  ← 具体实现
│  - 数据库: OrleansEventStore
│  - 集合: Test-EventStorageState
└─────────────────────────┘
```

### 存储结构

```javascript
// MongoDB 文档结构
{
  "_id": "6e5ae66f-e925-4dfa-bef6-bd82a4d3fe59",  // Agent ID
  "state": {
    // 当前状态（如果有快照）
    "account_holder": "Alice Smith",
    "balance": 1650.00,
    "transaction_history": [...]
  },
  "_etag": "...",
  "_modified_date": ISODate("...")
}

// 事件实际存储在 GrainState 内部的数据结构中
```

**设计优点**:
- ✅ 使用 Orleans 内置的 GrainStorage（可靠、经过充分测试）
- ✅ 支持所有 Orleans Storage Provider（MongoDB、Azure、SQL Server、Redis 等）
- ✅ 自动获得 Orleans 的并发控制和一致性保证
- ✅ Elastic（支持集群扩展）

---

## 🎯 快照支持

EventSourcing 支持自动快照以提升回放性能：

```csharp
// 在 Agent 中配置快照策略
public class BankAccountAgent : GAgentBaseWithEventSourcing<BankAccountState>
{
    // 每 100 个事件创建一个快照
    protected override ISnapshotStrategy SnapshotStrategy =>
        new IntervalSnapshotStrategy(100);

    // 或者自定义策略
    protected override async Task<bool> ShouldCreateSnapshotAsync()
    {
        return GetCurrentVersion() % 50 == 0;  // 每 50 个版本快照
    }
}
// 快照自动管理：
// - 保存：Grain 停用时创建快照
// - 加载：Grain 激活时加载最新快照
// - 回放：只回放快照之后的事件
```

---

## 💡 最佳实践

### ✅ 应该做的

1. **保持 TransitionState 纯函数**
```csharp
protected override void TransitionState(BankAccountState state, IMessage evt)
{
    // ✅ 纯函数：无副作用，可重放
    switch (evt)
    {
        case MoneyDeposited d:
            state.Balance += d.Amount;  // 只修改 state 参数
            break;
    }
}
```

2. **批量确认事件**
```csharp
// ✅ 批量处理多个事件，减少 I/O
public async Task BatchOperations()
{
    RaiseEvent(new MoneyDeposited { Amount = 100 });
    RaiseEvent(new MoneyDeposited { Amount = 200 });
    RaiseEvent(new MoneyWithdrawn { Amount = 50 });
    await ConfirmEventsAsync();  // 一次性持久化
}
```

3. **配置合适的快照频率**
```csharp
// ✅ 平衡性能和存储
protected override ISnapshotStrategy SnapshotStrategy =>
    new IntervalSnapshotStrategy(100);  // 每 100 个事件快照
```

### ❌ 不应该做的

1. **在 TransitionState 中有副作用**
```csharp
protected override void TransitionState(BankAccountState state, IMessage evt)
{
    switch (evt)
    {
        case MoneyDeposited d:
            state.Balance += d.Amount;
            _externalService.Notify(...);  // ❌ 不要调用外部服务
            File.WriteAllText(...);         // ❌ 不要 I/O 操作
            break;
    }
}
```

2. **忘记调用 ConfirmEventsAsync()**
```csharp
public async Task Deposit(decimal amount)
{
    RaiseEvent(new MoneyDeposited { Amount = amount });
    // ❌ 忘记 Confirm，事件不会持久化
}
```

3. **过度频繁创建快照**
```csharp
// ❌ 每 1 个事件就快照（浪费存储）
protected override ISnapshotStrategy SnapshotStrategy =>
    new IntervalSnapshotStrategy(1);
```

---

## 🎭 Framework 集成

### 依赖注入

```csharp
// Configure EventSourcing for Orleans runtime
services.AddSingleton<IEventStore, OrleansEventStore>();

// Or use Local runtime
services.AddSingleton<IEventStore, InMemoryEventStore>(); // No persistence

// Or use custom implementation
services.AddSingleton<IEventStore, YourCustomEventStore>();
```

### Runtime 配置

所有运行时使用相同的 `GAgentBaseWithEventSourcing` 基类：

```csharp
// Local Runtime
services.AddSingleton<IGAgentActorFactory, LocalGAgentActorFactory>();

// Orleans Runtime
services.AddSingleton<IGAgentActorFactory, OrleansGAgentActorFactory>();

// ProtoActor Runtime
services.AddSingleton<IGAgentActorFactory, ProtoActorGAgentActorFactory>();

// Agent 定义（运行时无关）
public class BankAccountAgent : GAgentBaseWithEventSourcing<BankAccountState>
{
    // 同一份代码可在任何 Runtime 中运行！
}
```

---

## 📈 性能调优

| 优化策略 | 效果 | 使用场景 |
|---------|------|---------|
| **快照** | 减少 90% 回放时间 | 事件数 > 100 |
| **批量确认** | 减少 50% I/O | 多个事件一起产生 |
| **事件过滤** | 减少内存使用 | 回放时只关心某些事件 |
| **压缩** | 减少 70% 存储 | 事件很大且重复 |

---

## ✅ EventSourcing 的核心优势

### 1. 完整审计
```
可以看到完整的历史：
  - 余额何时变化
  - 变化的原因是什么
  - 谁执行的这些操作
```

### 2. 状态回放
```
csharp
// 回放到任意时间点
var stateAtVersion10 = await GetStateAtVersion(10);
var stateLastWeek = await GetStateAtTime(DateTime.UtcNow.AddDays(-7));
```

### 3. 调试友好
```
// 重现问题
var allEvents = await eventStore.GetEventsAsync(agentId);
foreach (var evt in allEvents)
{
    Console.WriteLine($"Event: {evt.GetType().Name}, Data: {evt}");
}
```

### 4. 性能优化
```
// 通过缓存已回放的状态
// 支持快照 + 增量回放
// 适合高频事件场景
```

---

## 📚 相关资源

### 代码示例
- `examples/MongoDBEventStoreDemo/` - 完整的 MongoDB + EventSourcing 示例
- `test/Aevatar.Agents.Orleans.Tests/OrleansActorFactoryTests.cs` - Factory 测试

### 相关文档
- `CORE_CONCEPTS.md` - 核心概念（Protobuf、Stream、EventDirection）
- `docs/保留GAgentBaseWithEventSourcing论证.md` - 架构决策论证

### 实现细节
- `src/Aevatar.Agents.Core/EventSourcing/GAgentBaseWithEventSourcing.cs` - 事件溯源基类
- `src/Aevatar.Agents.Core/Helpers/AgentEventStoreInjector.cs` - EventStore 注入器
- `src/Aevatar.Agents.Runtime.Orleans/OrleansGAgentActor.cs` - Orleans Actor 层回放逻辑
- `src/Aevatar.Agents.Orleans/OrleansEventStore.cs` - Orleans EventStore 实现
- `src/Aevatar.Agents.Orleans/Repositories/MongoEventRepository.cs` - MongoDB 后端

---

## ⚠️ 常见陷阱和解决方案

### 问题 1：状态没有恢复

**现象**：Grain 重启后状态为初始值

**原因**：
- IEventStore 未注册
- Agent 不是继承自 GAgentBaseWithEventSourcing<T>

**解决**：
```csharp
// 确保注册
services.AddSingleton<IEventStore, OrleansEventStore>();

// 确保继承正确的基类
public class MyAgent : GAgentBaseWithEventSourcing<MyState> // ✅
```

### 问题 2：事件版本不匹配

**现象**：回放事件时报错

**原因**：
- Proto 消息定义更改后没有正确处理版本兼容

**解决**：
```protobuf
// 始终使用向后兼容的更改
// ✅ 添加字段时使用 optional/repeated
optional string new_field = 10;

// ❌ 不要删除或更改现有字段
```

### 问题 3：性能慢

**现象**：回放大量事件时很慢

**原因**：
- 没有配置快照
- 事件数太多

**解决**：
```csharp
// 配置快照
protected override ISnapshotStrategy SnapshotStrategy =>
    new IntervalSnapshotStrategy(100);
```

---

**记住**：EventSourcing 是一个强大的模式，但应该**有意为之**。如果不需要完整审计历史，使用普通的 `GAgentBase<TState>` 更简单。
