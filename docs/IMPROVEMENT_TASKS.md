# Aevatar Agent Framework - 改进任务清单

**基于**: `reviews/src-code-review-2025-11-27.md`  
**创建日期**: 2025-11-27  
**状态**: 待执行

---

## 📊 任务总览

| 优先级 | 任务数 | 预计工时 | 说明 |
|--------|--------|----------|------|
| **P0** | 5 | 3-4 天 | 必须立即修复，影响生产可靠性 |
| **P1** | 8 | 2-3 天 | 应尽快修复，影响代码质量 |
| **P2** | 7 | 3-4 天 | 可后续优化，技术债务清理 |
| **P3** | 6 | 2-3 天 | 长期改进，架构演进 |

---

## 🔴 P0 - 必须立即修复

### P0-1: 拆分 MakerCoordinatorGAgent（1926 行）✅ 已完成

**问题位置**: `src/Aevatar.Agents.Maker/Agents/MakerCoordinatorGAgent.cs`

**完成日期**: 2025-11-27

**修复内容**:

使用 partial class 模式将 1926 行的巨型文件拆分为 6 个职责单一的文件：

```
src/Aevatar.Agents.Maker/Agents/
├── MakerCoordinatorGAgent.cs   (555 行) - 核心状态、API、事件处理
├── MakerTaskExecutor.cs        (658 行) - 任务执行逻辑（迭代栈实现）
├── MakerVotingCoordinator.cs   (291 行) - 流式竞赛投票
├── MakerProviderValidator.cs   (324 行) - Provider 发现与验证
├── MakerStateRecovery.cs       (163 行) - 状态持久化 (P0-2 准备)
└── MakerProgressReporter.cs    (62 行)  - 进度报告与 RedFlag
```

**重构效果**:
| 指标 | 重构前 | 重构后 | 改进 |
|------|--------|--------|------|
| 主文件行数 | 1926 | 555 | -71% |
| 最大单文件 | 1926 | 658 | -66% |

**验收标准**:
- [x] 主文件大幅减少（1926→555，-71%）
- [x] 所有单元测试通过 (154/154)
- [x] 编译成功
- [x] 架构文档更新 (docs/COORDINATOR_ARCHITECTURE.md)

---

### P0-2: 持久化 MakerCoordinator 运行时状态

**问题位置**: `src/Aevatar.Agents.Maker/Agents/MakerCoordinatorGAgent.cs` (L38-59)

**当前问题**:
```csharp
// 以下状态在进程重启后丢失
private TaskCompletionSource<bool>? _initCompletionSource;
private TaskCompletionSource<VoteResult>? _consensusCompletionSource;
private CancellationTokenSource? _votingCts;
private VoteEngine? _currentVoteEngine;
private readonly ConcurrentBag<ProposalResult> _collectedProposals = [];
private int _workersInitialized;
private int _expectedWorkers;
// ... 更多运行时状态
```

**改进方案**:

1. **扩展 MakerCoordinatorState protobuf**:
```protobuf
message MakerCoordinatorState {
    // 现有字段...
    
    // 新增：执行恢复所需状态
    string current_voting_request_prefix = 20;
    bool is_solution_voting = 21;
    repeated ProposalResultSnapshot collected_proposals = 22;
    int32 workers_initialized = 23;
    int32 expected_workers = 24;
    int32 active_worker_requests = 25;
    ExecutionPhase current_phase = 26;
}

enum ExecutionPhase {
    IDLE = 0;
    INITIALIZING_WORKERS = 1;
    DECOMPOSITION_VOTING = 2;
    SOLUTION_VOTING = 3;
    COMPOSING = 4;
    COMPLETED = 5;
    FAILED = 6;
}
```

2. **实现状态恢复逻辑**:
```csharp
public override async Task OnActivateAsync(CancellationToken ct)
{
    await base.OnActivateAsync(ct);
    
    // 检查是否有未完成的执行
    if (CustomState.Status == 2) // Running
    {
        await TryRecoverExecutionAsync(ct);
    }
}
```

