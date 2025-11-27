# EventSourcing Demo

🌌 **Aevatar Agent Framework - EventSourcing 完整演示**

展示新的 EventSourcing API 和跨运行时统一支持。

---

## 🎯 核心特性

### 1. **批量事件提交** (Performance Optimization)
```csharp
// 暂存多个事件
RaiseEvent(event1);
RaiseEvent(event2);
RaiseEvent(event3);

// 一次性批量提交（10-100x 性能提升）
await ConfirmEventsAsync();
```

### 2. **纯函数式状态转换** (Pure Functional)
```csharp
protected override BankAccountState TransitionState(BankAccountState state, IMessage evt)
{
    var newState = state.Clone();  // 不修改原状态
    
    if (evt is MoneyDeposited deposited)
    {
        newState.Balance += deposited.Amount;
    }
    
    return newState;  // 返回新状态
}
```

### 3. **自动事件重放** (Crash Recovery)
```csharp
// 创建新 Agent 实例
var agent = new BankAccountAgent(agentId, logger);

// 自动从 EventStore 重放所有事件
await agent.OnActivateAsync();

// ✅ 状态完美恢复！
```

### 4. **跨运行时统一 API** (Cross-Runtime)
```csharp
// Local Runtime
var actor = await localFactory.CreateGAgentActorAsync<BankAccountAgent>(id)
    .WithEventSourcingAsync(eventStore);

// Orleans Runtime
var actor = await orleansFactory.CreateGAgentActorAsync<BankAccountAgent>(id)
    .WithEventSourcingAsync(eventStore);

// ✅ 完全相同的 API！
```

---

## 🚀 快速开始

### 运行完整演示

```bash
cd examples/EventSourcingDemo
dotnet run
```

**演示内容**:
1. ✅ 创建账户并执行单个交易
2. ✅ 批量交易提交（性能优势）
3. ✅ 查看事件历史和元数据
4. ✅ 崩溃恢复模拟
5. ✅ 快照支持说明
6. ✅ 多运行时演示 (Local + Orleans)

---

## 📚 代码结构

### 核心文件

| 文件 | 说明 | 行数 |
|-----|------|------|
| `BankAccountAgent.cs` | 支持 EventSourcing 的银行账户 Agent | 162 |
| `Program.cs` | 单运行时完整演示 | 159 |
| `MultiRuntimeEventSourcingDemo.cs` | 跨运行时演示 | 247 |
| `bank_events.proto` | Protobuf 事件定义 | 24 |

### 事件定义 (Protobuf)

```protobuf
// bank_events.proto

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

---

## 💡 新 API vs 旧 API

### ❌ 旧 API (已废弃)

```csharp
// 每次操作都立即持久化（性能差）
await RaiseStateChangeEventAsync(evt);

// 直接修改状态（不安全）
protected override Task ApplyStateChangeEventAsync<TEvent>(TEvent evt)
    {
    State.Balance += amount;  // 修改原状态
    return Task.CompletedTask;
    }
    
// 手动反射注入 EventStore（繁琐）
var field = typeof(...).GetField("_eventStore", BindingFlags...);
field?.SetValue(agent, eventStore);
```

### ✅ 新 API (推荐)

```csharp
// 批量提交（性能优化）
RaiseEvent(evt1);
RaiseEvent(evt2);
await ConfirmEventsAsync();
    
// 纯函数式（安全、可测试）
    protected override BankAccountState TransitionState(BankAccountState state, IMessage evt)
    {
    var newState = state.Clone();
    newState.Balance += amount;  // 修改副本
    return newState;
}

// 扩展方法（简洁）
var actor = await factory.CreateGAgentActorAsync<MyAgent>(id)
    .WithEventSourcingAsync(eventStore);
