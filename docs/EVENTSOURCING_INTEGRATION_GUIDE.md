# EventSourcing 实际场景集成指南

I'm HyperEcho, 我在**抽象与实践的桥梁时刻**

## 🎯 核心问题

**如何将 IEventStore 抽象与 Local/Orleans 实际场景结合？**

---

## 📐 场景 1: Local Runtime (开发/测试)

### 1.1 架构图

```
┌─────────────────────────────────────────────────────┐
│  BankAccountAgent : GAgentBaseWithEventSourcing     │
│  ├── RaiseEvent(MoneyDeposited)                    │
│  └── State: BankAccountState (Protobuf)            │
└─────────────────────────────────────────────────────┘
                    ↓ uses
┌─────────────────────────────────────────────────────┐
│  LocalGAgentActor (Actor 包装)                      │
│  └── _eventStore: InMemoryEventStore               │
└─────────────────────────────────────────────────────┘
                    ↓ stores in
┌─────────────────────────────────────────────────────┐
│  InMemoryEventStore                                 │
│  ├── _events: ConcurrentDict<Guid, List<Event>>    │
│  └── _snapshots: ConcurrentDict<Guid, Snapshot>    │
└─────────────────────────────────────────────────────┘
```

### 1.2 实际代码示例

#### Step 1: 定义 Agent 和 State

```csharp
// BankAccountState.proto
message BankAccountState {
    string account_id = 1;
    double balance = 2;
    int64 version = 3;
}

// BankAccountEvents.proto
message MoneyDeposited {
    double amount = 1;
    google.protobuf.Timestamp timestamp = 2;
}

message MoneyWithdrawn {
    double amount = 1;
    google.protobuf.Timestamp timestamp = 2;
}
```

#### Step 2: 实现 Agent (使用 EventSourcing)

```csharp
public class BankAccountAgent : GAgentBaseWithEventSourcing<BankAccountState>
{
    public BankAccountAgent(
        Guid id,
        IEventStore eventStore,
        ILogger<BankAccountAgent> logger)
        : base(id, eventStore, logger)
    {
    }
    
    // 业务方法：存款
    public async Task DepositAsync(double amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive");
        
        // 1. 创建事件
        var evt = new MoneyDeposited
        {
            Amount = amount,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        
        // 2. 触发事件（自动持久化到 IEventStore）
        await RaiseStateChangeEventAsync(evt);
        
        Logger.LogInformation("Deposited {Amount}, new balance: {Balance}",
            amount, State.Balance);
    }
    
    // 业务方法：取款
    public async Task WithdrawAsync(double amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive");
        
        if (State.Balance < amount)
            throw new InvalidOperationException("Insufficient balance");
        
        var evt = new MoneyWithdrawn
        {
            Amount = amount,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        
        await RaiseStateChangeEventAsync(evt);
        
        Logger.LogInformation("Withdrawn {Amount}, new balance: {Balance}",
            amount, State.Balance);
    }
    
    // 事件处理：应用事件到状态
    protected override Task ApplyStateChangeEventAsync<TEvent>(
        TEvent evt,
        CancellationToken ct = default)
    {
        switch (evt)
        {
            case MoneyDeposited deposited:
                State.Balance += deposited.Amount;
                break;
                
            case MoneyWithdrawn withdrawn:
                State.Balance -= withdrawn.Amount;
                break;
        }
        
        return Task.CompletedTask;
    }
}
```

#### Step 3: Local 场景使用

```csharp
// Program.cs - Local Runtime
public class Program
{
    public static async Task Main(string[] args)
    {
        // 1. 创建 EventStore (内存实现)
        var eventStore = new InMemoryEventStore();
        
        // 2. 创建 Agent
        var accountId = Guid.NewGuid();
        var account = new BankAccountAgent(
            accountId,
            eventStore,
            loggerFactory.CreateLogger<BankAccountAgent>());
        
        // 3. 激活 Agent（自动从 EventStore 重放事件）
        await account.OnActivateAsync();
        
        // 4. 执行业务操作
        await account.DepositAsync(100);   // Event 1
        await account.DepositAsync(50);    // Event 2
        await account.WithdrawAsync(30);   // Event 3
        
        Console.WriteLine($"Final Balance: {account.State.Balance}"); // 120
        
        // 5. 模拟 Agent 重启（重放事件）
        var account2 = new BankAccountAgent(
            accountId,
            eventStore,  // 同一个 EventStore
            loggerFactory.CreateLogger<BankAccountAgent>());
        
        await account2.OnActivateAsync();  // ← 自动重放 3 个事件
        
        Console.WriteLine($"After Replay: {account2.State.Balance}"); // 120
    }
}
```

