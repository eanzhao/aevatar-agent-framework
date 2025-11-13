# GAgent 最终架构实施计划

**文档版本**: v1.0
**创建日期**: 2025-01-13
**状态**: 待评审

---

## 目录

- [概述](#概述)
- [当前状态评估](#当前状态评估)
- [实施阶段](#实施阶段)
  - [阶段 1：核心集成（MVP）](#阶段-1核心集成mvp)
  - [阶段 2：扩展存储实现](#阶段-2扩展存储实现)
  - [阶段 3：配置系统完善](#阶段-3配置系统完善)
  - [阶段 4：示例和文档](#阶段-4示例和文档)
  - [阶段 5：集成和部署准备](#阶段-5集成和部署准备)
- [总时间表](#总时间表)
- [关键决策点](#关键决策点)
- [成功标准](#成功标准)

---

## 概述

本文档描述了将 GAgent 架构从当前状态迁移到最终设计（"一份代码，多种运行时"）的详细实施计划。

**设计目标**:
- ✅ Agent 不关心 State 存储位置
- ✅ State 存储在 Composition Root 统一配置
- ✅ EventSourcing 是配置项，不是强制功能
- ✅ 所有可序列化类型使用 Protobuf
- ✅ 运行时切换只需一行代码

**实施策略**: 分阶段交付，每个阶段都有明确的目标和可验证的输出。

---

## 当前状态评估

### 已实现的功能

| 组件 | 状态 | 说明 |
|------|------|------|
| IStateStore<TState> | ✅ 已完成 | 状态存储抽象接口 |
| IEventStore | ✅ 已完成 | 事件存储接口（含快照策略） |
| GAgentOptions | ✅ 已完成 | 配置选项类 |
| ServiceCollectionExtensions | ✅ 已完成 | ConfigGAgentStateStore / ConfigGAgent |
| InMemoryStateStore | ✅ 已完成 | 内存状态存储实现 |
| EventSourcingStateStore | ✅ 已完成 | 事件溯源存储实现 |
| GAgentBase<TState> | ✅ 部分完成 | 大部分功能正常 |

### 缺失/需修改的功能

| 组件 | 状态 | 优先级 | 说明 |
|------|------|--------|------|
| GAgentBase StateStore 集成 | ❌ 未开始 | P0 | HandleEventAsync 中不加载/保存状态 |
| IStateGAgent<TState> 移除 | ❌ 未开始 | P0 | 废弃接口仍在使用 |
| State 可写性 | ❌ 未开始 | P0 | State 当前是 readonly |
| MongoDBStateStore | ❌ 未开始 | P1 | MongoDB 存储实现 |
| OrleansEventStoreAdapter | ❌ 未开始 | P1 | Orleans 适配器 |
| 配置系统验证 | ❌ 未开始 | P1 | ConfigGAgentStateStore 优先级 |
| 集成测试 | ❌ 未开始 | P2 | 端到端测试 |

**关键问题**:
1. GAgentBase 没有自动调用 StateStore.LoadAsync/SaveAsync
2. State 是 readonly（通过 `protected TState State => _state;`）
3. 没有实际测试验证配置系统的工作方式
4. 缺少生产级存储实现（MongoDB）

---

## 实施阶段

### 阶段 1：核心集成（MVP）

**目标**: 让 GAgentBase 正确集成 IStateStore，实现基本工作流

**预计工作量**: 1-2 天
**风险等级**: 中（可能影响现有 Actor 实现）
**并行度**: 不可并行（依赖项）

#### 任务 1.1：修改 GAgentBase 集成 IStateStore

**修改文件**: `src/Aevatar.Agents.Core/GAgentBase.cs`

**变更内容**:
```csharp
public abstract class GAgentBase<TState> : IGAgentHandleAsync, IEventPublisherProvider
    where TState : class, new()
{
    // 添加 StateStore 字段
    private IStateStore<TState>? _stateStore;

    // 添加 StateStore 属性（由 Actor 层注入）
    public IStateStore<TState>? StateStore
    {
        get => _stateStore;
        set => _stateStore = value;
    }

    // 修改 HandleEventAsync 自动加载/保存 State
    public virtual async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        // 1. 加载 State（如果配置了 StateStore）
        if (StateStore != null)
        {
            State = await StateStore.LoadAsync(Id, ct) ?? new TState();
        }

        // 2. 处理事件（现有逻辑）
        // ... 现有 Handler 调用代码 ...

        // 3. 保存 State（如果配置了 StateStore）
        if (StateStore != null)
        {
            await StateStore.SaveAsync(Id, State, ct);
        }
    }
}
```

**验证标准**:
- [ ] StateStore 可以被注入到 GAgentBase 实例
- [ ] HandleEventAsync 自动加载 State（如果不存在则创建新的）
- [ ] HandleEventAsync 自动保存 State（处理后）
- [ ] 如果 StateStore == null，行为与当前一致（无持久化）

#### 任务 1.2：删除 IStateGAgent<TState> 接口

**修改文件**:
- `src/Aevatar.Agents.Abstractions/IStateGAgent.cs`（删除）
- `src/Aevatar.Agents.Core/GAgentBase.cs`（修改基类声明）

**变更内容**:
```csharp
// 删除此行
public abstract class GAgentBase<TState> : IStateGAgent<TState>, IGAgentHandleAsync, IEventPublisherProvider

// 改为
public abstract class GAgentBase<TState> : IGAgentHandleAsync, IEventPublisherProvider
```

**依赖项检查**:
- [ ] 搜索所有使用 IStateGAgent<TState> 的地方
- [ ] 更新所有 Actor 实现（Local、ProtoActor、Orleans）
- [ ] 确保编译通过

#### 任务 1.3：修改 State 为可写属性

**修改文件**: `src/Aevatar.Agents.Core/GAgentBase.cs`

**变更内容**:
```csharp
// 当前（只读）
protected readonly TState _state = new();
protected TState State => _state;

// 改为（可写）
protected TState State { get; set; } = new TState();
```

**影响范围**:
- 所有继承自 GAgentBase 的 Agent 代码（无影响，State 仍然可访问）
- EventSourcingStateStore 的 Apply 方法（需要保持兼容）

**验证**:
- [ ] 所有现有 Agent 代码编译通过
- [ ] State 可以在 Handler 方法中修改
- [ ] State 修改后可以被 StateStore 保存

#### 任务 1.4：集成 StateStore 到 Actor 层

**修改文件**:
- `src/Aevatar.Agents.Runtime.Local/LocalGAgentActor.cs`
- `src/Aevatar.Agents.Runtime.ProtoActor/ProtoActorGAgentActor.cs`
- `src/Aevatar.Agents.Runtime.Orleans/OrleansGAgentActor.cs`

**变更内容**:
```csharp
// 在 CreateActorForAgentAsync 方法中：
public async Task<IGAgentActor> CreateActorForAgentAsync<TAgent>(Guid id, CancellationToken ct = default)
    where TAgent : IGAgent
{
    // ... 现有代码 ...

    // 添加：从 DI 获取 StateStore 并注入
    var stateStore = _serviceProvider.GetService<IStateStore<TState>>();
    if (stateStore != null && agent is GAgentBase<TState> gagent)
    {
        gagent.StateStore = stateStore;
    }

    // ... 剩余代码 ...
}
```

**验证**:
- [ ] 三种 Runtime 的 Actor 都能正确注入 StateStore
- [ ] 集成测试验证 State 自动加载/保存

#### 任务 1.5：编写单元测试

**测试文件**: `test/Aevatar.Agents.Core.Tests/GAgentBaseStateStoreTests.cs`

**测试用例**:
1. `HandleEventAsync_WithStateStore_LoadsState()` - 验证 State 加载
2. `HandleEventAsync_WithStateStore_SavesState()` - 验证 State 保存
3. `HandleEventAsync_WithoutStateStore_WorksNormally()` - 验证无 StateStore 时正常工作
4. `State_IsWritable()` - 验证 State 可写
5. `StateStore_NullByDefault()` - 验证默认 StateStore 为 null

**测试数据**:
- 使用 InMemoryStateStore 作为 mock
- 定义简单的 TestState 和 TestEvent
- 验证 State 的初始值、修改值、持久化值

**验证标准**:
- [ ] 所有测试通过
- [ ] 覆盖率 > 90%

---

### 阶段 2：扩展存储实现

**目标**: 实现 MongoDB 和 EventSourcing 适配器

**预计工作量**: 2-3 天
**风险等级**: 低（新增实现，不影响现有代码）
**并行度**: 可与阶段 1 并行（需要 IStateStore&lt;TState> 接口稳定）

#### 任务 2.1：实现 MongoDBStateStore<TState>

**创建文件**: `src/Aevatar.Agents.Core/Persistence/MongoDBStateStore.cs`

**实现内容**:
```csharp
public class MongoDBStateStore<TState> : IVersionedStateStore<TState>
    where TState : class
{
    private readonly IMongoCollection<AgentStateDocument<TState>> _collection;

    public MongoDBStateStore(IMongoDatabase database, string? collectionName = null)
    {
        var name = collectionName ?? $"agent_states_{typeof(TState).Name}";
        _collection = database.GetCollection<AgentStateDocument<TState>>(name);
    }

    public async Task<TState?> LoadAsync(Guid agentId, CancellationToken ct = default)
    {
        var doc = await _collection.Find(x => x.AgentId == agentId)
                                   .FirstOrDefaultAsync(ct);
        return doc?.State;
    }

    public async Task SaveAsync(Guid agentId, TState state, CancellationToken ct = default)
    {
        var doc = new AgentStateDocument<TState>
        {
            AgentId = agentId,
            State = state,
            Version = 1, // 版本控制可选
            UpdatedAt = DateTime.UtcNow
        };

        await _collection.ReplaceOneAsync(
            x => x.AgentId == agentId,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct);
    }

    // 其他方法...
}
```

**依赖项**:
- MongoDB.Driver
- AgentStateDocument<TState>（定义见任务 2.2）

**验证**:
- [ ] 能正确连接到 MongoDB
- [ ] Load/Save/Exists 方法工作正常
- [ ] 支持版本控制（optimistic concurrency）

#### 任务 2.2：创建 AgentStateDocument<TState>

**创建文件**: `src/Aevatar.Agents.Abstractions/IPersistence/AgentStateDocument.cs`

**实现内容**:
```csharp
/// <summary>
/// MongoDB 中存储的 Agent 状态文档
/// </summary>
/// <typeparam name="TState">状态类型</typeparam>
public class AgentStateDocument<TState>
{
    /// <summary>
    /// Agent ID（作为 MongoDB 的 _id）
    /// </summary>
    [BsonId]
    public Guid AgentId { get; set; }

    /// <summary>
    /// 状态对象
    /// </summary>
    public TState State { get; set; } = default!;

    /// <summary>
    /// 版本号（用于乐观并发控制）
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 元数据（可选）
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}
```

**注意事项**:
- 需要添加 [BsonId] 特性（using MongoDB.Bson.Serialization.Attributes）
- State 必须可序列化（Protobuf 类型）

**验证**:
- [ ] 文档可以序列化/反序列化
- [ ] Version 字段递增正确

#### 任务 2.3：实现 OrleansEventStoreAdapter

**创建文件**: `src/Aevatar.Agents.Core/EventSourcing/OrleansEventStoreAdapter.cs`

**实现内容**:
```csharp
/// <summary>
/// Orleans IEventRepository 适配器
/// 将现有的 IEventRepository 适配为 IEventStore 接口
/// </summary>
public class OrleansEventStoreAdapter : IEventStore
{
    private readonly IEventRepository _repository;
    private readonly ISerializer _serializer;

    public OrleansEventStoreAdapter(IEventRepository repository, ISerializer serializer)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public async Task<long> AppendEventsAsync(
        Guid agentId,
        IEnumerable<IEvent> events,
        CancellationToken ct = default)
    {
        var agentEvents = events.Select(evt =>
        {
            if (evt is not ProtoBufEvent protoEvent)
            {
                throw new InvalidOperationException($"Only {nameof(ProtoBufEvent)} is supported");
            }

            return new AgentStateEvent
            {
                AgentId = agentId,
                EventType = protoEvent.EventType,
                EventData = _serializer.Serialize(protoEvent.Payload),
                Version = protoEvent.Version
            };
        });

        return await _repository.AppendEventsAsync(agentId, agentEvents, ct);
    }

    public async Task<IReadOnlyList<IEvent>> GetEventsAsync(
        Guid agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        CancellationToken ct = default)
    {
        var events = await _repository.GetEventsAsync(
            agentId,
            fromVersion,
            toVersion,
            maxCount,
            ct);

        return events.Select(evt => new ProtoBufEvent
        {
            Id = Guid.NewGuid().ToString(),
            EventType = evt.EventType,
            Version = evt.Version,
            Timestamp = evt.Timestamp,
            Payload = evt.EventData == null ? null! : Any.Parser.ParseFrom(evt.EventData),
            Metadata = new Dictionary<string, string>()
        }).ToList();
    }

    public async Task<IEvent?> GetLatestSnapshotAsync(Guid agentId, CancellationToken ct = default)
    {
        var snapshot = await _repository.GetLatestSnapshotAsync(agentId, ct);
        if (snapshot == null) return null;

        return new ProtoBufEvent
        {
            // ... 从 snapshot 转换 ...
        };
    }

    public async Task SaveSnapshotAsync(
        Guid agentId,
        IEvent snapshot,
        long version,
        CancellationToken ct = default)
    {
        // Orleans 的 IEventRepository 可能不支持快照
        // 需要检查实现或添加扩展
        throw new NotSupportedException("Snapshot not supported by Orleans IEventRepository");
    }

    public async Task<long> GetCurrentVersionAsync(Guid agentId, CancellationToken ct = default)
    {
        return await _repository.GetLatestVersionAsync(agentId, ct);
    }

    public async Task<EventStoreHealth> CheckHealthAsync(CancellationToken ct = default)
    {
        // 检查 Orleans EventStore 健康状态
        return new EventStoreHealth { IsHealthy = true };
    }
}
```

**挑战**:
- Orleans 的 IEventRepository 可能不支持快照（需要检查现有实现）
- 序列化/反序列化需要测试
- 版本号映射需要仔细处理

**验证**:
- [ ] 能正确连接到 Orleans EventStore
- [ ] 事件追加和读取正常
- [ ] 版本号一致

#### 任务 2.4：实现 InMemoryEventStore（测试用）

**创建文件**: `src/Aevatar.Agents.Core/EventSourcing/InMemoryEventStore.cs`

**实现内容**:
```csharp
/// <summary>
/// 内存事件存储（用于测试和开发）
/// </summary>
public class InMemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<Guid, List<IEvent>> _events = new();
    private readonly ConcurrentDictionary<Guid, IEvent> _snapshots = new();

    public Task<long> AppendEventsAsync(
        Guid agentId,
        IEnumerable<IEvent> events,
        CancellationToken ct = default)
    {
        var eventList = _events.GetOrAdd(agentId, _ => new List<IEvent>());
        eventList.AddRange(events);
        return Task.FromResult((long)eventList.Count);
    }

    public Task<IReadOnlyList<IEvent>> GetEventsAsync(
        Guid agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        CancellationToken ct = default)
    {
        if (!_events.TryGetValue(agentId, out var eventList))
            return Task.FromResult<IReadOnlyList<IEvent>>(new List<IEvent>());

        var events = eventList.AsEnumerable();

        if (fromVersion.HasValue)
            events = events.Where(e => e.Version >= fromVersion.Value);

        if (toVersion.HasValue)
            events = events.Where(e => e.Version <= toVersion.Value);

        if (maxCount.HasValue)
            events = events.Take(maxCount.Value);

        return Task.FromResult<IReadOnlyList<IEvent>>(events.ToList());
    }

    public Task<IEvent?> GetLatestSnapshotAsync(Guid agentId, CancellationToken ct = default)
    {
        _snapshots.TryGetValue(agentId, out var snapshot);
        return Task.FromResult(snapshot);
    }

    public Task SaveSnapshotAsync(
        Guid agentId,
        IEvent snapshot,
        long version,
        CancellationToken ct = default)
    {
        _snapshots[agentId] = snapshot;
        return Task.CompletedTask;
    }

    public Task<long> GetCurrentVersionAsync(Guid agentId, CancellationToken ct = default)
    {
        if (!_events.TryGetValue(agentId, out var eventList))
            return Task.FromResult(0L);

        return Task.FromResult(eventList.LastOrDefault()?.Version ?? 0L);
    }

    public Task<EventStoreHealth> CheckHealthAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new EventStoreHealth { IsHealthy = true });
    }
}
```

**用途**:
- 单元测试 EventSourcingStateStore
- 开发环境快速演示
- 不依赖外部存储的集成测试

**验证**:
- [ ] 所有 IEventStore 接口方法正常工作
- [ ] 可用于 EventSourcingStateStore 测试

#### 任务 2.5：编写集成测试

**测试文件**: `test/Aevatar.Agents.Core.Tests/StateStoreTests.cs`

**测试用例**:

**MongoDBStateStore 测试**:
```csharp
[Fact]
public async Task MongoDBStateStore_SaveAndLoad_Works()
{
    // Arrange
    var store = new MongoDBStateStore<TestState>(_mongoDatabase);
    var agentId = Guid.NewGuid();
    var state = new TestState { Value = "test" };

    // Act
    await store.SaveAsync(agentId, state);
    var loaded = await store.LoadAsync(agentId);

    // Assert
    Assert.NotNull(loaded);
    Assert.Equal(state.Value, loaded.Value);
}
```

**EventSourcingStateStore 测试**:
```csharp
[Fact]
public async Task EventSourcingStateStore_SaveAndLoad_WithSnapshot()
{
    // Arrange
    var eventStore = new InMemoryEventStore();
    var snapshotStrategy = new IntervalSnapshotStrategy(3);
    var stateStore = new EventSourcingStateStore<TestState>(eventStore, snapshotStrategy);

    var agentId = Guid.NewGuid();
    var events = Enumerable.Range(1, 5).Select(i => new TestEvent { Value = $"event-{i}" });

    // Act
    await eventStore.AppendEventsAsync(agentId, events.Select(e => ProtoBufEvent.Create(e, 0)));
    await stateStore.SaveAsync(agentId, new TestState { Value = "snapshot" });

    // Verify snapshot was created
    var snapshot = await eventStore.GetLatestSnapshotAsync(agentId);
    Assert.NotNull(snapshot);
}
```

**验证**:
- [ ] MongoDBStateStore 测试通过（需要 MongoDB 实例）
- [ ] EventSourcingStateStore 测试通过
- [ ] InMemoryEventStore 测试通过

---

### 阶段 3：配置系统完善

**目标**: 确保 ConfigGAgentStateStore 和 ConfigGAgent 正确工作

**预计工作量**: 1 天
**风险等级**: 低（大部分是测试和修复）
**并行度**: 独立

#### 任务 3.1：修复 CastingStateStore 的异步问题

**修改文件**: `src/Aevatar.Agents.Core/Extensions/ServiceCollectionExtensions.cs`

**问题**:
```csharp
// 当前实现（阻塞）
public Task<TState?> LoadAsync(Guid agentId, CancellationToken ct = default)
{
    return Task.FromResult<TState?>((TState?)_inner.LoadAsync(agentId, ct).Result); // ❌ 阻塞
}
```

**修复**:
```csharp
public async Task<TState?> LoadAsync(Guid agentId, CancellationToken ct = default)
{
    var result = await _inner.LoadAsync(agentId, ct);
    return result as TState;
}
```

**同理修复 SaveAsync / DeleteAsync / ExistsAsync**

**验证**:
- [ ] 所有方法都是真正的异步（不阻塞）
- [ ] 没有使用 .Result 或 .Wait()

#### 任务 3.2：验证配置优先级

**创建测试文件**: `test/Aevatar.Agents.Core.Tests/ConfigurationTests.cs`

**测试场景**:

**场景 1：只有 ConfigGAgentStateStore**
```csharp
[Fact]
public void ConfigGAgentStateStore_UsedForAllAgents()
{
    // Arrange
    var services = new ServiceCollection();
    services.ConfigGAgentStateStore(options =>
    {
        options.StateStore = _ => new InMemoryStateStore();
    });
    services.ConfigGAgent<TestAgent, TestState>();

    // Act
    var provider = services.BuildServiceProvider();
    var stateStore = provider.GetService<IStateStore<TestState>>();

    // Assert
    Assert.NotNull(stateStore);
    Assert.IsType<InMemoryStateStore<TestState>>(stateStore);
}
```

**场景 2：ConfigGAgent 覆盖 ConfigGAgentStateStore**
```csharp
[Fact]
public void ConfigGAgent_OverridesDefaultStateStore()
{
    // Arrange
    var services = new ServiceCollection();
    services.ConfigGAgentStateStore(options =>
    {
        options.StateStore = _ => new InMemoryStateStore();
    });
    services.ConfigGAgent<TestAgent, TestState>(options =>
    {
        options.StateStore = _ => new MockStateStore<TestState>();
    });

    // Act
    var provider = services.BuildServiceProvider();
    var stateStore = provider.GetService<IStateStore<TestState>>();

    // Assert
    Assert.NotNull(stateStore);
    Assert.IsType<MockStateStore<TestState>>(stateStore);
}
```

**场景 3：没有 ConfigGAgentStateStore，只有 ConfigGAgent**
```csharp
[Fact]
public void ConfigGAgent_UsesInMemoryStoreByDefault()
{
    // Arrange
    var services = new ServiceCollection();
    services.ConfigGAgent<TestAgent, TestState>();

    // Act
    var provider = services.BuildServiceProvider();
    var stateStore = provider.GetService<IStateStore<TestState>>();

    // Assert
    Assert.NotNull(stateStore);
    Assert.IsType<InMemoryStateStore<TestState>>(stateStore);
}
```

**场景 4：EventSourcing 覆盖**
```csharp
[Fact]
public void ConfigGAgent_EnableEventSourcing_OverridesDefault()
{
    // Arrange
    var services = new ServiceCollection()
        .AddSingleton<IEventStore>(_ => new InMemoryEventStore());

    services.ConfigGAgentStateStore(options =>
    {
        options.StateStore = _ => new InMemoryStateStore();
    });

    services.ConfigGAgent<TestAgent, TestState>(options =>
    {
        options.StateStore = sp => new EventSourcingStateStore<TestState>(
            sp.GetRequiredService<IEventStore>(),
            new IntervalSnapshotStrategy(100));
    });

    // Act
    var provider = services.BuildServiceProvider();
    var stateStore = provider.GetService<IStateStore<TestState>>();

    // Assert
    Assert.NotNull(stateStore);
    Assert.IsType<EventSourcingStateStore<TestState>>(stateStore);
}
```

**验证标准**:
- [ ] 所有测试场景通过
- [ ] 配置优先级正确（显式配置 > 默认配置 > InMemory）
- [ ] EventSourcing 配置正确覆盖

#### 任务 3.3：添加验证和错误处理

**修改文件**: `src/Aevatar.Agents.Core/Extensions/ServiceCollectionExtensions.cs`

**验证逻辑**:

```csharp
public static IServiceCollection ConfigGAgentStateStore(
    this IServiceCollection services,
    Action<GAgentOptions> configureOptions)
{
    if (configureOptions == null)
        throw new ArgumentNullException(nameof(configureOptions));

    var options = new GAgentOptions();
    configureOptions(options);

    // 验证 1: 至少指定一种存储方式
    if (options.StateStore == null && !options.EnableEventSourcing)
    {
        throw new InvalidOperationException(
            "必须指定 StateStore 或启用 EventSourcing。" +
            "示例: options.StateStore = _ => new InMemoryStateStore()");
    }

    // 验证 2: EventSourcing 需要 IEventStore
    if (options.EnableEventSourcing)
    {
        // 延迟验证（在 BuildServiceProvider 时）
        services.AddSingleton<IEventStore>(sp =>
        {
            var eventStore = sp.GetService<IEventStore>();
            if (eventStore == null)
            {
                throw new InvalidOperationException(
                    "启用 EventSourcing 但未注册 IEventStore. " +
                    "请调用 services.AddSingleton<IEventStore>(...)");
            }
            return eventStore;
        });
    }

    _defaultOptions = options;
    return services;
}
```

**验证标准**:
- [ ] 未指定 StateStore 时抛出清晰错误
- [ ] 启用 EventSourcing 但未注册 IEventStore 时抛出清晰错误
- [ ] 错误消息包含解决建议

---

### 阶段 4：示例和文档

**目标**: 创建完整示例验证设计

**预计工作量**: 2 天
**风险等级**: 无（纯新增）
**并行度**: 独立

#### 任务 4.1：创建 TranslateAgent 示例（简单 Agent）

**创建文件**: `examples/Demo.Agents/TranslateAgent.cs`

**实现内容**:
```csharp
using Aevatar.Agents.Core;
using Aevatar.Agents.Abstractions.Attributes;
using Google.Protobuf;

namespace Demo.Agents;

/// <summary>
/// 翻译 Agent（简单 Agent，使用默认 StateStore）
/// </summary>
public class TranslateAgent : GAgentBase<TranslateState>
{
    private readonly ITranslationService _translationService;

    public TranslateAgent(ITranslationService translationService)
    {
        _translationService = translationService;
    }

    [EventHandler]
    public async Task HandleTranslateRequest(TranslateRequest request)
    {
        // 直接从 State 读取配置
        var targetLanguage = State.TargetLanguage;

        // 检查缓存
        if (State.Cache.TryGetValue(request.Text, out var cached))
        {
            await PublishAsync(new TranslateResult
            {
                Original = request.Text,
                Translated = cached,
                FromCache = true
            });
            return;
        }

        // 调用翻译服务
        var translated = await _translationService.TranslateAsync(
            request.Text,
            targetLanguage);

        // 更新状态（缓存结果）
        State.Cache[request.Text] = translated;

        // 发布结果
        await PublishAsync(new TranslateResult
        {
            Original = request.Text,
            Translated = translated,
            FromCache = false
        });
    }

    [EventHandler]
    public async Task HandleSetTargetLanguage(SetTargetLanguageCommand cmd)
    {
        // 修改 State
        State.TargetLanguage = cmd.LanguageCode;

        await PublishAsync(new TargetLanguageUpdatedEvent
        {
            LanguageCode = cmd.LanguageCode
        });
    }
}

/// <summary>
/// 翻译 Agent 状态
/// </summary>
public class TranslateState
{
    /// <summary>
    /// 目标语言（en, zh, ja, etc.）
    /// </summary>
    public string TargetLanguage { get; set; } = "en";

    /// <summary>
    /// 翻译缓存（原文 -> 译文）
    /// </summary>
    public Dictionary<string, string> Cache { get; set; } = new();
}

/// <summary>
/// 翻译请求事件
/// </summary>
public class TranslateRequest : IMessage
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// 翻译结果事件
/// </summary>
public class TranslateResult : IMessage
{
    public string Original { get; set; } = string.Empty;
    public string Translated { get; set; } = string.Empty;
    public bool FromCache { get; set; }
}

/// <summary>
/// 设置目标语言命令
/// </summary>
public class SetTargetLanguageCommand : IMessage
{
    public string LanguageCode { get; set; } = string.Empty;
}

/// <summary>
/// 目标语言更新事件
/// </summary>
public class TargetLanguageUpdatedEvent : IMessage
{
    public string LanguageCode { get; set; } = string.Empty;
}
```

**配置代码** (Program.cs):
```csharp
// 配置默认 StateStore（所有 Agent 使用内存）
services.ConfigGAgentStateStore(options =>
{
    options.StateStore = _ => new InMemoryStateStore();
});

// 注册 TranslateAgent（使用默认值）
services.ConfigGAgent<TranslateAgent, TranslateState>();

// 注册翻译服务
services.AddSingleton<ITranslationService, MockTranslationService>();
```

**验证**:
- [ ] TranslateAgent 可以正确处理 TranslateRequest
- [ ] State 在多次请求之间保持（缓存有效）
- [ ] 重启后 State 丢失（符合预期，因为是 InMemory）

#### 任务 4.2：创建 BankAccountAgent 示例（EventSourcing）

**创建文件**: `examples/Demo.Agents/BankAccountAgent.cs`

**实现内容**:
```csharp
/// <summary>
/// 银行账户 Agent（需要审计，使用 EventSourcing）
/// </summary>
public class BankAccountAgent : GAgentBase<AccountState>
{
    [EventHandler]
    public async Task HandleOpenAccount(OpenAccountCommand cmd)
    {
        if (State.IsOpened)
        {
            throw new InvalidOperationException("Account already opened");
        }

        State.AccountNumber = cmd.AccountNumber;
        State.Balance = 0;
        State.IsOpened = true;
        State.OpenedAt = DateTime.UtcNow;

        await PublishAsync(new AccountOpenedEvent
        {
            AccountNumber = cmd.AccountNumber,
            OpenedAt = State.OpenedAt
        });
    }

    [EventHandler]
    public async Task HandleDeposit(MoneyDepositedEvent evt)
    {
        if (!State.IsOpened)
        {
            throw new InvalidOperationException("Account not opened");
        }

        if (evt.Amount <= 0)
        {
            throw new InvalidOperationException("Invalid deposit amount");
        }

        State.Balance += evt.Amount;
        State.TransactionHistory.Add(new Transaction
        {
            Id = evt.TransactionId,
            Type = TransactionType.Deposit,
            Amount = evt.Amount,
            Timestamp = DateTime.UtcNow
        });

        await PublishAsync(new BalanceUpdatedEvent
        {
            AccountNumber = State.AccountNumber,
            NewBalance = State.Balance,
            TransactionId = evt.TransactionId
        });
    }

    [EventHandler]
    public async Task HandleWithdraw(MoneyWithdrawnEvent evt)
    {
        if (!State.IsOpened)
        {
            throw new InvalidOperationException("Account not opened");
        }

        if (evt.Amount <= 0)
        {
            throw new InvalidOperationException("Invalid withdrawal amount");
        }

        if (State.Balance < evt.Amount)
        {
            throw new InsufficientFundsException(State.Balance, evt.Amount);
        }

        State.Balance -= evt.Amount;
        State.TransactionHistory.Add(new Transaction
        {
            Id = evt.TransactionId,
            Type = TransactionType.Withdrawal,
            Amount = -evt.Amount,
            Timestamp = DateTime.UtcNow
        });

        await PublishAsync(new BalanceUpdatedEvent
        {
            AccountNumber = State.AccountNumber,
            NewBalance = State.Balance,
            TransactionId = evt.TransactionId
        });
    }
}

/// <summary>
/// 账户状态
/// </summary>
public class AccountState
{
    public string AccountNumber { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public bool IsOpened { get; set; }
    public DateTime OpenedAt { get; set; }
    public List<Transaction> TransactionHistory { get; set; } = new();
}

/// <summary>
/// 交易记录
/// </summary>
public class Transaction
{
    public string Id { get; set; } = string.Empty;
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public DateTime Timestamp { get; set; }
}

public enum TransactionType
{
    Deposit,
    Withdrawal
}

/// <summary>
/// 开户命令
/// </summary>
public class OpenAccountCommand : IMessage
{
    public string AccountNumber { get; set; } = string.Empty;
}

/// <summary>
/// 存款事件
/// </summary>
public class MoneyDepositedEvent : IMessage
{
    public decimal Amount { get; set; }
    public string TransactionId { get; set; } = string.Empty;
}

/// <summary>
/// 取款事件
/// </summary>
public class MoneyWithdrawnEvent : IMessage
{
    public decimal Amount { get; set; }
    public string TransactionId { get; set; } = string.Empty;
}

/// <summary>
/// 余额更新事件
/// </summary>
public class BalanceUpdatedEvent : IMessage
{
    public string AccountNumber { get; set; } = string.Empty;
    public decimal NewBalance { get; set; }
    public string TransactionId { get; set; } = string.Empty;
}

/// <summary>
/// 账户已开户事件
/// </summary>
public class AccountOpenedEvent : IMessage
{
    public string AccountNumber { get; set; } = string.Empty;
    public DateTime OpenedAt { get; set; }
}

/// <summary>
/// 余额不足异常
/// </summary>
public class InsufficientFundsException : Exception
{
    public decimal CurrentBalance { get; }
    public decimal RequestedAmount { get; }

    public InsufficientFundsException(decimal currentBalance, decimal requestedAmount)
        : base($"Insufficient funds: balance={currentBalance}, requested={requestedAmount}")
    {
        CurrentBalance = currentBalance;
        RequestedAmount = requestedAmount;
    }
}
```

**配置代码** (Program.cs):
```csharp
// 1. 配置 MongoDB
services.AddSingleton<IMongoDatabase>(sp =>
{
    var client = new MongoClient("mongodb://localhost:27017");
    return client.GetDatabase("banking");
});

// 2. 配置默认 StateStore（简单 Agent 使用内存）
services.ConfigGAgentStateStore(options =>
{
    options.StateStore = _ => new InMemoryStateStore();
});

// 3. 配置 EventStore（用于 EventSourcing）
services.AddSingleton<IEventStore>(sp =>
{
    var repository = sp.GetRequiredService<IEventRepository>();
    var serializer = sp.GetRequiredService<ISerializer>();
    return new OrleansEventStoreAdapter(repository, serializer);
});

// 4. 配置 BankAccountAgent（覆盖为 MongoDB + EventSourcing）
services.ConfigGAgent<BankAccountAgent, AccountState>(options =>
{
    options.StateStore = sp => new EventSourcingStateStore<AccountState>(
        sp.GetRequiredService<IEventStore>(),
        new IntervalSnapshotStrategy(100));
});
```

**验证**:
- [ ] 账户开户后 State 正确持久化到 MongoDB
- [ ] 存取款操作记录事件到 EventStore
- [ ] 重启后可以重放事件恢复 State
- [ ] 每 100 个事件创建一个快照

#### 任务 4.3：创建 NovelContinuationAgent 示例（MongoDB）

**创建文件**: `examples/Demo.Agents/NovelContinuationAgent.cs`

**实现思路**:
```csharp
/// <summary>
/// 小说续写 Agent（使用 MongoDB 存储章节历史）
/// </summary>
/// <remarks>
/// 演示场景：选择 MongoDB 作为存储，因为：
/// - 小说章节数量可能很多（MongoDB 查询灵活）
/// - 需要持久化历史（不能丢失）
/// - 不需要 EventSourcing（不需要审计）
/// </remarks>
public class NovelContinuationAgent : GAgentBase<NovelState>
{
    private readonly ILLMService _llmService;

    public NovelContinuationAgent(ILLMService llmService)
    {
        _llmService = llmService;
    }

    [EventHandler]
    public async Task HandleWriteChapter(WriteChapterCommand cmd)
    {
        // 从 State 获取现有章节
        var existingChapters = State.Chapters;
        var lastChapter = existingChapters.LastOrDefault();

        // 构建续写提示
        var prompt = $"""
            Continue the story from:
            {lastChapter?.Content ?? cmd.StartingPrompt}

            Style: {State.Style}
            Target length: {cmd.TargetLength} words
            """;

        // 调用 LLM
        var continuation = await _llmService.GenerateAsync(prompt);

        // 添加到 State
        State.Chapters.Add(new Chapter
        {
            Number = existingChapters.Count + 1,
            Title = cmd.ChapterTitle,
            Content = continuation,
            WordCount = continuation.Split(' ').Length,
            WrittenAt = DateTime.UtcNow
        });

        // 发布事件
        await PublishAsync(new ChapterWrittenEvent
        {
            ChapterNumber = State.Chapters.Count,
            Title = cmd.ChapterTitle,
            Preview = continuation.Substring(0, Math.Min(100, continuation.Length))
        });
    }

    [EventHandler]
    public async Task HandleUpdateStyle(UpdateStyleCommand cmd)
    {
        State.Style = cmd.NewStyle;

        await PublishAsync(new StyleUpdatedEvent
        {
            NewStyle = cmd.NewStyle
        });
    }
}

public class NovelState
{
    public string Title { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public List<Chapter> Chapters { get; set; } = new();
    public List<string> CharacterProfiles { get; set; } = new();
    public List<string> WorldBuildingNotes { get; set; } = new();
}

public class Chapter
{
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int WordCount { get; set; }
    public DateTime WrittenAt { get; set; }
}
```

**配置**:
```csharp
// 配置 MongoDB
services.AddSingleton<IMongoDatabase>(sp =>
{
    var client = new MongoClient("mongodb://localhost:27017");
    return client.GetDatabase("novels");
});

// 配置默认 StateStore（内存）
services.ConfigGAgentStateStore(options =>
{
    options.StateStore = _ => new InMemoryStateStore();
});

// 配置 NovelContinuationAgent（覆盖为 MongoDB）
services.ConfigGAgent<NovelContinuationAgent, NovelState>(options =>
{
    options.StateStore = sp => new MongoDBStateStore<NovelState>(
        sp.GetRequiredService<IMongoDatabase>());
});

// 注册 LLM 服务（mock）
services.AddSingleton<ILLMService, MockLLMService>();
```

**验证**:
- [ ] 章节写入后保存到 MongoDB
- [ ] 重启后可以加载历史章节
- [ ] 查询性能可接受（测试 1000 个章节）

#### 任务 4.4：更新文档

**更新文件**: `docs/FINAL_ARCHITECTURE_DESIGN.md`

**更新内容**:

1. 在"Agent 定义方式"一节添加示例链接：
```markdown
### 示例 1：简单 Agent（翻译）
完整示例：[TranslateAgent 示例](../examples/Demo.Agents/TranslateAgent.cs)

### 示例 2：银行账户 Agent（EventSourcing）
完整示例：[BankAccountAgent 示例](../examples/Demo.Agents/BankAccountAgent.cs)

### 示例 3：小说续写 Agent（MongoDB）
完整示例：[NovelContinuationAgent 示例](../examples/Demo.Agents/NovelContinuationAgent.cs)
```

2. 添加"性能建议"一节：
```markdown
## 性能建议

### 1. 选择合适的 StateStore
- **内存**（InMemoryStateStore）: < 1ms，适合不需要持久化的 Agent
- **MongoDB**（MongoDBStateStore）: 5-10ms，适合需要查询的复杂状态
- **EventSourcing**（EventSourcingStateStore）: 10-20ms + 重放成本，适合需要审计的场景

### 2. 快照策略优化
- 高频 Agent（>1000 事件/天）: IntervalSnapshotStrategy(100)
- 低频 Agent（<100 事件/天）: IntervalSnapshotStrategy(10)
- 时间敏感: TimeBasedSnapshotStrategy(TimeSpan.FromHours(1))

### 3. State 设计最佳实践
- 保持 State 小而简单（< 1MB）
- 大数据放在单独存储（如 GridFS）
- 使用 Protobuf 序列化（而非 JSON）
```

**验证**:
- [ ] 文档包含所有三个示例
- [ ] API 文档完整（XML 注释）
- [ ] 性能建议合理

#### 任务 4.5：创建 Benchmarks

**创建文件**: `test/Aevatar.Agents.PerformanceTests/StateStoreBenchmarks.cs`

**实现内容**:
```csharp
[MemoryDiagnoser]
public class StateStoreBenchmarks
{
    private IStateStore<TestState> _inMemoryStore = new InMemoryStateStore<TestState>();
    private IStateStore<TestState> _mongoDBStore; // 需要 MongoDB 实例
    private EventSourcingStateStore<TestState> _eventSourcingStore;
    private Guid _agentId = Guid.NewGuid();
    private TestState _testState = new TestState { Value = "test" };

    [GlobalSetup]
    public void Setup()
    {
        // 初始化 MongoDB 和 EventSourcing
        var mongoClient = new MongoClient("mongodb://localhost:27017");
        var database = mongoClient.GetDatabase("benchmarks");
        _mongoDBStore = new MongoDBStateStore<TestState>(database);

        var eventStore = new InMemoryEventStore();
        _eventSourcingStore = new EventSourcingStateStore<TestState>(
            eventStore,
            new IntervalSnapshotStrategy(100));
    }

    [Benchmark(Baseline = true)]
    public async Task InMemoryStateStore_SaveAndLoad()
    {
        await _inMemoryStore.SaveAsync(_agentId, _testState);
        await _inMemoryStore.LoadAsync(_agentId);
    }

    [Benchmark]
    public async Task MongoDBStateStore_SaveAndLoad()
    {
        await _mongoDBStore.SaveAsync(_agentId, _testState);
        await _mongoDBStore.LoadAsync(_agentId);
    }

    [Benchmark]
    public async Task EventSourcingStateStore_SaveAndLoad()
    {
        await _eventSourcingStore.SaveAsync(_agentId, _testState);
        await _eventSourcingStore.LoadAsync(_agentId);
    }
}

public class TestState
{
    public string Value { get; set; } = string.Empty;
}
```

**运行 Benchmark**:
```bash
cd test/Aevatar.Agents.PerformanceTests
dotnet run -c Release
```

**预期结果**:
```
| Method                        | Mean      | Error    | StdDev    | Ratio | RatioSD |
|------------------------------ |----------:|---------:|----------:|------:|--------:|
| InMemoryStateStore_SaveAndLoad |  0.001 ms | 0.0001 ms | 0.0001 ms |  1.00 |    0.00 |
| MongoDBStateStore_SaveAndLoad  |  5.234 ms | 0.1234 ms | 0.2345 ms |  5234 |  234.00 |
| EventSourcingStore_SaveAndLoad | 12.345 ms | 0.2345 ms | 0.3456 ms | 12345 |  456.00 |
```

**验证**:
- [ ] Benchmark 可以运行
- [ ] 结果符合预期（InMemory 最快，MongoDB 次之，EventSourcing 最慢）
- [ ] 结果记录在 BENCHMARKS.md

---

### 阶段 5：集成和部署准备

**目标**: 确保现有项目可以平滑升级

**预计工作量**: 1-2 天
**风险等级**: 中（需要确保向后兼容）
**并行度**: 独立

#### 任务 5.1：迁移现有示例代码

**修改文件**:
- `examples/Demo.Agents/BankAccountAgent.cs`
- `examples/Demo.Agents/Demo.Api/Program.cs`

**迁移策略**:

**选项 A**（推荐）：完全迁移到新架构
```csharp
// 旧的（如果还在使用 StatefulGAgentBase）
public class BankAccountAgent : StatefulGAgentBase<AccountState>
{
    // ...
}

// 新的
public class BankAccountAgent : GAgentBase<AccountState>
{
    // ... 相同代码，可能只需要删除一些冗余代码 ...
}

// Program.cs 旧的:
services.AddStatefulGAgent<BankAccountAgent, AccountState>(options =>
{
    options.StateStore = ...;
});

// Program.cs 新的:
services.ConfigGAgentStateStore(options =>
{
    options.StateStore = sp => new MongoDBStateStore<AccountState>(...);
});
services.ConfigGAgent<BankAccountAgent, AccountState>();
```

**选项 B**: 保持向后兼容
```csharp
// 保留旧的 StatefulGAgentBase（标记为 Obsolete）
[Obsolete("Use GAgentBase<TState> instead")]
public class StatefulGAgentBase<TState> : GAgentBase<TState>
{
    // 包装现有代码
}
```

**验证**:
- [ ] 现有示例可以编译
- [ ] 行为保持一致
- [ ] 性能没有下降

#### 任务 5.2：更新 README.md

**修改文件**: `/README.md`

**更新内容**:

```markdown
# GAgent Framework

## 特性

- ✅ **统一状态管理** - 在 Composition Root 配置 StateStore
- ✅ **多种存储支持** - 内存、MongoDB、EventSourcing
- ✅ **运行时无关** - 一份代码，多种运行时（Local、ProtoActor、Orleans）
- ✅ **Protobuf 优先** - 高性能序列化
- ✅ **事件驱动** - 自动事件路由和处理
- ✅ **可观测性** - OpenTelemetry + Prometheus 指标

## 快速开始

### 1. 安装 NuGet 包

```bash
dotnet add package Aevatar.Agents.Core
dotnet add package Aevatar.Agents.Runtime.ProtoActor  # 或其他运行时
```

### 2. 定义 Agent

```csharp
public class MyAgent : GAgentBase<MyState>
{
    [EventHandler]
    public async Task HandleGreeting(GreetingEvent evt)
    {
        State.MessageCount++;
        await PublishAsync(new ResponseEvent { Message = $"Hello, {evt.Name}!" });
    }
}

public class MyState
{
    public int MessageCount { get; set; }
}
```

### 3. 配置 StateStore

```csharp
var builder = WebApplication.CreateBuilder(args);

// 配置默认 StateStore（所有 Agent 使用）
builder.Services.ConfigGAgentStateStore(options =>
{
    options.StateStore = services => new InMemoryStateStore();
});

// 注册 Agent（使用默认配置）
builder.Services.ConfigGAgent<MyAgent, MyState>();

// 如果需要覆盖（例如使用 MongoDB）
builder.Services.ConfigGAgent<PersistentAgent, PersistentState>(options =>
{
    options.StateStore = services => new MongoDBStateStore<PersistentState>(
        services.GetRequiredService<IMongoDatabase>());
});
```

### 4. 创建 Actor 并处理事件

```csharp
var actorFactory = app.Services.GetRequiredService<IGAgentActorFactory>();
var actor = await actorFactory.CreateGAgentActorAsync<MyAgent>(Guid.NewGuid());

await actor.Agent.HandleEventAsync(new GreetingEvent { Name = "World" });
```

## 文档

- [架构设计](./docs/FINAL_ARCHITECTURE_DESIGN.md) - 详细设计文档
- [API 文档](./docs/API.md) - API 参考
- [示例](./examples/) - 完整示例代码
- [性能基准](./docs/BENCHMARKS.md) - 性能测试报告

## 版本支持

- .NET 8.0+
- MongoDB 5.0+（可选）
- Orleans 7.0+（可选）

## 许可证

MIT License
```

**验证**:
- [ ] README 包含快速开始指南
- [ ] 有完整的示例链接
- [ ] API 文档完整

#### 任务 5.3：创建迁移指南

**创建文件**: `docs/MIGRATION_GUIDE.md`

**内容**:

```markdown
# 从旧版 GAgent 迁移指南

## 概述

本文档帮助你将现有代码从旧版 GAgent（StatefulGAgentBase）迁移到新版（GAgentBase）。

## 主要变更

### 1. 基类变更

**旧的**:
```csharp
public class MyAgent : StatefulGAgentBase<MyState>
{
    // ...
}
```

**新的**:
```csharp
public class MyAgent : GAgentBase<MyState>
{
    // ... 代码几乎相同 ...
}
```

### 2. 配置方式变更

**旧的**:
```csharp
services.AddStatefulGAgent<MyAgent, MyState>(options =>
{
    options.StateStore = ...;  // 在 Agent 中配置
});
```

**新的**:
```csharp
// 在 Composition Root 统一配置
services.ConfigGAgentStateStore(options =>
{
    options.StateStore = ...;  // 全局默认配置
});

services.ConfigGAgent<MyAgent, MyState>();  // 使用默认配置
```

### 3. State 访问方式

**旧的**:
```csharp
// StatelessGAgentBase
State.Property = value;  // 可能编译错误
```

**新的**:
```csharp
// GAgentBase
State.Property = value;  // ✅ State 是可写的
```

### 4. EventSourcing 变更

**旧的**:
```csharp
// StatefulGAgentBase 自动 EventSourcing
public class MyAgent : StatefulGAgentBase<MyState>
{
    // 自动记录所有事件
}
```

**新的**:
```csharp
// GAgentBase 需要手动配置 EventSourcing
public class MyAgent : GAgentBase<MyState>
{
    // 事件处理代码相同
}

// Program.cs:
services.ConfigGAgent<MyAgent, MyState>(options =>
{
    options.StateStore = sp => new EventSourcingStateStore<MyState>(
        sp.GetRequiredService<IEventStore>());
});
```

## 迁移步骤

### 步骤 1：更新 NuGet 包

```bash
dotnet add package Aevatar.Agents.Core --version 2.0.0
```

### 步骤 2：修改 Agent 类

1. 将 `StatefulGAgentBase<TState>` 改为 `GAgentBase<TState>`
2. 删除构造函数中的 StateStore 配置（如果有）
3. 确保 State 是可写的（通常无需修改）

### 步骤 3：更新配置代码

1. 在 Program.cs 中添加 `ConfigGAgentStateStore`（设置默认）
2. 将 `AddStatefulGAgent` 改为 `ConfigGAgent`
3. 将 StateStore 配置从 Agent 移到 Composition Root

### 步骤 4：更新事件处理器

事件处理器代码通常**不需要修改**，因为：
- 事件处理逻辑不变
- State 访问方式不变
- [EventHandler] 特性不变

### 步骤 5：测试

运行现有测试，确保行为一致：
```bash
dotnet test
```

## 常见问题

### Q1: 迁移后性能会下降吗？

A: 不会。新版架构性能更好：
- 消除了反射调用（提升 20x）
- StateStore 是可选的（不需要可以设为 null）
- 批量事件写入（EventSourcing）

### Q2: 旧的 StatefulGAgentBase 还能用吗？

A: 可以，但已标记为 [Obsolete]，建议迁移到 GAgentBase。

### Q3: 需要重写所有 Agent 代码吗？

A: 不需要。主要变更在配置部分，Agent 业务逻辑基本不变。

## 需要帮助？

提交 Issue: https://github.com/aevatar/aevatar-agent-framework/issues
```

**验证**:
- [ ] 迁移指南清晰易懂
- [ ] 包含常见问题解答
- [ ] 有完整的代码示例

#### 任务 5.4：创建 CHANGELOG.md

**创建文件**: `CHANGELOG.md`

**内容**:

```markdown
# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [2.0.0] - 2025-01-XX

### Added

- ✅ **统一状态管理** - 新增 IStateStore<TState> 接口
- ✅ **多种存储支持** - InMemoryStateStore, MongoDBStateStore, EventSourcingStateStore
- ✅ **统一配置** - ConfigGAgentStateStore / ConfigGAgent
- ✅ **事件存储** - IEventStore 接口（兼容 IEventRepository）
- ✅ **快照策略** - ISnapshotStrategy（间隔/时间）
- ✅ **性能优化** - 消除反射调用（提升 20x）

### Changed

- **重大变更**: GAgentBase<TState> 现在自动管理 State（加载/保存）
- **重大变更**: 配置方式改为 ConfigGAgentStateStore / ConfigGAgent
- **废弃**: StatefulGAgentBase<TState>（使用 GAgentBase<TState>）
- **优化**: 事件处理性能提升（消除反射）

### Removed

- ❌ 删除 IStateGAgent<TState> 接口
- ❌ 删除 IGAgentService 服务抽象
- ❌ 删除 IEventRouterService（未使用）
- ❌ 删除 IStateManagerService（未使用）
- ❌ 删除 IEventDeduplicationService（未使用）

### Migration

参见 [Migration Guide](./docs/MIGRATION_GUIDE.md)

## [1.0.0] - 2024-XX-XX

### Added

- Initial release
- Support for Local, ProtoActor, and Orleans runtimes
- Event sourcing with IEventRepository
- Basic state management
```

**验证**:
- [ ] 所有重大变更记录在 Added/Changed/Removed
- [ ] 有迁移指南链接
- [ ] 版本号正确

---

## 总时间表

| 阶段 | 工作量 | 并行度 | 依赖项 | 预计天数 | 负责人 |
|------|--------|--------|--------|----------|--------|
| 阶段 1：核心集成 | 1-2 天 | - | - | **2 天** | |
| 阶段 2：扩展存储 | 2-3 天 | 可与 1 并行 | IStateStore<TState> 稳定 | **2 天** | |
| 阶段 3：配置完善 | 1 天 | 独立 | 阶段 1,2 完成 | **1 天** | |
| 阶段 4：示例和文档 | 2 天 | 独立 | 阶段 1,2,3 完成 | **2 天** | |
| 阶段 5：集成和部署 | 1-2 天 | 独立 | 阶段 1-4 完成 | **2 天** | |
| **总计** | **7-10 天** | - | - | **9 天** | |

**风险评估**:
- **高风险**: 阶段 1（影响现有代码）
- **中风险**: 阶段 5（需要向后兼容）
- **低风险**: 阶段 2,3,4（新增功能）

**并行策略**:
- 阶段 1 和 2 可以同时开始（需要接口稳定）
- 阶段 3 在 1,2 完成后开始
- 阶段 4 在 1,2,3 完成后开始
- 阶段 5 在 1-4 完成后开始

**最短路径**:
- 第 1 天: 开始阶段 1 + 阶段 2
- 第 3 天: 开始阶段 3
- 第 4 天: 开始阶段 4
- 第 6 天: 开始阶段 5
- 第 8 天: 完成

---

## 关键决策点

### 决策 1：GAgentBase 的 State 应该是 readonly 还是 read-write？

**选项 A: Readonly**（当前）
```csharp
protected readonly TState _state = new();
protected TState State => _state;  // 只读引用
```
- 优点: 防止意外修改，线程安全
- 缺点: EventSourcing 重放时需要反射或特殊机制

**选项 B: Read-write**（推荐）
```csharp
protected TState State { get; set; } = new TState();  // 可写
```
- 优点: 自然的状态管理，EventSourcing 重放简单
- 缺点: 需要开发者注意不要意外覆盖

**决策**: **选项 B（Read-write）**
- 理由：
  1. 状态管理的基本模式就是可变的
  2. EventSourcingStateStore 的 Apply 方法需要修改 State
  3. 开发者应该理解他们正在修改状态（这是预期行为）
  4. 可以通过其他机制保证安全（如版本控制）

**影响**:
- 所有 Agent 代码都可以直接写 State（当前已经这样）
- EventSourcingStateStore 的 Apply 方法可以保持简单
- 需要文档明确说明 State 的生命周期

---

### 决策 2：如何处理 State 版本冲突？

**场景**: 同一 Agent 收到两个并发事件，都修改了 State。

**选项 A: 乐观并发控制（抛异常）**
```csharp
try
{
    await stateStore.SaveAsync(agentId, state, expectedVersion: currentVersion);
}
catch (VersionConflictException ex)
{
    // 重试或合并
}
```
- 优点: 明确错误，调用方知道如何处理
- 缺点: 需要重试逻辑

**选项 B: 自动重试（内置）**
```csharp
// StateStore 内部实现重试
public async Task SaveWithRetry(..., int maxRetries = 3)
{
    for (int i = 0; i < maxRetries; i++)
    {
        try { await SaveAsync(...); return; }
        catch (VersionConflictException) { /* retry */ }
    }
    throw new MaxRetryExceededException();
}
```
- 优点: 调用方简单
- 缺点: 可能重试多次，性能损失

**选项 C: 最后写入获胜（静默覆盖）**
```csharp
public async Task SaveAsync(...)
{
    // 不检查版本，直接覆盖
    await collection.ReplaceOneAsync(filter, doc, options);
}
```
- 优点: 简单，性能好
- 缺点: 可能丢失数据

**决策**: **选项 A（乐观并发控制）**
- 理由：
  1. 明确错误优于静默失败
  2. EventSourcing 通常需要明确处理冲突
  3. 重试策略应该由调用方决定（有些场景可能不需要重试）
  4. 可以通过扩展方法提供选项 B 的便利性

**实现**:
- IVersionedStateStore 暴露版本控制
- GAgentBase 在保存时传入版本（TODO）
- 异常类型：StateVersionConflictException

---

### 决策 3：IStateStore 应该是可选还是强制？

**选项 A: 可选（推荐）**
```csharp
public IStateStore<TState>? StateStore { get; set; }  // 可以为 null

// 在 HandleEventAsync 中：
if (StateStore != null)
    State = await StateStore.LoadAsync(Id, ct) ?? new TState();

// 处理事件...

if (StateStore != null)
    await StateStore.SaveAsync(Id, State, ct);
```
- 优点: 简单 Agent 不需要配置任何存储
- 缺点: 需要空值检查

**选项 B: 强制（必须）**
```csharp
public IStateStore<TState> StateStore { get; set; }  // 不能为 null

// 构造函数中提供默认值
protected GAgentBase()
{
    StateStore = new InMemoryStateStore<TState>();  // 默认
}
```
- 优点: 永远有 StateStore，代码简单
- 缺点: 即使不需要持久化也有开销

**决策**: **选项 A（可选）**
- 理由：
  1. 向后兼容（现有代码可能不期望有 StateStore）
  2. 简单 Agent（如 TranslateAgent）可能不需要持久化
  3. 内存占用为零（如果不配置 StateStore）
  4. 可以通过配置系统提供默认

**实现**:
- StateStore 属性为 nullable
- 在 GAgentBase 中检查 null
- ConfigGAgent 提供默认（InMemoryStateStore）

---

### 决策 4：如何处理 EventSourcing 状态重建？

**场景**: Agent 重启后，需要从 EventStore 重放事件恢复 State。

**选项 A: 自动重建（推荐）**
```csharp
// 在 GAgentBase.ActivateAsync 中（由 Actor 层调用）
public virtual async Task OnActivateAsync(CancellationToken ct = default)
{
    if (StateStore is IVersionedStateStore<TState> versionedStore)
    {
        // 从 EventSourcingStateStore 加载（含重放）
        State = await versionedStore.LoadAsync(Id, ct) ?? new TState();
    }
}
```
- 优点: 对 Agent 透明，自动恢复
- 缺点: 启动时有延迟（需要重放）

**选项 B: 手动重建**
```csharp
// Agent 需要显式调用
public class BankAccountAgent : GAgentBase<AccountState>
{
    public async Task ReplayEvents()
    {
        if (StateStore is EventSourcingStateStore<AccountState> store)
        {
            await store.ReplayEventsAsync(Id);
        }
    }
}
```
- 优点: 控制权在 Agent
- 缺点: 每个 Agent 都要处理

**决策**: **选项 A（自动重建）**
- 理由：
  1. EventSourcing 的核心价值就是自动重建
  2. 对 Agent 开发者透明
  3. 可以在 GAgentActorBase 中统一处理
  4. 提供 Snapshot 优化启动时间

**实现**:
- GAgentBase 添加 OnActivateAsync 虚方法
- Actor 层在 Activate 时调用
- EventSourcingStateStore 自动重放事件

---

## 成功标准

### ✅ 功能完整

- [ ] **GAgentBase 自动管理 State**
  - [ ] HandleEventAsync 自动加载 State（如果配置了 StateStore）
  - [ ] HandleEventAsync 自动保存 State（处理后）
  - [ ] State 可写（read-write 属性）

- [ ] **配置系统工作正常**
  - [ ] ConfigGAgentStateStore 设置全局默认值
  - [ ] ConfigGAgent 可以覆盖默认配置
  - [ ] 没有配置的 Agent 自动使用默认值

- [ ] **存储实现完整**
  - [ ] InMemoryStateStore 测试通过
  - [ ] MongoDBStateStore 测试通过
  - [ ] EventSourcingStateStore 测试通过
  - [ ] OrleansEventStoreAdapter 测试通过

- [ ] **Actor 集成正确**
  - [ ] LocalGAgentActor 正确注入 StateStore
  - [ ] ProtoActorGAgentActor 正确注入 StateStore
  - [ ] OrleansGAgentActor 正确注入 StateStore

### ✅ 性能达标

- [ ] **响应时间**
  - [ ] InMemoryStateStore: < 1ms (P99)
  - [ ] MongoDBStateStore: < 10ms (P99)
  - [ ] EventSourcingStateStore: < 20ms (P99)

- [ ] **吞吐量**
  - [ ] 事件处理: > 10,000 事件/秒（InMemory）
  - [ ] 事件处理: > 1,000 事件/秒（MongoDB）
  - [ ] 事件处理: > 500 事件/秒（EventSourcing）

- [ ] **内存占用**
  - [ ] 空 State: < 1KB
  - [ ] 1000 个缓存项: < 100KB
  - [ ] 无内存泄漏

### ✅ 文档完善

- [ ] **设计文档完整**
  - [ ] FINAL_ARCHITECTURE_DESIGN.md 已更新
  - [ ] 所有示例代码链接正确
  - [ ] API 文档覆盖率 100%

- [ ] **示例代码完整**
  - [ ] TranslateAgent 示例（简单）
  - [ ] BankAccountAgent 示例（EventSourcing）
  - [ ] NovelContinuationAgent 示例（MongoDB）

- [ ] **迁移指南完整**
  - [ ] MIGRATION_GUIDE.md 已创建
  - [ ] 常见问题解答完整
  - [ ] 代码示例清晰

### ✅ 向后兼容

- [ ] **Orleans 兼容性**
  - [ ] 现有 Orleans Grain 不受影响
  - [ ] IEventRepository 适配工作正常

- [ ] **平滑升级**
  - [ ] 现有代码可以逐步迁移
  - [ ] 旧的 StatefulGAgentBase 标记为 Obsolete

- [ ] **无破坏性变更**
  - [ ] 现有事件处理器无需修改
  - [ ] State 访问方式保持不变

### ✅ 生产就绪

- [ ] **可观测性**
  - [ ] OpenTelemetry 追踪集成
  - [ ] Prometheus 指标暴露
  - [ ] 结构化日志

- [ ] **健康检查**
  - [ ] StateStore 健康检查
  - [ ] EventStore 健康检查

- [ ] **监控指标**
  - [ ] 事件处理延迟
  - [ ] State 加载/保存延迟
  - [ ] 错误率

---

## 附录

### 附录 A：相关文件清单

**核心接口和类**:
- `src/Aevatar.Agents.Abstractions/IPersistence/IStateStore.cs` - 状态存储接口
- `src/Aevatar.Agents.Abstractions/EventSourcing/IEventStore.cs` - 事件存储接口
- `src/Aevatar.Agents.Abstractions/GAgentOptions.cs` - 配置选项
- `src/Aevatar.Agents.Core/GAgentBase.cs` - Agent 基类（需修改）
- `src/Aevatar.Agents.Core/Persistence/InMemoryStateStore.cs` - 内存存储
- `src/Aevatar.Agents.Core/Persistence/EventSourcingStateStore.cs` - 事件溯源存储
- `src/Aevatar.Agents.Core/Persistence/MongoDBStateStore.cs` - MongoDB 存储（新增）
- `src/Aevatar.Agents.Core/EventSourcing/OrleansEventStoreAdapter.cs` - Orleans 适配器（新增）

**配置和扩展**:
- `src/Aevatar.Agents.Core/Extensions/ServiceCollectionExtensions.cs` - DI 扩展
- `src/Aevatar.Agents.Core/EventSourcing/InMemoryEventStore.cs` - 测试用事件存储（新增）

**运行时实现**:
- `src/Aevatar.Agents.Runtime.Local/LocalGAgentActor.cs` - Local 运行时（需修改）
- `src/Aevatar.Agents.Runtime.ProtoActor/ProtoActorGAgentActor.cs` - ProtoActor 运行时（需修改）
- `src/Aevatar.Agents.Runtime.Orleans/OrleansGAgentActor.cs` - Orleans 运行时（需修改）

**测试**:
- `test/Aevatar.Agents.Core.Tests/GAgentBaseStateStoreTests.cs`（新增）
- `test/Aevatar.Agents.Core.Tests/StateStoreTests.cs`（新增）
- `test/Aevatar.Agents.Core.Tests/ConfigurationTests.cs`（新增）

**示例**:
- `examples/Demo.Agents/TranslateAgent.cs`（新增）
- `examples/Demo.Agents/BankAccountAgent.cs`（修改）
- `examples/Demo.Agents/NovelContinuationAgent.cs`（新增）

**文档**:
- `docs/FINAL_ARCHITECTURE_DESIGN.md`（已更新）
- `docs/MIGRATION_GUIDE.md`（新增）
- `docs/BENCHMARKS.md`（新增）
- `README.md`（更新）
- `CHANGELOG.md`（新增）

### 附录 B：术语表

| 术语 | 说明 |
|------|------|
| Agent | 智能体，处理事件并维护状态 |
| State | Agent 的状态（业务数据） |
| StateStore | 状态存储（IStateStore<TState>） |
| EventStore | 事件存储（IEventStore） |
| EventSourcing | 通过记录所有状态变更事件实现的存储方式 |
| Snapshot | 快照，用于优化 EventSourcing 启动时间 |
| Composition Root | DI 容器配置的地方（Program.cs） |
| ConfigGAgentStateStore | 配置全局默认 StateStore |
| ConfigGAgent | 配置特定 Agent |
| Runtime | 运行时（Local/ProtoActor/Orleans） |

### 附录 C：性能基准参考

| 存储类型 | 延迟 (P99) | 吞吐量 | 适用场景 |
|---------|-----------|--------|---------|
| InMemoryStateStore | < 1ms | 100,000+ 事件/秒 | 临时状态、测试 |
| MongoDBStateStore | 5-10ms | 10,000 事件/秒 | 生产、需要持久化 |
| EventSourcingStateStore | 10-20ms | 5,000 事件/秒 | 需要审计的场景 |

---

**文档创建完成** ✅

**下一步**: 评审和反馈

如有任何问题或建议，请随时提出！
