# EventSourcing 最终推荐方案

I'm HyperEcho, 我在**架构决策的关键时刻**

## 🎯 核心问题总结

通过对比原有 `Aevatar.EventSourcing.Core` 实现和当前设计，发现以下关键问题：

### ❌ 当前 IEventStore 的问题

1. **使用 C# class 而非 Protobuf** - 违反框架序列化规范
2. **无快照支持** - 事件增长导致重放性能下降
3. **缺少范围查询** - 无法高效获取事件片段
4. **无乐观并发控制** - 并发写入可能导致冲突
5. **接口过于简单** - 不足以支撑生产级 EventSourcing

### ✅ 原有设计的优势

1. **完整的 EventSourcing 基础设施** (快照 + 事件 + 版本控制)
2. **泛型事件类型** (`TLogEntry`)
3. **自动状态重放** (LogViewAdaptor)
4. **深拷贝保护** (防止状态污染)
5. **Orleans 深度集成** (ILogConsistencyProtocolServices)

---

## 💡 最终推荐方案

### 方案：**增强 IEventStore + 保留原有优势**

```
┌─────────────────────────────────────────────────────────┐
│  统一抽象层 (Protobuf)                                   │
│  ├── AgentStateEvent (Protobuf 事件)                    │
│  ├── AgentSnapshot (Protobuf 快照)                      │
│  └── IEventStore (增强接口)                             │
└─────────────────────────────────────────────────────────┘
                    ↓ implements
┌─────────────────────────────────────────────────────────┐
│  多种实现                                                │
│  ├── InMemoryEventStore (开发/测试，简单)                │
│  ├── OrleansLogConsistencyEventStore (包装原有实现)      │
│  ├── FileSystemEventStore (本地持久化)                  │
│  └── PostgreSQLEventStore (生产级数据库)                 │
└─────────────────────────────────────────────────────────┘
```

---

## 📐 详细设计

### 1. Protobuf 消息定义

```protobuf
// messages.proto

// EventSourcing 事件
message AgentStateEvent {
    string event_id = 1;
    google.protobuf.Timestamp timestamp = 2;
    int64 version = 3;
    string event_type = 4;
    google.protobuf.Any event_data = 5;  // 支持任意事件类型
    string agent_id = 6;
    string correlation_id = 7;
    map<string, string> metadata = 8;
}

// EventSourcing 快照
message AgentSnapshot {
    int64 version = 1;
    google.protobuf.Timestamp timestamp = 2;
    google.protobuf.Any state_data = 3;  // 支持任意状态类型
    map<string, string> metadata = 4;
}
```

### 2. 增强的 IEventStore 接口

```csharp
public interface IEventStore
{
    // ========== 事件操作 ==========
    
    /// <summary>
    /// 追加事件（批量 + 乐观并发）
    /// 参考: ILogConsistentStorage.AppendAsync
    /// </summary>
    Task<long> AppendEventsAsync(
        Guid agentId,
        IEnumerable<AgentStateEvent> events,
        long expectedVersion,  // ← 乐观并发控制
        CancellationToken ct = default);
    
    /// <summary>
    /// 获取事件（范围查询 + 分页）
    /// 参考: ILogConsistentStorage.ReadAsync
    /// </summary>
    Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        Guid agentId,
        long? fromVersion = null,  // ← 范围查询
        long? toVersion = null,
        int? maxCount = null,      // ← 分页支持
        CancellationToken ct = default);
    
    Task<long> GetLatestVersionAsync(
        Guid agentId,
        CancellationToken ct = default);
    
    // ========== 快照操作 (参考原有设计) ==========
    
    /// <summary>
    /// 保存快照（性能优化）
    /// 参考: LogViewAdaptor.WriteAsync (快照部分)
    /// </summary>
    Task SaveSnapshotAsync(
        Guid agentId,
        AgentSnapshot snapshot,
        CancellationToken ct = default);
    
    /// <summary>
    /// 获取最新快照
    /// 参考: LogViewAdaptor.ReadAsync (快照加载)
    /// </summary>
    Task<AgentSnapshot?> GetLatestSnapshotAsync(
        Guid agentId,
        CancellationToken ct = default);
}
```

### 3. InMemoryEventStore 实现 (简化版)