**验收标准**:
- [ ] 进程重启后可恢复执行
- [ ] 添加恢复相关单元测试

---

### P0-3: 修复 MongoDB Client 复用问题 ✅ 已完成

**问题位置**: `src/Aevatar.Agents.Persistence.MongoDB/MongoDBStateStoreFactory.cs` (L134-144)

**完成日期**: 2025-11-27

**修复内容**:

1. **新增 `MongoDBServiceCollectionExtensions.cs`**:
   - `AddAevatarMongoDB(connectionString, databaseName)` - 注册单例 IMongoClient 和 IMongoDatabase
   - `AddMongoDBStateStore<TState>()` - 注册状态存储
   - `AddMongoDBConfigStore<TConfig>()` - 注册配置存储
   - `AddMongoDBEventRouterStore()` - 注册路由存储

2. **重构三个 Factory 类**:
   - `MongoDBStateStoreFactory.Create<TState>()` - 使用 DI 注入的 IMongoDatabase
   - `MongoDBConfigurationStoreFactory.Create<TConfig>()` - 使用 DI 注入的 IMongoDatabase
   - `MongoDBEventRouterStoreFactory.Create()` - 使用 DI 注入的 IMongoDatabase

3. **向后兼容**:
   - 保留 `CreateWithConnectionString()` 方法但标记为 `[Obsolete]`
   - 旧方法在 factory 创建时缓存 client（而非每次调用创建）

**使用示例**:
```csharp
// 推荐用法
services.AddAevatarMongoDB("mongodb://localhost:27017", "aevatar");
services.AddMongoDBStateStore<MyState>();
services.AddMongoDBConfigStore<MyConfig>();
services.AddMongoDBEventRouterStore();
```

**验收标准**:
- [x] IMongoClient 为单例注入
- [x] 编译通过
- [x] 测试通过（8/8）
- [ ] 压测验证连接池无泄漏（待后续进行）

---

### P0-4: 添加 MongoDB 索引自动创建 ✅ 已完成

**问题位置**: `src/Aevatar.Agents.Persistence.MongoDB/`

**完成日期**: 2025-11-27

**修复内容**:

1. **新增 `MongoDBIndexManager.cs`** - 集中式索引管理器：
   - 使用 `ConcurrentDictionary` 跟踪已初始化的集合
   - 每个集合每进程只创建一次索引（幂等）
   - 处理索引已存在异常 (MongoCommandException code 85/86)
   - 支持同步/异步两种模式

2. **StateStore 索引**:
   - `idx_updated_at` - UpdatedAt 降序索引（TTL 清理）
   - `idx_agent_version` - AgentId + Version 复合索引（乐观并发）

3. **ConfigStore 索引**:
   - `idx_agent_type_id` - AgentType + AgentId 复合唯一索引
   - `idx_updated_at` - UpdatedAt 降序索引

4. **EventRouterStore 索引**:
   - `idx_parent_id` - ParentId 索引（查找子节点）
   - `idx_updated_at` - UpdatedAt 降序索引

**注意**: AgentId 使用 `[BsonId]` 标记，MongoDB 自动为 `_id` 创建唯一索引。

**验收标准**:
- [x] 首次启动自动创建索引
- [x] 索引幂等创建（重复调用无副作用）
- [x] 编译通过
- [x] 测试通过 (8/8)

---

### P0-5: ExecuteTaskRecursiveAsync 栈溢出风险

**问题位置**: `src/Aevatar.Agents.Maker/Agents/MakerCoordinatorGAgent.cs` (L543)

**当前问题**:
```csharp
// 递归调用可能导致深层调用栈
var (result, node) = await ExecuteTaskRecursiveAsync(...);
```

**改进方案**: 改用显式栈实现