#### Step 4: 快照优化 (自动触发)

```csharp
// GAgentBaseWithEventSourcing 内部逻辑
protected async Task RaiseStateChangeEventAsync<TEvent>(TEvent evt, ...)
{
    // ... 持久化事件
    
    // 每 100 个事件自动创建快照
    if (_currentVersion % 100 == 0)
    {
        var snapshot = new AgentSnapshot
        {
            Version = _currentVersion,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            StateData = Any.Pack(State)  // 当前状态
        };
        
        await _eventStore.SaveSnapshotAsync(Id, snapshot);
    }
}

// 重放时优化
public async Task ReplayEventsAsync(...)
{
    // 1. 先加载快照（版本 100）
    var snapshot = await _eventStore.GetLatestSnapshotAsync(Id);
    if (snapshot != null)
    {
        State = snapshot.StateData.Unpack<BankAccountState>();
        _currentVersion = 100;
    }
    
    // 2. 只重放快照后的事件（101-150）
    var events = await _eventStore.GetEventsAsync(
        Id,
        fromVersion: _currentVersion + 1);  // 只取 50 个事件！
    
    // 3. 应用增量事件
    foreach (var evt in events)
    {
        await ApplyStateChangeEventAsync(evt);
    }
}
```

---

## 📐 场景 2: Orleans Runtime (分布式生产)

### 2.1 架构图

```
┌─────────────────────────────────────────────────────┐
│  BankAccountAgent : GAgentBaseWithEventSourcing     │
│  (业务逻辑，与 Local 完全相同)                        │
└─────────────────────────────────────────────────────┘
                    ↓ runs in
┌─────────────────────────────────────────────────────┐
│  OrleansGAgentGrain (Orleans Grain)                 │
│  ├── _agent: BankAccountAgent                      │
│  └── _eventStore: OrleansEventStore                │
└─────────────────────────────────────────────────────┘
                    ↓ stores in
┌─────────────────────────────────────────────────────┐
│  OrleansEventStore                                  │
│  ├── Events → Orleans GrainStorage (Table Storage)  │
│  └── Snapshots → Orleans GrainStorage (Blob)        │
└─────────────────────────────────────────────────────┘
```

### 2.2 OrleansEventStore 实现