```csharp
public class InMemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<Guid, List<AgentStateEvent>> _events = new();
    private readonly ConcurrentDictionary<Guid, AgentSnapshot> _snapshots = new();
    private readonly object _lock = new();
    
    public Task<long> AppendEventsAsync(
        Guid agentId,
        IEnumerable<AgentStateEvent> events,
        long expectedVersion,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            var eventList = _events.GetOrAdd(agentId, _ => new List<AgentStateEvent>());
            
            // 乐观并发检查
            var currentVersion = eventList.Any() ? eventList.Max(e => e.Version) : 0;
            if (currentVersion != expectedVersion)
            {
                throw new ConcurrencyException(
                    $"Version conflict: expected {expectedVersion}, got {currentVersion}");
            }
            
            // 追加事件
            var newVersion = currentVersion;
            foreach (var evt in events)
            {
                evt.Version = ++newVersion;
                eventList.Add(evt);
            }
            
            return Task.FromResult(newVersion);
        }
    }
    
    public Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        Guid agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        CancellationToken ct = default)
    {
        if (!_events.TryGetValue(agentId, out var eventList))
        {
            return Task.FromResult<IReadOnlyList<AgentStateEvent>>(Array.Empty<AgentStateEvent>());
        }
        
        var query = eventList.AsEnumerable();
        
        if (fromVersion.HasValue)
            query = query.Where(e => e.Version >= fromVersion.Value);
        
        if (toVersion.HasValue)
            query = query.Where(e => e.Version <= toVersion.Value);
        
        if (maxCount.HasValue)
            query = query.Take(maxCount.Value);
        
        return Task.FromResult<IReadOnlyList<AgentStateEvent>>(query.ToList());
    }
    
    public Task SaveSnapshotAsync(Guid agentId, AgentSnapshot snapshot, CancellationToken ct = default)
    {
        _snapshots[agentId] = snapshot;
        return Task.CompletedTask;
    }
    
    public Task<AgentSnapshot?> GetLatestSnapshotAsync(Guid agentId, CancellationToken ct = default)
    {
        _snapshots.TryGetValue(agentId, out var snapshot);
        return Task.FromResult(snapshot);
    }
    
    public Task<long> GetLatestVersionAsync(Guid agentId, CancellationToken ct = default)
    {
        if (!_events.TryGetValue(agentId, out var eventList) || !eventList.Any())
        {
            return Task.FromResult(0L);
        }
        
        return Task.FromResult(eventList.Max(e => e.Version));
    }
}
```

### 4. GAgentBaseWithEventSourcing 优化

```csharp
public abstract class GAgentBaseWithEventSourcing<TState> : GAgentBase<TState>
    where TState : class, IMessage, new()  // ← TState 也必须是 Protobuf
{
    private readonly IEventStore? _eventStore;
    private long _currentVersion = 0;
    private const int SnapshotInterval = 100;  // 每 100 个事件做快照
    
    protected GAgentBaseWithEventSourcing(
        Guid id,
        IEventStore? eventStore = null,
        ILogger? logger = null)
        : base(id, logger)
    {
        _eventStore = eventStore;
    }
    
    /// <summary>
    /// 触发状态变更事件 (Protobuf)
    /// </summary>
    protected async Task RaiseStateChangeEventAsync<TEvent>(
        TEvent evt,
        CancellationToken ct = default)
        where TEvent : class, IMessage
    {
        if (_eventStore == null)
        {
            Logger.LogWarning("EventStore not configured");
            return;
        }
        
        // 创建 AgentStateEvent (Protobuf)
        var stateEvent = new AgentStateEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            EventType = evt.Descriptor.FullName,
            EventData = Any.Pack(evt),  // ← 使用 Any.Pack
            AgentId = Id.ToString(),
            Version = _currentVersion + 1
        };
        
        // 持久化事件（乐观并发）
        _currentVersion = await _eventStore.AppendEventsAsync(
            Id,
            new[] { stateEvent },
            _currentVersion,  // ← 期望版本
            ct);
        
        // 应用事件到状态
        await ApplyStateChangeEventAsync(evt, ct);
        
        // 检查是否需要快照
        if (_currentVersion % SnapshotInterval == 0)
        {
            await CreateSnapshotInternalAsync(ct);
        }
    }
    
    /// <summary>
    /// 从事件存储重放状态（快照优化）
    /// </summary>
    public async Task ReplayEventsAsync(CancellationToken ct = default)
    {
        if (_eventStore == null)
        {
            Logger.LogWarning("EventStore not configured");
            return;
        }
        
        Logger.LogInformation("Replaying events for Agent {AgentId}", Id);
        
        // 1. 先尝试加载最新快照
        var snapshot = await _eventStore.GetLatestSnapshotAsync(Id, ct);
        if (snapshot != null)
        {
            Logger.LogInformation("Loading snapshot at version {Version}", snapshot.Version);
            
            // 从快照恢复状态
            if (snapshot.StateData.Is(TState.Descriptor))
            {
                State = snapshot.StateData.Unpack<TState>();
                _currentVersion = snapshot.Version;
            }
        }
        
        // 2. 然后只重放快照之后的事件
        var events = await _eventStore.GetEventsAsync(
            Id,
            fromVersion: _currentVersion + 1,  // ← 只重放增量
            ct: ct);
        
        if (!events.Any())
        {
            Logger.LogInformation("No new events to replay");
            return;
        }
        
        // 3. 应用事件
        foreach (var stateEvent in events.OrderBy(e => e.Version))
        {
            try
            {
                var descriptor = Google.Protobuf.Reflection.TypeRegistry.Empty
                    .Find(stateEvent.EventType.Replace(".", "/"));
                
                if (descriptor != null && stateEvent.EventData.Is(descriptor))
                {
                    var evt = stateEvent.EventData.Unpack(descriptor.ClrType) as IMessage;
                    if (evt != null)
                    {
                        await ApplyStateChangeEventAsync(evt, ct);
                        _currentVersion = stateEvent.Version;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error replaying event {EventId}", stateEvent.EventId);
            }
        }
        
        Logger.LogInformation(
            "Replayed {Count} events, current version: {Version}",
            events.Count,
            _currentVersion);
    }
    
    /// <summary>
    /// 创建快照（内部）
    /// </summary>
    private async Task CreateSnapshotInternalAsync(CancellationToken ct)
    {
        if (_eventStore == null) return;
        
        var snapshot = new AgentSnapshot
        {
            Version = _currentVersion,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            StateData = Any.Pack(State)  // ← State 必须是 Protobuf
        };
        
        await _eventStore.SaveSnapshotAsync(Id, snapshot, ct);
        
        Logger.LogInformation(
            "Snapshot created for Agent {AgentId} at version {Version}",
            Id,
            _currentVersion);
    }
    
    /// <summary>
    /// 应用状态变更事件（由子类实现）
    /// </summary>
    protected abstract Task ApplyStateChangeEventAsync<TEvent>(
        TEvent evt,
        CancellationToken ct = default)
        where TEvent : class, IMessage;
}
```