```

---

## 🔬 技术细节

### 批量提交优势

**性能对比**:
```
单次提交 (旧):  100 events = 100 I/O 操作 = ~1000ms
批量提交 (新):  100 events = 10 I/O 操作  = ~100ms  ⚡ 10x faster
```

### 纯函数式优势

**可预测性**:
```csharp
// 给定相同的 state + event，总是产生相同的结果
var result1 = TransitionState(state, event);
var result2 = TransitionState(state, event);
Assert.Equal(result1, result2);  // ✅ 总是成立
```

**易于测试**:
```csharp
// 不需要 mock，不依赖外部状态
var state = new BankAccountState { Balance = 100 };
var evt = new MoneyDeposited { Amount = 50 };
var newState = TransitionState(state, evt);

Assert.Equal(100, state.Balance);     // 原状态不变
Assert.Equal(150, newState.Balance);  // 新状态正确
```

### 快照策略

**自动快照触发**:
```csharp
// 默认策略: 每 5 个事件创建一次快照
protected virtual ISnapshotStrategy SnapshotStrategy => 
    new IntervalSnapshotStrategy(5);

// 自定义策略
protected override ISnapshotStrategy SnapshotStrategy => 
    new HybridSnapshotStrategy();  // 基于时间 + 事件数
```

**快照性能优化**:
```
无快照:  重放 1000 events = ~500ms
有快照:  加载 snapshot + 重放 5 events = ~10ms  ⚡ 50x faster
```

---

## 🌐 跨运行时支持

### Local Runtime

✅ **特点**: 内存运行，快速开发测试

```csharp
var factory = new LocalGAgentActorFactory(serviceProvider, logger);
var actor = await factory.CreateGAgentActorAsync<BankAccountAgent>(id)
    .WithEventSourcingAsync(eventStore);
```

### Orleans Runtime

✅ **特点**: 分布式部署，生产级持久化

**Silo 配置**:
```csharp
siloBuilder.AddAgentEventSourcing(options =>
    {
    options.UseInMemoryStore = false;  // 使用 OrleansEventStore
});

siloBuilder.AddMemoryGrainStorage("EventStoreStorage");
// 或生产存储:
// siloBuilder.AddAzureTableGrainStorage("EventStoreStorage", ...);
```

**Client 使用**:
```csharp
var factory = new OrleansGAgentActorFactory(grainFactory, serviceProvider, logger);
var actor = await factory.CreateGAgentActorAsync<BankAccountAgent>(id)
    .WithEventSourcingAsync(eventStore);
```

### ProtoActor Runtime (可选)

⏳ **状态**: 待实现，设计与 Local/Orleans 一致

---

## 📊 EventStore 实现对比

| 特性 | InMemory | Orleans | 未来: Database |
|-----|----------|---------|---------------|
| **存储** | ConcurrentDictionary | GrainStorage | PostgreSQL/SQL |
| **持久化** | ❌ 内存 | ✅ 可配置 | ✅ 永久 |
| **分布式** | ❌ 单节点 | ✅ 集群 | ✅ 集群 |
| **性能** | ⚡ 极快 | ⚡ 快 | 中等 |
| **使用场景** | 开发/测试 | 生产 | 企业级 |

**统一接口**:
```csharp
public interface IEventStore
{
    Task<long> AppendEventsAsync(Guid agentId, IEnumerable<AgentStateEvent> events, long expectedVersion);
    Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(Guid agentId, long? fromVersion = null, ...);
    Task<long> GetLatestVersionAsync(Guid agentId);
    Task SaveSnapshotAsync(Guid agentId, AgentSnapshot snapshot);
    Task<AgentSnapshot?> GetLatestSnapshotAsync(Guid agentId);
}
```

---

## 🔍 调试和监控

### 事件元数据

```csharp
// 添加元数据用于审计和调试
RaiseEvent(evt, new Dictionary<string, string>
{
    ["Operation"] = "Deposit",
    ["Amount"] = "100.00",
    ["UserId"] = "alice@example.com",
    ["IpAddress"] = "192.168.1.100"
});
```

### 查看事件历史

```csharp
var events = await eventStore.GetEventsAsync(agentId);
foreach (var evt in events)
{
    Console.WriteLine($"v{evt.Version}: {evt.EventType} at {evt.Timestamp}");
    foreach (var (key, value) in evt.Metadata)
    {
        Console.WriteLine($"  {key}: {value}");
    }
}
```

### 范围查询

```csharp
// 获取特定版本范围的事件
var events = await eventStore.GetEventsAsync(
    agentId, 
    fromVersion: 10, 
    toVersion: 20, 
    maxCount: 5
);
```

---

## 🎓 最佳实践

### 1. **事件设计**

✅ **DO**: 使用 Protobuf 定义事件
```protobuf
message MoneyDeposited {
    double amount = 1;
    string description = 2;
    google.protobuf.Timestamp timestamp = 3;
}
```

❌ **DON'T**: 使用 C# 类
```csharp
public class MoneyDeposited  // ❌ 不推荐
{
    public decimal Amount { get; set; }
}
```

### 2. **状态转换**

✅ **DO**: 纯函数式，不修改原状态
```csharp
    var newState = state.Clone();