```csharp
/// <summary>
/// Orleans 实现的 EventStore
/// 使用 Orleans GrainStorage 持久化事件和快照
/// </summary>
public class OrleansEventStore : IEventStore
{
    private readonly IGrainStorage _eventStorage;
    private readonly IGrainStorage _snapshotStorage;
    private readonly ILogger<OrleansEventStore> _logger;
    
    public OrleansEventStore(
        [PersistentState("events", "EventStore")] IGrainStorage eventStorage,
        [PersistentState("snapshots", "SnapshotStore")] IGrainStorage snapshotStorage,
        ILogger<OrleansEventStore> logger)
    {
        _eventStorage = eventStorage;
        _snapshotStorage = snapshotStorage;
        _logger = logger;
    }
    
    public async Task<long> AppendEventsAsync(
        Guid agentId,
        IEnumerable<AgentStateEvent> events,
        long expectedVersion,
        CancellationToken ct = default)
    {
        var grainId = GrainId.Create("agent-events", agentId.ToString());
        
        // 1. 读取当前状态
        var state = new EventStreamState();
        await _eventStorage.ReadStateAsync(
            "EventStream",
            grainId,
            state);
        
        // 2. 乐观并发检查
        if (state.State.Version != expectedVersion)
        {
            throw new ConcurrencyException(
                $"Version conflict: expected {expectedVersion}, got {state.State.Version}");
        }
        
        // 3. 追加事件
        var newVersion = expectedVersion;
        foreach (var evt in events)
        {
            evt.Version = ++newVersion;
            state.State.Events.Add(evt);
        }
        state.State.Version = newVersion;
        
        // 4. 持久化到 Orleans Storage
        await _eventStorage.WriteStateAsync(
            "EventStream",
            grainId,
            state);
        
        _logger.LogInformation(
            "Appended {Count} events for agent {AgentId}, version: {Version}",
            events.Count(), agentId, newVersion);
        
        return newVersion;
    }
    
    public async Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        Guid agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        CancellationToken ct = default)
    {
        var grainId = GrainId.Create("agent-events", agentId.ToString());
        var state = new EventStreamState();
        
        await _eventStorage.ReadStateAsync(
            "EventStream",
            grainId,
            state);
        
        var query = state.State.Events.AsEnumerable();
        
        if (fromVersion.HasValue)
            query = query.Where(e => e.Version >= fromVersion.Value);
        
        if (toVersion.HasValue)
            query = query.Where(e => e.Version <= toVersion.Value);
        
        if (maxCount.HasValue)
            query = query.Take(maxCount.Value);
        
        return query.ToList();
    }
    
    public async Task SaveSnapshotAsync(
        Guid agentId,
        AgentSnapshot snapshot,
        CancellationToken ct = default)
    {
        var grainId = GrainId.Create("agent-snapshot", agentId.ToString());
        var state = new SnapshotState { State = snapshot };
        
        await _snapshotStorage.WriteStateAsync(
            "Snapshot",
            grainId,
            state);
        
        _logger.LogInformation(
            "Saved snapshot for agent {AgentId} at version {Version}",
            agentId, snapshot.Version);
    }
    
    public async Task<AgentSnapshot?> GetLatestSnapshotAsync(
        Guid agentId,
        CancellationToken ct = default)
    {
        var grainId = GrainId.Create("agent-snapshot", agentId.ToString());
        var state = new SnapshotState();
        
        await _snapshotStorage.ReadStateAsync(
            "Snapshot",
            grainId,
            state);
        
        return state.State;
    }
    
    public async Task<long> GetLatestVersionAsync(
        Guid agentId,
        CancellationToken ct = default)
    {
        var events = await GetEventsAsync(agentId, ct: ct);
        return events.Any() ? events.Max(e => e.Version) : 0;
    }
}

// Orleans Storage State 包装类
[GenerateSerializer]
public class EventStreamState : GrainState<EventStreamData>
{
}

[GenerateSerializer]
public class EventStreamData
{
    [Id(0)]
    public List<AgentStateEvent> Events { get; set; } = new();
    
    [Id(1)]
    public long Version { get; set; }
}

[GenerateSerializer]
public class SnapshotState : GrainState<AgentSnapshot?>
{
}
```

### 2.3 OrleansGAgentGrain 集成

```csharp
/// <summary>
/// Orleans Grain 标准实现（不使用 JournaledGrain）
/// 可选集成 IEventStore 提供 EventSourcing
/// </summary>
public class OrleansGAgentGrain : Grain, IGAgentGrain
{
    private IGAgent? _agent;
    private IEventStore? _eventStore;  // ← 可选
    private readonly ILogger<OrleansGAgentGrain> _logger;
    
    public OrleansGAgentGrain(
        ILogger<OrleansGAgentGrain> logger,
        IEventStore? eventStore = null)  // ← 通过 DI 注入
    {
        _logger = logger;
        _eventStore = eventStore;
    }
    
    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        
        var grainId = this.GetPrimaryKey();
        _logger.LogInformation("Grain activated: {GrainId}", grainId);
        
        // 如果配置了 EventStore，自动重放
        if (_agent is GAgentBaseWithEventSourcing<object> esAgent && _eventStore != null)
        {
            _logger.LogInformation("Replaying events for agent {AgentId}", grainId);
            await esAgent.ReplayEventsAsync(ct);
        }
    }
    
    public async Task InitializeAsync(IGAgent agent)
    {
        _agent = agent;
        
        // 如果是 EventSourcing Agent，注入 EventStore
        if (_agent is GAgentBaseWithEventSourcing<object> esAgent && _eventStore != null)
        {
            // 通过反射或扩展方法注入（需要在 GAgentBaseWithEventSourcing 中添加 SetEventStore）
            esAgent.SetEventStore(_eventStore);
        }
    }
    
    // ... 其他 IGAgentGrain 方法
}
```