### 5. Orleans LogConsistency 包装 (可选优化)

```csharp
/// <summary>
/// 包装原有的 ILogConsistentStorage，实现新的 IEventStore
/// 保留原有实现的所有优势
/// </summary>
public class OrleansLogConsistencyEventStore : IEventStore
{
    private readonly ILogConsistentStorage _storage;
    private readonly string _grainTypeName;
    
    public OrleansLogConsistencyEventStore(
        ILogConsistentStorage storage,
        string grainTypeName = "AgentGrain")
    {
        _storage = storage;
        _grainTypeName = grainTypeName;
    }
    
    public async Task<long> AppendEventsAsync(
        Guid agentId,
        IEnumerable<AgentStateEvent> events,
        long expectedVersion,
        CancellationToken ct = default)
    {
        var grainId = GrainId.Create("agent", agentId.ToString());
        
        // 转换为 LogEntry
        var logEntries = events.Select(e => new LogEntry
        {
            Data = JsonSerializer.Serialize(e)  // 或者用 Protobuf
        }).ToList();
        
        // 调用原有的 AppendAsync
        return await _storage.AppendAsync(
            _grainTypeName,
            grainId,
            logEntries,
            (int)expectedVersion);
    }
    
    // ... 其他方法类似包装
}
```

---

## 📊 方案对比

| 特性 | 当前设计 | 推荐方案 | 原有设计 |
|-----|---------|---------|---------|
| **事件类型** | ❌ C# class | ✅ Protobuf | ⚠️ 泛型 (JSON) |
| **快照** | ❌ 无 | ✅ 支持 | ✅ 支持 |
| **范围查询** | ❌ 无 | ✅ 支持 | ✅ 支持 |
| **乐观并发** | ❌ 无 | ✅ 支持 | ✅ 支持 |
| **分页** | ❌ 无 | ✅ 支持 | ✅ 支持 |
| **跨运行时** | ✅ 统一 | ✅ 统一 | ❌ Orleans only |
| **Orleans 优化** | ❌ 无 | ✅ 可选包装 | ✅ 原生 |
| **复杂度** | ⭐ 简单 | ⭐⭐ 中等 | ⭐⭐⭐ 复杂 |

---

## ✅ 实施建议

### Phase 1: 核心重构 (必须)

1. ✅ 定义 `AgentStateEvent` 和 `AgentSnapshot` (Protobuf)
2. ✅ 重构 `IEventStore` 接口（增加快照、范围查询、乐观并发）
3. ✅ 实现 `InMemoryEventStore` (快照 + 范围查询)
4. ✅ 更新 `GAgentBaseWithEventSourcing` (快照优化)
5. ✅ 测试三运行时统一使用

### Phase 2: Orleans 优化 (可选)

6. ⚠️ 创建 `OrleansLogConsistencyEventStore` 包装
7. ⚠️ 保留原有 LogViewAdaptor 优势
8. ⚠️ 提供工厂选择不同实现

### Phase 3: 生产级存储 (未来)

9. 📝 PostgreSQL/MongoDB EventStore
10. 📝 分布式快照存储
11. 📝 事件流式处理

---

## 🎯 结论

**推荐方案融合了原有设计的优势和当前设计的灵活性**：

✅ **Protobuf 序列化** - 符合框架规范  
✅ **快照优化** - 解决重放性能问题  
✅ **范围查询** - 高效事件获取  
✅ **乐观并发** - 并发安全  
✅ **运行时无关** - 统一接口  
✅ **Orleans 优化** - 可选包装原有实现  
✅ **渐进式** - Phase 1 简单，Phase 2 优化

---

*好的架构是站在巨人的肩膀上，而不是推倒重来* 🌌

