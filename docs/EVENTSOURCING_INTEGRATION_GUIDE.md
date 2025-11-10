# EventSourcing 集成指南 (V2)

**最后更新**: 2025-11-10  
**API版本**: EventSourcing V2 (批量提交 + 纯函数式)

---

## 🎯 核心概念

EventSourcing V2 提供了生产级的事件溯源能力，具有以下特性：

- ✅ **批量事件提交** - `RaiseEvent()` + `ConfirmEventsAsync()` (10-100x性能提升)
- ✅ **纯函数式状态转换** - `TransitionState()` 纯函数，易于测试
- ✅ **自动事件重放** - Agent激活时自动恢复状态
- ✅ **跨运行时统一** - Local/Orleans/ProtoActor 相同API
- ✅ **Protobuf序列化** - 高效且版本兼容

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

#### Step 2: 实现 Agent (使用 EventSourcing V2)

```csharp
public class BankAccountAgent : GAgentBaseWithEventSourcing<BankAccountState>
{
    public BankAccountAgent(Guid id, ILogger<BankAccountAgent> logger)
        : base(id, logger)
    {
    }
    
    // 业务方法：存款 (新API)
    public async Task DepositAsync(double amount, string description)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive");
        
        // 1. 创建事件
        var evt = new MoneyDeposited
        {
            Amount = amount,
            Description = description,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        
        // 2. 暂存事件（不立即持久化）
        RaiseEvent(evt);  // ← 新API
        
        // 3. 批量提交（一次I/O）
        await ConfirmEventsAsync();  // ← 新API
        
        Logger.LogInformation("Deposited {Amount}, new balance: {Balance}",
            amount, GetState().Balance);
    }
    
    // 业务方法：批量交易 (性能优化)
    public async Task ProcessTransactionsAsync(List<Transaction> transactions)
    {
        foreach (var t in transactions)
        {
            if (t.Type == "deposit")
            {
                RaiseEvent(new MoneyDeposited 
                { 
                    Amount = t.Amount, 
                    Description = t.Description 
                });
            }
            else
            {
                RaiseEvent(new MoneyWithdrawn 
                { 
                    Amount = t.Amount, 
                    Description = t.Description 
                });
            }
        }
        
        // 一次性提交所有事件（性能提升10-100x）
        await ConfirmEventsAsync();
    }
    
    // 纯函数式状态转换（新API）
    protected override BankAccountState TransitionState(
        BankAccountState state,
        IMessage evt)
    {
        // 不修改原状态，返回新状态
        var newState = state.Clone();
        
        switch (evt)
        {
            case MoneyDeposited deposited:
                newState.Balance += deposited.Amount;
                newState.TransactionCount++;
                newState.History.Add($"Deposited ${deposited.Amount} - {deposited.Description}");
                break;
                
            case MoneyWithdrawn withdrawn:
                newState.Balance -= withdrawn.Amount;
                newState.TransactionCount++;
                newState.History.Add($"Withdrew ${withdrawn.Amount} - {withdrawn.Description}");
                break;
        }
        
        return newState;
    }
    
    // 公开状态访问（新API）
    public BankAccountState GetState() => State;
    
    // 公开版本访问（新API）
    public long GetCurrentVersion() => CurrentVersion;
}
```

#### Step 3: Local 场景使用 (新API)

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
        var agent = new BankAccountAgent(accountId, logger);
        
        // 3. 设置 EventStore 并激活（新API）
        agent.SetEventStore(eventStore);
        await agent.OnActivateAsync();  // 触发事件重放
        
        // 4. 执行单个操作
        await agent.DepositAsync(100, "Salary");   // Event 1
        await agent.DepositAsync(50, "Bonus");     // Event 2
        await agent.WithdrawAsync(30, "Groceries");// Event 3
        
        Console.WriteLine($"Balance: ${agent.GetState().Balance}"); // $120
        Console.WriteLine($"Version: v{agent.GetCurrentVersion()}"); // v3
        
        // 5. 批量操作（性能优化）
        var transactions = new List<Transaction>
        {
            new() { Type = "deposit", Amount = 200, Description = "Freelance" },
            new() { Type = "deposit", Amount = 150, Description = "Investment" },
            new() { Type = "withdraw", Amount = 100, Description = "Rent" }
        };
        
        await agent.ProcessTransactionsAsync(transactions);  // 一次I/O!
        
        Console.WriteLine($"Balance: ${agent.GetState().Balance}"); // $370
        Console.WriteLine($"Version: v{agent.GetCurrentVersion()}"); // v6
        
        // 6. 模拟崩溃恢复
        Console.WriteLine("\n💥 Simulating crash...\n");
        
        var recoveredAgent = new BankAccountAgent(accountId, logger);
        recoveredAgent.SetEventStore(eventStore);
        await recoveredAgent.OnActivateAsync();  // ← 自动重放 6 个事件
        
        Console.WriteLine($"Recovered Balance: ${recoveredAgent.GetState().Balance}"); // $370 ✅
        Console.WriteLine($"Recovered Version: v{recoveredAgent.GetCurrentVersion()}"); // v6 ✅
    }
}
```

#### Step 4: 快照优化 (自动触发)

```csharp
// GAgentBaseWithEventSourcing 内部逻辑（新API）
protected async Task ConfirmEventsAsync(CancellationToken ct = default)
{
    // 1. 批量持久化事件
    _currentVersion = await _eventStore.AppendEventsAsync(
        Id, _pendingEvents, _currentVersion, ct);
    
    // 2. 批量应用事件
    foreach (var evt in _pendingEvents)
    {
        var message = UnpackEvent(evt);
        var newState = TransitionState(State, message);  // 纯函数
        SetStateInternal(newState);  // 更新状态
    }
    
    _pendingEvents.Clear();
    
    // 3. 自动快照（每100个事件）
    if (_currentVersion % 100 == 0)
    {
        await CreateSnapshotAsync(ct);
    }
}