### 2.4 Orleans 场景使用

```csharp
// Program.cs - Orleans Silo
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseOrleans(siloBuilder =>
{
    siloBuilder
        .UseLocalhostClustering()
        
        // 配置事件存储
        .AddMemoryGrainStorage("EventStore")
        .AddMemoryGrainStorage("SnapshotStore")
        
        // 或者使用 Azure Table Storage
        // .AddAzureTableGrainStorage("EventStore", 
        //     options => options.ConnectionString = "...")
        // .AddAzureBlobGrainStorage("SnapshotStore",
        //     options => options.ConnectionString = "...")
        
        // 注册 IEventStore
        .ConfigureServices(services =>
        {
            services.AddSingleton<IEventStore, OrleansEventStore>();
        });
});

// Client 使用
var client = app.Services.GetRequiredService<IGrainFactory>();
var accountId = Guid.NewGuid();

// 获取 Grain (Orleans 自动创建/激活)
var grain = client.GetGrain<IGAgentGrain>(accountId);

// 初始化 Agent
var account = new BankAccountAgent(
    accountId,
    eventStore,  // ← OrleansEventStore
    logger);

await grain.InitializeAsync(account);

// 执行业务操作（Orleans 自动持久化事件）
await grain.InvokeAsync(async agent =>
{
    var bankAgent = (BankAccountAgent)agent;
    await bankAgent.DepositAsync(100);   // Event → Azure Table
    await bankAgent.DepositAsync(50);    // Event → Azure Table
    await bankAgent.WithdrawAsync(30);   // Event → Azure Table
});

// Grain 自动 Deactivate 后再 Activate
// 事件会自动从 Azure Table 重放
```

---

## 📊 场景对比

| 维度 | Local (InMemory) | Orleans (Production) |
|-----|-----------------|---------------------|
| **EventStore 实现** | `InMemoryEventStore` | `OrleansEventStore` |
| **事件存储** | `ConcurrentDictionary` | `Azure Table / GrainStorage` |
| **快照存储** | `ConcurrentDictionary` | `Azure Blob / GrainStorage` |
| **重放触发** | 手动 `OnActivateAsync()` | Grain 激活时自动 |
| **并发控制** | 简单 lock | 乐观并发 + Orleans 保证 |
| **持久化** | ❌ 内存，进程重启丢失 | ✅ 持久化到存储 |
| **分布式** | ❌ 单节点 | ✅ 多节点，Orleans 管理 |
| **适用场景** | 开发/测试 | 生产环境 |

---

## 🔄 关键交互流程

### 流程 1: 事件持久化

```
User Code
    └── agent.DepositAsync(100)
            └── RaiseStateChangeEventAsync(MoneyDeposited)
                    ├── 1. 创建 AgentStateEvent (Protobuf)
                    │   └── EventData = Any.Pack(MoneyDeposited)
                    │
                    ├── 2. 持久化到 IEventStore
                    │   └── eventStore.AppendEventsAsync(
                    │           agentId, [event], expectedVersion)
                    │       ├── Local: 写入 ConcurrentDictionary
                    │       └── Orleans: 写入 GrainStorage
                    │
                    ├── 3. 应用事件到状态
                    │   └── ApplyStateChangeEventAsync(MoneyDeposited)
                    │       └── State.Balance += 100
                    │
                    └── 4. 检查快照
                        └── if (version % 100 == 0)
                            └── SaveSnapshotAsync(snapshot)
```

### 流程 2: 事件重放 (快照优化)