```csharp
private async Task<(string? Result, TaskNode Node)> ExecuteTaskIterativeAsync(
    string taskId,
    string description,
    Dictionary<string, string> context,
    CancellationToken ct)
{
    var stack = new Stack<TaskExecutionFrame>();
    var results = new Dictionary<string, (string? Result, TaskNode Node)>();
    
    // 初始任务入栈
    stack.Push(new TaskExecutionFrame
    {
        TaskId = taskId,
        Description = description,
        Context = context,
        Depth = 0,
        Phase = ExecutionPhase.NotStarted
    });
    
    while (stack.Count > 0)
    {
        var frame = stack.Peek();
        
        switch (frame.Phase)
        {
            case ExecutionPhase.NotStarted:
                // 评估原子性...
                break;
            case ExecutionPhase.WaitingForChildren:
                // 检查子任务是否完成...
                break;
            // ... 其他阶段
        }
    }
    
    return results[taskId];
}

private record TaskExecutionFrame
{
    public required string TaskId { get; init; }
    public required string Description { get; init; }
    public required Dictionary<string, string> Context { get; init; }
    public required int Depth { get; init; }
    public ExecutionPhase Phase { get; set; }
    public List<string>? ChildTaskIds { get; set; }
}
```

**验收标准**:
- [ ] 深度 100 级任务无栈溢出
- [ ] 性能与递归版本相当
- [ ] 所有现有测试通过

---

## 🟠 P1 - 应尽快修复

### P1-1: 移除 LocalGAgentActor 反射调用

**问题位置**: `src/Aevatar.Agents.Runtime.Local/LocalGAgentActor.cs` (L135-139)

**当前代码**:
```csharp
var handleMethod = Agent.GetType().GetMethod("HandleEventAsync",
    [typeof(EventEnvelope), typeof(CancellationToken)]);
if (handleMethod != null)
{
    var task = handleMethod.Invoke(Agent, new object[] { envelope, ct }) as Task;
    if (task != null) await task;
}
```

**改进方案**:
```csharp
// 直接调用接口方法
await Agent.HandleEventAsync(envelope, ct);
```

**验收标准**:
- [ ] 移除反射代码
- [ ] 单元测试通过

---

### P1-2: 清理 OrleansGAgentGrain.OnStreamEventReceived

**问题位置**: `src/Aevatar.Agents.Runtime.Orleans/OrleansGAgentGrain.cs` (L184-201)

**当前问题**:
```csharp
private async Task OnStreamEventReceived(byte[] envelopeBytes, StreamSequenceToken? token)
{
    // 只是解析日志，没有实际处理
    var envelope = EventEnvelope.Parser.ParseFrom(envelopeBytes);
    _logger?.LogDebug(...);
    await Task.CompletedTask;  // 空操作
}
```

**改进方案**:

```csharp
/// <summary>
/// Stream 事件接收回调
/// 
/// 设计说明：
/// - Grain 订阅自己的 Stream 是为了实现 Stream → Actor 的桥接
/// - 事件会自动路由到 OrleansGAgentActor (通过 Stream 订阅机制)
/// - 此回调仅用于监控/诊断目的，不需要转发处理
/// </summary>
private Task OnStreamEventReceived(byte[] envelopeBytes, StreamSequenceToken? token)
{
    if (_logger?.IsEnabled(LogLevel.Debug) == true)
    {
        try
        {
            var envelope = EventEnvelope.Parser.ParseFrom(envelopeBytes);
            _logger.LogDebug(
                "[Orleans Grain {GrainId}] Stream received event {EventId}, direction: {Direction}",
                this.GetGrainId(), envelope.Id, envelope.Direction);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse stream event for diagnostics");
        }
    }
    
    // 事件通过 Stream 自动路由到 Actor，此处无需额外处理
    return Task.CompletedTask;
}
```

**验收标准**:
- [ ] 添加清晰注释说明设计意图
- [ ] 移除 `await Task.CompletedTask` 反模式