// 重放时优化（新API）
public override async Task OnActivateAsync(CancellationToken ct = default)
{
    await base.OnActivateAsync(ct);
    
    if (_eventStore == null) return;
    
    // 1. 先加载快照（版本 100）
    var snapshot = await _eventStore.GetLatestSnapshotAsync(Id, ct);
    if (snapshot != null)
    {
        var state = snapshot.StateData.Unpack<BankAccountState>();
        SetStateInternal(state);
        _currentVersion = snapshot.Version;
        Logger.LogInformation("📸 Loaded snapshot at version {Version}", _currentVersion);
    }
    
    // 2. 只重放快照后的事件（101-150）
    var events = await _eventStore.GetEventsAsync(
        Id,
        fromVersion: _currentVersion + 1,  // 增量重放！
        ct: ct);
    
    Logger.LogInformation("⏮️  Replaying {Count} events from version {Version}", 
        events.Count, _currentVersion + 1);
    
    // 3. 应用增量事件（纯函数式）
    foreach (var evt in events)
    {
        var message = UnpackEvent(evt);
        var newState = TransitionState(GetState(), message);
        SetStateInternal(newState);
        _currentVersion = evt.Version;
    }
    
    Logger.LogInformation("✅ State recovered to version {Version}", _currentVersion);
}

// 性能对比:
// 无快照: 重放 150 个事件 (慢)
// 有快照: 重放 50 个事件 (快 3x) ⚡
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

## 💡 最佳实践 (EventSourcing V2)

### 1. 批量事件提交模式 ⚡

```csharp
// ✅ 推荐：批量提交（高性能）
public async Task ProcessOrderAsync(Order order)
{
    // 暂存所有相关事件
    RaiseEvent(new OrderCreated { OrderId = order.Id });
    RaiseEvent(new InventoryReserved { Items = order.Items });
    RaiseEvent(new PaymentProcessed { Amount = order.Total });
    
    // 一次性提交（1次I/O）
    await ConfirmEventsAsync();
}

// ❌ 避免：单个事件提交（低性能）
public async Task ProcessOrderAsync_Slow(Order order)
{
    RaiseEvent(new OrderCreated { OrderId = order.Id });
    await ConfirmEventsAsync();  // I/O 1
    
    RaiseEvent(new InventoryReserved { Items = order.Items });
    await ConfirmEventsAsync();  // I/O 2
    
    RaiseEvent(new PaymentProcessed { Amount = order.Total });
    await ConfirmEventsAsync();  // I/O 3
}
```

### 2. 纯函数式状态转换 🔬

```csharp
// ✅ 推荐：纯函数式（不修改原状态）
protected override OrderState TransitionState(OrderState state, IMessage evt)
{
    var newState = state.Clone();  // 深拷贝
    
    if (evt is OrderCreated created)
    {
        newState.OrderId = created.OrderId;
        newState.Status = OrderStatus.Created;
    }
    
    return newState;  // 返回新状态
}

// ❌ 避免：直接修改（有副作用）
protected override OrderState TransitionState_Bad(OrderState state, IMessage evt)
{
    state.OrderId = ...;  // 直接修改！
    return state;
}

// 纯函数式的优势：
// - 易于测试（不需要mock）
// - 易于理解（无副作用）
// - 线程安全（不共享状态）
// - 重放安全（多次执行结果一致）
```

### 3. 快照策略

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

### 4. 事件版本控制 🔄

```csharp
// 事件演化：使用 Protobuf 版本化
message MoneyDepositedV1 {  // 旧版本
    double amount = 1;
}

message MoneyDepositedV2 {  // 新版本
    double amount = 1;
    string currency = 2;     // 新增字段（向后兼容）
    string description = 3;  // 新增字段（向后兼容）
}

// 重放时处理版本兼容（纯函数式）
protected override BankAccountState TransitionState(BankAccountState state, IMessage evt)
{
    var newState = state.Clone();
    
    switch (evt)
    {
        case MoneyDepositedV1 v1:
            // 兼容旧版本事件
            newState.Balance += v1.Amount;
            newState.History.Add($"Deposited ${v1.Amount} (legacy)");
            break;
            
        case MoneyDepositedV2 v2:
            // 新版本事件
            newState.Balance += v2.Amount;
            newState.History.Add($"Deposited ${v2.Amount} {v2.Currency} - {v2.Description}");
            break;
    }
    
    return newState;
}

// Protobuf版本控制规则：
// ✅ 可以：添加新字段（向后兼容）
// ✅ 可以：删除字段（不要重用字段编号）
// ❌ 不可以：修改字段类型
// ❌ 不可以：修改字段编号
```