```
Agent Activation
    └── OnActivateAsync()
            └── ReplayEventsAsync()
                    ├── 1. 加载快照
                    │   └── snapshot = eventStore.GetLatestSnapshotAsync()
                    │       ├── 找到快照（版本 100）
                    │       │   └── State = snapshot.StateData.Unpack()
                    │       └── 无快照
                    │           └── State = new()
                    │
                    ├── 2. 获取增量事件
                    │   └── events = eventStore.GetEventsAsync(
                    │           fromVersion: snapshot.Version + 1)
                    │       └── 只取 101-150 (50 个事件)
                    │
                    └── 3. 应用增量事件
                        └── foreach (event in events)
                            └── ApplyStateChangeEventAsync(event)

性能对比:
❌ 无快照: 重放 150 个事件
✅ 有快照: 重放 50 个事件 (快 3 倍)
```

---

## 💡 最佳实践

### 1. 快照策略

```csharp
// 配置快照间隔
public abstract class GAgentBaseWithEventSourcing<TState>
{
    protected virtual int SnapshotInterval => 100;  // 可重写
    
    protected virtual bool ShouldCreateSnapshot(long version)
    {
        // 策略 A: 固定间隔
        return version % SnapshotInterval == 0;
        
        // 策略 B: 时间间隔
        // return (DateTime.UtcNow - lastSnapshotTime) > TimeSpan.FromMinutes(5);
        
        // 策略 C: 事件数量 + 时间
        // return (version % 100 == 0) || 
        //        (DateTime.UtcNow - lastSnapshotTime) > TimeSpan.FromMinutes(10);
    }
}
```

### 2. 事件版本控制

```csharp
// 事件演化：使用 Protobuf 版本化
message MoneyDepositedV1 {  // 旧版本
    double amount = 1;
}

message MoneyDepositedV2 {  // 新版本
    double amount = 1;
    string currency = 2;     // 新增字段
    string description = 3;  // 新增字段
}

// 重放时处理版本兼容
protected override Task ApplyStateChangeEventAsync<TEvent>(TEvent evt, ...)
{
    if (evt is MoneyDepositedV1 v1)
    {
        // 升级到 V2
        var v2 = new MoneyDepositedV2
        {
            Amount = v1.Amount,
            Currency = "USD",  // 默认值
            Description = "Legacy deposit"
        };
        State.Balance += v2.Amount;
    }
    else if (evt is MoneyDepositedV2 v2)
    {
        State.Balance += v2.Amount;
    }
    
    return Task.CompletedTask;
}
```

### 3. 性能优化

```csharp
// 批量事件追加
public async Task ProcessBatchAsync(List<Transaction> transactions)
{
    var events = transactions.Select(t => new MoneyDeposited { Amount = t.Amount });
    
    // 一次性持久化多个事件
    var stateEvents = events.Select((e, i) => new AgentStateEvent
    {
        EventId = Guid.NewGuid().ToString(),
        EventType = "MoneyDeposited",
        EventData = Any.Pack(e),
        Version = _currentVersion + i + 1
    }).ToList();
    
    _currentVersion = await _eventStore.AppendEventsAsync(
        Id,
        stateEvents,
        _currentVersion);
    
    // 批量应用
    foreach (var evt in events)
    {
        await ApplyStateChangeEventAsync(evt);
    }
}
```

---

## ✅ 总结

### IEventStore 抽象的价值

1. **运行时无关** - 同一业务代码，Local/Orleans 无缝切换
2. **实现灵活** - InMemory/Orleans/Database 可替换
3. **快照优化** - 自动快照，重放性能提升
4. **并发安全** - 乐观并发控制
5. **演化友好** - Protobuf 支持事件版本化

### 使用建议

```csharp
// 开发阶段：Local + InMemoryEventStore
var eventStore = new InMemoryEventStore();
var agent = new MyAgent(id, eventStore, logger);

// 生产阶段：Orleans + OrleansEventStore
services.AddSingleton<IEventStore, OrleansEventStore>();
// Orleans 自动注入和重放
```

---

*抽象的力量在于统一接口，实现的智慧在于适配场景* 🌌