---

### P1-3: 修复 AIGAgentBase 硬编码默认值

**问题位置**: `src/Aevatar.Agents.AI.Core/AIGAgentBase.cs` (L365-373)

**当前问题**:
```csharp
protected virtual void ConfigAI(AevatarAIAgentConfig config)
{
    config.Model = "gpt-5";  // 不存在的模型名！
    config.Temperature = 0.7f;
    config.MaxOutputTokens = 2000;
}
```

**改进方案**:

```csharp
// AIGAgentDefaults.cs - 新文件
namespace Aevatar.Agents.AI.Core;

/// <summary>
/// AI Agent 默认配置常量
/// </summary>
public static class AIGAgentDefaults
{
    /// <summary>
    /// 默认模型名称
    /// 注意：实际模型由 Provider 配置决定，这里仅作为占位符
    /// </summary>
    public const string DefaultModel = "gpt-4o-mini";
    
    public const float DefaultTemperature = 0.7f;
    public const int DefaultMaxOutputTokens = 2000;
    public const int DefaultMaxHistoryMessages = 50;
}

// AIGAgentBase.cs - 修改后
protected virtual void ConfigAI(AevatarAIAgentConfig config)
{
    config.Model ??= AIGAgentDefaults.DefaultModel;
    config.Temperature = config.Temperature > 0 ? config.Temperature : AIGAgentDefaults.DefaultTemperature;
    config.MaxOutputTokens = config.MaxOutputTokens > 0 ? config.MaxOutputTokens : AIGAgentDefaults.DefaultMaxOutputTokens;
}
```

**验收标准**:
- [ ] 移除硬编码 "gpt-5"
- [ ] 默认值集中管理
- [ ] 文档更新

---

### P1-4: 合并 StateProtectionContext 重复代码

**问题位置**: `src/Aevatar.Agents.Core/StateProtection/StateProtectionContext.cs`

**当前问题**:
```csharp
// EventHandlerScope 和 InitializationScope 实现完全相同
public class EventHandlerScope : IDisposable { ... }
public class InitializationScope : IDisposable { ... }  // 代码重复
```

**改进方案**:

```csharp
internal static class StateProtectionContext
{
    private static readonly AsyncLocal<bool> IsStateOrConfigModifiable = new();

    public static bool IsModifiable => IsStateOrConfigModifiable.Value;

    /// <summary>
    /// 通用状态修改作用域
    /// </summary>
    public sealed class StateModificationScope : IDisposable
    {
        private readonly bool _previousValue;
        private readonly string _scopeType;

        internal StateModificationScope(string scopeType)
        {
            _scopeType = scopeType;
            _previousValue = IsStateOrConfigModifiable.Value;
            IsStateOrConfigModifiable.Value = true;
        }

        public void Dispose()
        {
            IsStateOrConfigModifiable.Value = _previousValue;
        }
    }

    public static StateModificationScope BeginEventHandlerScope() => new("EventHandler");
    public static StateModificationScope BeginInitializationScope() => new("Initialization");

    // 保留类型别名以保持向后兼容
    [Obsolete("Use StateModificationScope directly")]
    public sealed class EventHandlerScope : IDisposable
    {
        private readonly StateModificationScope _inner = new("EventHandler");
        public void Dispose() => _inner.Dispose();
    }

    [Obsolete("Use StateModificationScope directly")]
    public sealed class InitializationScope : IDisposable
    {
        private readonly StateModificationScope _inner = new("Initialization");
        public void Dispose() => _inner.Dispose();
    }
    
    // ... EnsureModifiable 保持不变
}
```

**验收标准**:
- [ ] 消除重复代码
- [ ] 保持向后兼容
- [ ] 所有测试通过

---

### P1-5: 配置化 EventRouter 硬编码阈值

**问题位置**: `src/Aevatar.Agents.Core/EventRouting/EventRouter.cs`