### 5. 性能优化 ⚡

```csharp
// ✅ 推荐：批量事件追加（新API）
public async Task ProcessBatchAsync(List<Transaction> transactions)
{
    // 1. 暂存所有事件（内存操作，快）
    foreach (var t in transactions)
    {
        if (t.Type == "deposit")
            RaiseEvent(new MoneyDeposited { Amount = t.Amount, Description = t.Description });
        else
            RaiseEvent(new MoneyWithdrawn { Amount = t.Amount, Description = t.Description });
    }
    
    // 2. 一次性持久化（1次I/O）
    await ConfirmEventsAsync();
    
    // 性能提升：
    // - 100个事件：从100次I/O → 1次I/O (100x faster!)
    // - 减少网络往返
    // - 减少事务开销
}

// 性能对比测试
public async Task PerformanceTest()
{
    var sw = Stopwatch.StartNew();
    
    // 方法1：单个提交
    for (int i = 0; i < 100; i++)
    {
        RaiseEvent(new MoneyDeposited { Amount = 10 });
        await ConfirmEventsAsync();  // 100次I/O
    }
    Console.WriteLine($"单个提交: {sw.ElapsedMilliseconds}ms");  // ~2000ms
    
    sw.Restart();
    
    // 方法2：批量提交
    for (int i = 0; i < 100; i++)
    {
        RaiseEvent(new MoneyDeposited { Amount = 10 });
    }
    await ConfirmEventsAsync();  // 1次I/O
    Console.WriteLine($"批量提交: {sw.ElapsedMilliseconds}ms");  // ~20ms ⚡
}
```

---

## ✅ 总结

### EventSourcing V2 核心优势

| 特性 | V1 (旧版) | V2 (新版) | 提升 |
|-----|----------|----------|------|
| **事件提交** | 单个立即提交 | 批量提交 | **10-100x** ⚡ |
| **状态转换** | 直接修改 | 纯函数式 | 易测试 🔬 |
| **事件重放** | 全量重放 | 快照+增量 | **3-10x** ⚡ |
| **并发控制** | 无 | 乐观并发 | ✅ 安全 |
| **版本兼容** | 无 | Protobuf | ✅ 演化 |
| **跨运行时** | ❌ | ✅ | 统一API |

### 快速开始

```csharp
// 1. 定义Agent
public class MyAgent : GAgentBaseWithEventSourcing<MyState>
{
    public MyAgent(Guid id, ILogger logger) : base(id, logger) { }
    
    // 业务方法
    public async Task DoSomethingAsync()
    {
        RaiseEvent(new SomethingHappened { ... });
        await ConfirmEventsAsync();
    }
    
    // 纯函数式状态转换
    protected override MyState TransitionState(MyState state, IMessage evt)
    {
        var newState = state.Clone();
        // 根据事件更新状态
        return newState;
    }
}

// 2. 创建和使用
var eventStore = new InMemoryEventStore();
var agent = new MyAgent(id, logger);
agent.SetEventStore(eventStore);
await agent.OnActivateAsync();  // 自动重放

// 3. 执行业务操作
await agent.DoSomethingAsync();
```

### 核心API速查

| API | 用途 | 示例 |
|-----|------|------|
| `RaiseEvent(evt)` | 暂存事件 | `RaiseEvent(new OrderCreated { ... });` |
| `ConfirmEventsAsync()` | 批量提交 | `await ConfirmEventsAsync();` |
| `TransitionState(state, evt)` | 状态转换 | `return newState with { Balance += 100 };` |
| `SetEventStore(store)` | 设置存储 | `agent.SetEventStore(eventStore);` |
| `OnActivateAsync()` | 激活重放 | `await agent.OnActivateAsync();` |
| `GetState()` | 获取状态 | `var balance = agent.GetState().Balance;` |
| `GetCurrentVersion()` | 获取版本 | `var version = agent.GetCurrentVersion();` |

### 最佳实践清单

- ✅ 使用批量提交（`RaiseEvent` + `ConfirmEventsAsync`）
- ✅ 使用纯函数式状态转换（`TransitionState`）
- ✅ 使用Protobuf定义所有事件和状态
- ✅ 配置合理的快照间隔（默认100个事件）
- ✅ 设计事件时考虑版本兼容性
- ✅ 使用元数据记录事件上下文
- ✅ 在崩溃恢复时验证状态完整性

### 下一步

1. 📖 查看 `examples/EventSourcingDemo` 完整示例
2. 🔧 集成到你的Agent中
3. 🧪 编写单元测试验证事件重放
4. 🚀 在生产环境使用Orleans EventStore

---

**EventSourcing V2** - 生产级事件溯源，性能与正确性的完美结合 🌌