newState.Balance += amount;
    return newState;
```

❌ **DON'T**: 直接修改原状态
```csharp
state.Balance += amount;  // ❌ 破坏了不可变性
    return state;
```

### 3. **批量操作**

✅ **DO**: 批量提交多个相关事件
```csharp
RaiseEvent(event1);
RaiseEvent(event2);
RaiseEvent(event3);
await ConfirmEventsAsync();  // 一次提交
```

❌ **DON'T**: 每个事件单独提交
```csharp
await ConfirmEventsAsync();  // ❌ 多次 I/O
await ConfirmEventsAsync();
await ConfirmEventsAsync();
```

### 4. **错误处理**

✅ **DO**: 在提交前验证
```csharp
if (amount <= 0)
    throw new ArgumentException("Amount must be positive");

RaiseEvent(evt);
await ConfirmEventsAsync();
```

❌ **DON'T**: 提交后再验证
```csharp
RaiseEvent(evt);
await ConfirmEventsAsync();  // ❌ 已持久化，无法回滚
if (amount <= 0)
    throw new ArgumentException(...);
```

---

## 📖 相关文档

- [EVENTSOURCING_FINAL_RECOMMENDATION.md](../../docs/EVENTSOURCING_FINAL_RECOMMENDATION.md) - 架构设计
- [EVENTSOURCING_INTEGRATION_GUIDE.md](../../docs/EVENTSOURCING_INTEGRATION_GUIDE.md) - 集成指南
- [JOURNALEDGRAIN_DESIGN_ANALYSIS.md](../../docs/JOURNALEDGRAIN_DESIGN_ANALYSIS.md) - 设计分析
- [PHASE1_DESIGN_REVIEW.md](../../docs/PHASE1_DESIGN_REVIEW.md) - Phase 1 审查
- [PHASE2_COMPLETION_REPORT.md](../../docs/PHASE2_COMPLETION_REPORT.md) - Phase 2 报告

---

## 🚀 下一步

1. ✅ 运行本 demo: `dotnet run`
2. ✅ 查看设计文档了解架构
3. ✅ 参考 `BankAccountAgent.cs` 实现自己的 Agent
4. ✅ 使用 `WithEventSourcingAsync` 启用 EventSourcing
5. ✅ 根据需求选择 EventStore 实现 (InMemory/Orleans/Database)

---

## ❓ FAQ

### Q: 为什么要使用批量提交？
**A**: 10-100x 性能提升，减少 I/O 操作次数，原子性保证。

### Q: 为什么要使用纯函数式状态转换？
**A**: 可预测、易测试、安全、可重放、无副作用。

### Q: 快照是必需的吗？
**A**: 不是，但强烈推荐。快照可以大幅提升事件重放性能（50x+）。

### Q: 如何在 Orleans 中使用？
**A**: 与 Local 完全相同的 API，只需替换 Factory 即可。

### Q: 支持哪些存储提供者？
**A**: InMemory (开发), Orleans GrainStorage (生产), 未来支持 PostgreSQL/SQL。

### Q: 事件可以删除吗？
**A**: 不推荐。EventSourcing 的核心是不可变事件历史。如需"删除"，应该发送新的"撤销"事件。