**当前问题**:
```csharp
const int safetyMaxHops = 100;  // L250
MaxHopCount = 50  // L146
```

**改进方案**:

```csharp
// EventRouterOptions.cs - 新文件
namespace Aevatar.Agents.Core.EventRouting;

public class EventRouterOptions
{
    /// <summary>
    /// 默认最大跳数（用于事件创建时的默认值）
    /// </summary>
    public int DefaultMaxHopCount { get; set; } = 50;
    
    /// <summary>
    /// 安全最大跳数（硬限制，防止栈溢出）
    /// </summary>
    public int SafetyMaxHopCount { get; set; } = 100;
    
    /// <summary>
    /// 是否启用循环检测
    /// </summary>
    public bool EnableCycleDetection { get; set; } = true;
}

// EventRouter.cs - 修改后
public class EventRouter
{
    private readonly EventRouterOptions _options;
    
    public EventRouter(
        Guid agentId,
        Func<Guid, EventEnvelope, CancellationToken, Task> sendToActorAsync,
        Func<EventEnvelope, CancellationToken, Task> sendToSelfAsync,
        ILogger? logger = null,
        IEventRouterStore? store = null,
        EventRouterOptions? options = null)
    {
        _options = options ?? new EventRouterOptions();
        // ...
    }
    
    public EventEnvelope CreateEventEnvelope<TEvent>(TEvent evt, EventDirection direction)
        where TEvent : IMessage
    {
        // ...
        MaxHopCount = _options.DefaultMaxHopCount,
        // ...
    }
}
```

**验收标准**:
- [ ] 阈值可通过配置注入
- [ ] 保持默认值向后兼容
- [ ] 文档更新

---

### P1-6: LocalGAgentActor 流订阅管理

**问题位置**: `src/Aevatar.Agents.Runtime.Local/LocalGAgentActor.cs` (L200-214)

**当前问题**:
```csharp
// OnActivateAsync 中订阅但未存储 handle
await _myStream.SubscribeAsync<EventEnvelope>(async envelope => { ... }, null, ct);
// 无法在 Deactivate 时取消订阅
```

**改进方案**:

```csharp
private IMessageStreamSubscription? _selfStreamSubscription;

protected override async Task OnActivateAsync(CancellationToken ct)
{
    if (_myStream != null)
    {
        _selfStreamSubscription = await _myStream.SubscribeAsync<EventEnvelope>(
            async envelope =>
            {
                Logger.LogDebug("[SUBSCRIPTION] Agent {AgentId} received event {EventId}", Id, envelope.Id);
                await HandleEventAsync(envelope, ct);
            },
            null,
            ct);
    }
    // ...
}

protected override async Task OnDeactivateAsync(CancellationToken ct = default)
{
    // 取消自身流订阅
    if (_selfStreamSubscription != null)
    {
        await _selfStreamSubscription.UnsubscribeAsync();
        _selfStreamSubscription = null;
    }
    
    // ... 现有清理逻辑
}
```

**验收标准**:
- [ ] Deactivate 时正确取消订阅
- [ ] 无资源泄漏

---

### P1-7: GAgentBase 反射回退逻辑抽取

**问题位置**: `src/Aevatar.Agents.Core/GAgentBase.cs` (L350-375)

**当前问题**: 反射回退代码冗余

**改进方案**:

```csharp
// 抽取为私有方法
private IMessage? UnpackWithReflectionFallback(Any payload, Type targetType)
{
    try
    {
        var unpackMethod = typeof(Any).Assembly
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public))
            .FirstOrDefault(m => 
                m.Name == "Unpack" && 
                m.IsGenericMethod && 
                m.GetParameters().Length == 1 && 
                m.GetParameters()[0].ParameterType == typeof(Any));
                
        if (unpackMethod == null)
        {
            Logger.LogError("CRITICAL: Could not find Any.Unpack method via reflection");
            return null;
        }
        
        var genericUnpack = unpackMethod.MakeGenericMethod(targetType);
        return (IMessage?)genericUnpack.Invoke(null, new object[] { payload });
    }
    catch (Exception ex)
    {
        Logger.LogDebug(ex, "Reflection fallback unpack failed for type {Type}", targetType.Name);
        return null;
    }
}
```

**验收标准**:
- [ ] 消除重复代码
- [ ] 保持行为一致

---

### P1-8: Orleans ServiceProvider 空检查

**问题位置**: `src/Aevatar.Agents.Runtime.Orleans/OrleansGAgentGrain.cs` (L66, L71)

**当前问题**:
```csharp
_logger = ServiceProvider?.GetService(...) as ILogger<...>;  // 可能为 null
```

**改进方案**:

```csharp
public override async Task OnActivateAsync(CancellationToken cancellationToken)
{
    // 使用 null-forgiving 或 require
    var serviceProvider = ServiceProvider 
        ?? throw new InvalidOperationException("ServiceProvider is not available during Grain activation");
    
    _logger = serviceProvider.GetRequiredService<ILogger<OrleansGAgentGrain>>();
    
    var streamingOptions = serviceProvider.GetService<IOptions<StreamingOptions>>();
    // ...
}
```

**验收标准**:
- [ ] 明确的空值处理
- [ ] 适当的异常信息

---

## 🟡 P2 - 可后续优化 ✅ 全部完成

### P2-1: 拆分 IGAgentManager 接口 ✅ 已完成

**完成日期**: 2025-11-27

**修复内容**:
- 拆分为 4 个职责单一的接口：`IAgentTypeRegistry`, `IAgentTypeRegistrar`, `IAgentMetadataProvider`, `IAgentPluginLoader`
- `IGAgentManager` 保留为聚合接口以向后兼容
- 符合接口隔离原则 (ISP)

---

### P2-2: ResourceContext 类型安全改进 ✅ 已完成

**完成日期**: 2025-11-27

**修复内容**:
- 新增泛型方法：`Get<T>`, `GetRequired<T>`, `Set<T>`, `TryGet<T>`
- 旧 API 标记为 `[Obsolete]` 保持向后兼容
- 新增 `Contains`, `Remove`, `Keys`, `Count`, `Clear` 等便捷方法

---

### P2-3: AevatarToolManager Logger 注入 ✅ 已完成

**完成日期**: 2025-11-27

**修复内容**:
- `AIGAgentWithToolBase.CreateToolManager()` 使用继承的 Logger
- 创建 `LoggerAdapter<T>` 转换 `ILogger` 到 `ILogger<T>`
- 不再使用 `NullLogger`

---

### P2-4: ParseToolArguments 异常处理 ✅ 已完成

**完成日期**: 2025-11-27

**修复内容**:
- 新增 `ToolArgumentParseException` 异常类
- 新增 `ToolArgumentParseResult` 结构体用于结构化错误处理
- `ToolExecutionCoordinator.TryParseToolArguments()` 返回结构化结果
- 支持 `ThrowOnParseError` 配置

---

### P2-5: MassTransit Category 映射文档 ✅ 已完成

**完成日期**: 2025-11-27

**修复内容**:
- 新增 `src/Aevatar.Agents.Plugins.MassTransit/docs/ARCHITECTURE.md`
- 说明 Category → Exchange/Topic 映射规则
- 包含架构图、配置示例、最佳实践

---

### P2-6: ISubscriptionManager 设计优化 ✅ 已完成

**完成日期**: 2025-11-27

**修复内容**:
- 在 `ISubscriptionManager.cs` 头部添加详细设计文档
- 明确 `ISubscriptionManager`, `ISubscriptionHandle`, `IMessageStreamSubscription` 三者关系
- 添加 ASCII 架构图说明层级关系

---

### P2-7: JSON 解析健壮性 (LLMResponseParser) ✅ 已完成

**完成日期**: 2025-11-27

**修复内容**:
- 新增 `src/Aevatar.Agents.AI.Core/Utils/LLMResponseParser.cs`
- 支持 markdown 代码块、嵌套 JSON、部分 JSON 提取
- 提供 `ExtractJson`, `ParseJson<T>`, `TryParseJson<T>`, `ParseToDocument` 方法
- `MakerVotingCoordinator` 已使用新工具类

---

## 🟢 P3 - 长期改进

### P3-1: 代码复杂度 CI 检查

**目标**: 圈复杂度 > 10 告警

**实施方案**:
- 集成 SonarQube 或 NDepend
- 配置 GitHub Actions 检查

---

### P3-2: 端到端集成测试

**目标**: 覆盖核心流程

**测试场景**:
- Agent 层级关系建立与事件传播
- MAKER 完整任务执行
- 跨运行时一致性

---

### P3-3: 性能基准测试

**目标**: 建立性能基线

**测试指标**:
- 事件处理吞吐量
- 状态序列化/反序列化延迟
- Stream 消息延迟

---

### P3-4: MakerCoordinator 状态机重构

**目标**: 使用显式状态机替代隐式状态

```csharp
public enum MakerExecutionState
{
    Idle,
    InitializingWorkers,
    AssessingAtomicity,
    Decomposing,
    SolvingAtomic,
    Voting,
    Composing,
    Completed,
    Failed,
    Cancelled
}
```

---

### P3-5: 健康检查和重连逻辑

**目标**: MassTransit 插件添加健康检查

---

### P3-6: OpenTelemetry 完整集成

**目标**: 分布式追踪全覆盖

---

## 📋 执行计划

### 第一周 (P0)
- [x] P0-1: 拆分 MakerCoordinatorGAgent
- [x] P0-3: 修复 MongoDB Client 复用 ✅ 2025-11-27
- [x] P0-4: 添加 MongoDB 索引 ✅ 2025-11-27

### 第二周 (P0 + P1)
- [x] P0-2: 持久化运行时状态
- [x] P0-5: 迭代式任务执行
- [x] P1-1: 移除反射调用 ✅ 2025-11-27
- [x] P1-3: 修复硬编码默认值 ✅ 2025-11-27

### 第三周 (P1)
- [x] P1-2: 清理 Orleans 回调 ✅ 2025-11-27
- [x] P1-4: 合并重复代码 ✅ 2025-11-27
- [x] P1-5: 配置化阈值 ✅ 2025-11-27
- [x] P1-6: 流订阅管理 ✅ 2025-11-27
- [x] P1-7: 反射回退抽取
- [x] P1-8: ServiceProvider 空检查

### 第四周 (P2) ✅ 已完成
- [x] P2-1: 拆分 IGAgentManager 接口 ✅ 2025-11-27
- [x] P2-2: ResourceContext 类型安全改进 ✅ 2025-11-27
- [x] P2-3: AevatarToolManager Logger 注入 ✅ 2025-11-27
- [x] P2-4: ParseToolArguments 异常处理 ✅ 2025-11-27
- [x] P2-5: MassTransit Category 映射文档 ✅ 2025-11-27
- [x] P2-6: ISubscriptionManager 设计优化 ✅ 2025-11-27
- [x] P2-7: JSON 解析健壮性 (LLMResponseParser) ✅ 2025-11-27

### 持续进行 (P3)
- [ ] P3-1 ~ P3-6: 作为长期技术债务清理

---

## 🔍 验收检查清单

- [ ] 所有单元测试通过
- [ ] 所有集成测试通过
- [ ] 代码覆盖率不下降
- [ ] 无新增 Lint 警告
- [ ] 文档同步更新
- [ ] CHANGELOG 更新

---

*文档版本: 1.0.0*  
*最后更新: 2025-11-27*

