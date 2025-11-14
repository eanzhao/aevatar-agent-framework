# Aevatar Agent Framework - 核心概念

## 🌌 概述

本文档涵盖Aevatar Agent Framework的三个核心概念：**序列化规则**、**Stream架构**和**事件传播机制**。这些是理解和使用框架的基础。

---

## 🔴 第一原则：Protocol Buffers 序列化

### 强制规则

> **所有需要跨运行时边界传输的类型必须使用 Protocol Buffers 定义！**

这是框架的**非协商规则**。违反此规则将导致运行时序列化失败。

### 必须使用 Protobuf 的类型

#### 1. Agent State（`TState`）

```protobuf
// ✅ 正确
message MyAgentState {
    string id = 1;
    int32 count = 2;
    google.protobuf.Timestamp last_update = 3;
    repeated string items = 4;
}
```

```csharp
// ❌ 错误 - 永远不要手动定义State类
public class MyAgentState  // 运行时会失败！
{
    public string Id { get; set; }
    public int Count { get; set; }
}
```

#### 2. Event Messages

```protobuf
// ✅ 正确
message TaskAssignedEvent {
    string task_id = 1;
    string assigned_to = 2;
    string description = 3;
    google.protobuf.Timestamp assigned_at = 4;
}
```

#### 3. Event Sourcing Events

```protobuf
// ✅ 正确
message AccountCreditedEvent {
    string account_id = 1;
    double amount = 2;
    string transaction_id = 3;
}
```

### 为什么这么重要？

1. **Orleans Streaming**: 使用 `byte[]` 传输消息
2. **跨运行时兼容**: Local、Orleans、ProtoActor都能理解Protobuf
3. **版本兼容性**: Protobuf提供前向/后向兼容
4. **性能**: 高效的二进制序列化
5. **类型安全**: 编译时检查

### 常见类型映射

| C# 类型 | Protobuf 类型 | 注意事项 |
|---------|--------------|---------|
| `string` | `string` | ✅ 直接映射 |
| `int` | `int32` | ✅ 直接映射 |
| `long` | `int64` | ✅ 直接映射 |
| `double` | `double` | ✅ 直接映射 |
| `decimal` | `double` | ⚠️ 使用double，注意精度 |
| `DateTime` | `google.protobuf.Timestamp` | ⚠️ 必须用Timestamp |
| `List<T>` | `repeated T` | ✅ 直接映射 |
| `Dictionary<K,V>` | `map<K,V>` | ✅ 直接映射 |

### 项目配置

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Google.Protobuf" />
    <PackageReference Include="Grpc.Tools">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <Protobuf Include="my_messages.proto" GrpcServices="None" />
  </ItemGroup>
</Project>
```

---

## 🌊 Stream 架构

### 核心设计理念

每个 `GAgentActor` 维护一个 **Stream**，作为事件广播频道：

```
Parent Stream
    ├── Child 1 (subscribed) ← 接收广播
    ├── Child 2 (subscribed) ← 接收广播  
    └── Child 3 (subscribed) ← 接收广播
```

### Stream 接口

```csharp
public interface IMessageStream
{
    // 订阅特定类型的消息
    Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler, 
        CancellationToken ct = default) 
        where T : IMessage;
    
    // 带过滤器的订阅
    Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler,
        Func<T, bool> filter,
        CancellationToken ct = default) 
        where T : IMessage;
    
    // 发布消息到stream
    Task PublishAsync<T>(T message, CancellationToken ct = default) 
        where T : IMessage;
}
```

### 订阅生命周期

```csharp
public interface IMessageStreamSubscription : IAsyncDisposable
{
    Guid SubscriptionId { get; }
    Guid StreamId { get; }
    bool IsActive { get; }
    
    Task UnsubscribeAsync();  // 取消订阅
    Task ResumeAsync();       // 恢复订阅（用于重连）
}
```

### 三种Runtime的Stream实现

| Runtime | Stream实现 | 底层机制 |
|---------|-----------|---------|
| Local | `LocalMessageStream` | System.Threading.Channels |
| Orleans | `OrleansMessageStream` | Orleans Streaming |
| ProtoActor | `ProtoActorMessageStream` | Proto.Actor EventStream |

---

## 🔄 事件传播方向

### EventDirection 枚举

```csharp
public enum EventDirection
{
    Up,    // 向上传播（发给父节点的stream）
    Down,  // 向下传播（发给自己的stream）
    Both   // 双向传播
}
```

### UP - 向上传播

**使用场景**: 子节点向父节点报告

```csharp
// 子节点代码
await PublishAsync(new TaskCompletedEvent { TaskId = "123" }, EventDirection.Up);
```

**流程**:
```
Child Agent
    ↓ publish to Parent Stream
Parent Stream
    ↓ broadcast to all subscribers
All Siblings (including self)
```

**效果**: 所有兄弟节点（包括自己）都能收到

### DOWN - 向下传播

**使用场景**: 父节点向子节点下发命令

```csharp
// 父节点代码
await PublishAsync(new TaskAssignedEvent { TaskId = "456", AssignedTo = "child1" }, EventDirection.Down);
```

**流程**:
```
Parent Agent
    ↓ publish to Own Stream
Own Stream
    ↓ broadcast to all subscribers
All Children
```

### BOTH - 双向传播

**使用场景**: 全局广播

```csharp
await PublishAsync(new SystemAnnouncementEvent { Message = "Maintenance" }, EventDirection.Both);
```

**流程**:
```
Agent
    ├→ Parent Stream → All Siblings
    └→ Own Stream → All Children
```

### 父子关系建立

```csharp
// 子节点侧：设置父节点并自动订阅
await childActor.SetParentAsync(parentId);

// 父节点侧：添加子节点引用
await parentActor.AddChildAsync(childId);
```

**订阅机制**:
- `SetParentAsync()` 自动创建对父Stream的订阅
- 支持类型过滤（使用 `GAgentBase<TState, TEvent>` 时）
- 自动清理（`ClearParentAsync()`时取消订阅）

---

## 🎯 事件处理器

### 定义事件处理器

#### 1. 特定事件处理器

```csharp
[EventHandler]
public async Task HandleTaskAssigned(TaskAssignedEvent evt)
{
    State.AssignedTasks.Add(evt.TaskId);
    Logger.LogInformation("Received task: {TaskId}", evt.TaskId);
    await Task.CompletedTask;
}
```

#### 2. 全事件处理器

```csharp
[AllEventHandler]
public async Task HandleAnyEvent(EventEnvelope envelope)
{
    // 处理任何类型的事件
    Logger.LogInformation("Event {EventId} received", envelope.Id);
    await Task.CompletedTask;
}
```

#### 3. 约定处理器（无需属性）

```csharp
public async Task HandleAsync(MyEvent evt)
{
    // 方法名为 HandleAsync 或 HandleEventAsync 时自动发现
    await ProcessEvent(evt);
}
```

### 处理器规则

1. **方法签名**: 必须返回 `Task`，接受单个参数
2. **优先级**: 通过 `[EventHandler(Priority = 1)]` 设置
3. **自事件**: 默认不处理自己发布的事件，使用 `HandleSelfEvents = true` 覆盖
4. **类型过滤**: 使用 `GAgentBase<TState, TEvent>` 在类型层面过滤

---

## 📊 类型过滤机制

### 基础Agent（无过滤）

```csharp
public class MyAgent : GAgentBase<MyState>
{
    // 接收所有类型的事件
    [EventHandler]
    public async Task HandleAnyEvent(IMessage evt) { }
}
```

### 类型过滤Agent

```csharp
public class MyAgent : GAgentBase<MyState, TeamEvent>
{
    // 只接收 TeamEvent 及其子类型
    // 其他事件在订阅时就被过滤，不会反序列化
    [EventHandler]
    public async Task HandleTeamEvent(TeamEvent evt) { }
}
```

**好处**:
- 减少不必要的反序列化开销
- 类型安全
- 性能优化

---

## 🔧 实战示例

### 示例1：层次化团队协作

```csharp
// 定义Events
message TaskAssignedEvent { string task_id = 1; string assigned_to = 2; }
message TaskCompletedEvent { string task_id = 1; string completed_by = 2; }

// 父Agent - 团队领导
public class TeamLeaderAgent : GAgentBase<TeamLeaderState>
{
    // 分配任务给子节点
    public async Task AssignTask(string taskId, string memberId)
    {
        var evt = new TaskAssignedEvent { TaskId = taskId, AssignedTo = memberId };
        await PublishAsync(evt, EventDirection.Down);  // 向下广播
    }

    // 接收子节点的完成报告
    [EventHandler]
    public async Task HandleTaskCompleted(TaskCompletedEvent evt)
    {
        State.CompletedTasks.Add(evt.TaskId);
        Logger.LogInformation("Task {TaskId} completed by {Member}",
            evt.TaskId, evt.CompletedBy);
    }
}

// 子Agent - 团队成员
public class TeamMemberAgent : GAgentBase<TeamMemberState>
{
    // 接收任务分配
    [EventHandler]
    public async Task HandleTaskAssigned(TaskAssignedEvent evt)
    {
        if (evt.AssignedTo == State.MemberId)
        {
            State.CurrentTask = evt.TaskId;
            // 模拟完成任务
            await Task.Delay(1000);
            await CompleteTask(evt.TaskId);
        }
    }

    // 完成任务并报告
    private async Task CompleteTask(string taskId)
    {
        var evt = new TaskCompletedEvent {
            TaskId = taskId,
            CompletedBy = State.MemberId
        };
        await PublishAsync(evt, EventDirection.Up);  // 向上报告
    }
}

// 使用
var leader = await manager.CreateAndRegisterAsync<TeamLeaderAgent>(leaderId);
var member1 = await manager.CreateAndRegisterAsync<TeamMemberAgent>(member1Id);
var member2 = await manager.CreateAndRegisterAsync<TeamMemberAgent>(member2Id);

// 建立关系
await member1.SetParentAsync(leaderId);  // member1自动订阅leader的stream
await member2.SetParentAsync(leaderId);  // member2自动订阅leader的stream
await leader.AddChildAsync(member1Id);   // leader添加child引用
await leader.AddChildAsync(member2Id);

// 分配任务
await ((TeamLeaderAgent)leader.GetAgent()).AssignTask("task-1", "member1");
// 流程：leader → leader.stream → member1收到 → 完成后 → leader.stream(UP) → 所有人收到
```

### 示例2：使用 EventSourcing 的银行账户

```csharp
// 定义事件
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

// 状态
message BankAccountState {
    string account_holder = 1;
    double balance = 2;
    repeated string transaction_history = 3;
}

// Agent - 使用事件溯源
public class BankAccountAgent : GAgentBaseWithEventSourcing<BankAccountState>
{
    // 创建账户
    public async Task CreateAccountAsync(string holder, double initialBalance)
    {
        var evt = new AccountCreated {
            AccountHolder = holder,
            InitialBalance = initialBalance
        };
        RaiseEvent(evt, new Dictionary<string, string> {
            ["Operation"] = "CreateAccount",
            ["Holder"] = holder
        });
        await ConfirmEventsAsync();
    }

    // 存款
    public async Task DepositAsync(double amount, string description = "")
    {
        var evt = new MoneyDeposited {
            Amount = amount,
            Description = description
        };
        RaiseEvent(evt);
        await ConfirmEventsAsync();
    }

    // 取款
    public async Task WithdrawAsync(double amount, string description = "")
    {
        if (GetState().Balance < amount)
            throw new InvalidOperationException("Insufficient balance");

        var evt = new MoneyWithdrawn {
            Amount = amount,
            Description = description
        };
        RaiseEvent(evt);
        await ConfirmEventsAsync();
    }

    // 实现状态转换（纯函数）
    protected override void TransitionState(BankAccountState state, IMessage evt)
    {
        switch (evt)
        {
            case AccountCreated created:
                state.AccountHolder = created.AccountHolder;
                state.Balance = created.InitialBalance;
                state.TransactionHistory.Add($"[{GetCurrentVersion()}] Account created for {created.AccountHolder}");
                break;

            case MoneyDeposited deposited:
                state.Balance += deposited.Amount;
                state.TransactionHistory.Add($"[{GetCurrentVersion()}] Deposited ${deposited.Amount:F2} - {deposited.Description}");
                break;

            case MoneyWithdrawn withdrawn:
                state.Balance -= withdrawn.Amount;
                state.TransactionHistory.Add($"[{GetCurrentVersion()}] Withdrew ${withdrawn.Amount:F2} - {withdrawn.Description}");
                break;
        }
    }
}

// 使用
var actor = await factory.CreateGAgentActorAsync<BankAccountAgent>(accountId);
var agent = actor.GetAgent() as BankAccountAgent;

// 在组合根配置事件存储（自动启用事件溯源）
services.AddSingleton<IEventStore, OrleansEventStore>();
services.AddSingleton<IEventRepository>(sp => new MongoEventRepository(...));

// 执行业务操作
await agent.CreateAccountAsync("Alice Smith", 1000.0);
await agent.DepositAsync(500.0, "Salary");
await agent.WithdrawAsync(200.0, "Rent");

// 状态自动持久化到 MongoDB
// 事件自动存储并可回放
// Deactivate/Reactivate 时自动恢复状态
```

### 示例2：类型过滤优化

```csharp
// 只关心团队事件
public class TeamAgent : GAgentBase<TeamState, TeamEvent>
{
    // 框架会在订阅时自动添加类型过滤
    // 非TeamEvent的消息根本不会到达这个Agent
    [EventHandler]
    public async Task HandleTeamMessage(TeamMessageEvent evt)
    {
        // 只处理团队消息
    }
}
```

---

## 📐 Stream订阅管理

### SubscriptionManager

每个Runtime都有自己的 `ISubscriptionManager` 实现：

```csharp
public interface ISubscriptionManager
{
    // 创建订阅
    Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Guid subscriberId,
        IMessageStream targetStream,
        Func<T, Task> handler,
        Func<T, bool>? filter = null,
        CancellationToken ct = default) 
        where T : IMessage;
    
    // 取消订阅
    Task UnsubscribeAsync(Guid subscriptionId);
    
    // 获取订阅
    IMessageStreamSubscription? GetSubscription(Guid subscriptionId);
    
    // 获取所有活跃订阅
    IReadOnlyList<IMessageStreamSubscription> GetActiveSubscriptions();
}
```

### 恢复机制（Resume Mechanism）

订阅支持暂停和恢复，用于网络重连或临时故障：

```csharp
// 暂停订阅（停止接收消息）
await subscription.UnsubscribeAsync();

// 恢复订阅（重新开始接收）
await subscription.ResumeAsync();
```

**使用场景**:
- 网络重连
- 临时流量控制
- 优雅降级

---

## 🎭 实现对比

### Local Runtime

```csharp
// 使用Channel作为Stream
public class LocalMessageStream : IMessageStream
{
    private readonly Channel<IMessage> _channel;
    
    public async Task PublishAsync<T>(T message, CancellationToken ct = default) 
        where T : IMessage
    {
        await _channel.Writer.WriteAsync(message, ct);
    }
}
```

**特点**:
- 进程内通信
- 最快速度
- 无持久化

### Orleans Runtime

```csharp
// 使用Orleans Stream
public class OrleansMessageStream : IMessageStream
{
    private readonly IAsyncStream<byte[]> _stream;
    
    public async Task PublishAsync<T>(T message, CancellationToken ct = default) 
        where T : IMessage
    {
        var bytes = message.ToByteArray();
        await _stream.OnNextAsync(bytes);
    }
}
```

**特点**:
- 分布式
- 可持久化（可选）
- 虚拟Actor模型

### ProtoActor Runtime

```csharp
// 使用ProtoActor EventStream
public class ProtoActorMessageStream : IMessageStream
{
    private readonly EventStream _eventStream;
    
    public async Task PublishAsync<T>(T message, CancellationToken ct = default) 
        where T : IMessage
    {
        _eventStream.Publish(message);
        await Task.CompletedTask;
    }
}
```

**特点**:
- 轻量级Actor
- 高性能
- 灵活的生命周期

---

## 🎯 最佳实践

### 1. State设计

```protobuf
message AgentState {
    string id = 1;
    
    // ✅ 使用Timestamp而非自定义时间格式
    google.protobuf.Timestamp created_at = 2;
    
    // ✅ 使用repeated而非自定义列表
    repeated string items = 3;
    
    // ✅ 使用map而非自定义字典
    map<string, int32> counts = 4;
    
    // ✅ 使用double而非decimal
    double balance = 5;
}
```

### 2. Event设计

```protobuf
message UserActionEvent {
    string event_id = 1;
    
    // ✅ 包含足够的上下文信息
    string user_id = 2;
    string action_type = 3;
    
    // ✅ 使用oneof处理多态
    oneof payload {
        ClickPayload click = 10;
        PurchasePayload purchase = 11;
    }
    
    // ✅ 总是包含时间戳
    google.protobuf.Timestamp timestamp = 100;
}
```

### 3. 父子关系管理

```csharp
// ✅ 正确 - 双向建立关系
await child.SetParentAsync(parentId);  // 子设置父+订阅
await parent.AddChildAsync(childId);   // 父添加子引用

// ❌ 错误 - 单向关系
await child.SetParentAsync(parentId);  // 只设置父，父不知道子
```

### 4. 事件命名

```protobuf
// ✅ 好的事件命名
message OrderPlacedEvent { }      // 过去时态
message PaymentReceivedEvent { }  // 描述已发生的事实

// ❌ 不好的事件命名
message PlaceOrderEvent { }       // 命令式（这不是Event，是Command）
message OrderData { }             // 不明确（这是Event还是State？）
```

### 5. Stream订阅管理

```csharp
// ✅ 正确 - 使用using或记得Dispose
await using var subscription = await stream.SubscribeAsync<MyEvent>(async evt => {
    await HandleEvent(evt);
});

// ❌ 错误 - 忘记取消订阅会导致内存泄漏
var subscription = await stream.SubscribeAsync<MyEvent>(handler);
// ... 没有调用 DisposeAsync()
```

---

## 🔍 调试技巧

### 1. 验证序列化

```csharp
// 测试消息能否正确序列化/反序列化
var original = new MyEvent { Id = "test" };
var bytes = original.ToByteArray();
var deserialized = MyEvent.Parser.ParseFrom(bytes);
Assert.Equal(original.Id, deserialized.Id);
```

### 2. 追踪事件流

```csharp
[AllEventHandler]
public async Task LogAllEvents(EventEnvelope envelope)
{
    Logger.LogDebug("Event {EventId} from {SenderId} to {ReceiverId}",
        envelope.Id, envelope.SenderId, envelope.ReceiverId);
    await Task.CompletedTask;
}
```

### 3. 检查订阅状态

```csharp
var manager = serviceProvider.GetRequiredService<ISubscriptionManager>();
var activeSubscriptions = manager.GetActiveSubscriptions();
Logger.LogInformation("Active subscriptions: {Count}", activeSubscriptions.Count);
```

---

## ⚠️ 常见陷阱

### 1. 使用C#类而非Protobuf

```csharp
// ❌ 这会在Orleans runtime失败
public class MyState { public string Name { get; set; } }

// ✅ 必须用proto定义
// message MyState { string name = 1; }
```

### 2. 忘记订阅Stream

```csharp
// ❌ 只添加Child引用，但没有让Child订阅Parent stream
await parent.AddChildAsync(childId);  // Child不会收到DOWN事件

// ✅ 必须双向建立关系
await child.SetParentAsync(parentId);  // 这会自动订阅
await parent.AddChildAsync(childId);
```

### 3. 使用错误的EventDirection

```csharp
// ❌ 子节点报告用Down（消息发给自己的stream，没人订阅）
await PublishAsync(reportEvent, EventDirection.Down);

// ✅ 子节点报告用Up（消息发给父stream，大家都能收到）
await PublishAsync(reportEvent, EventDirection.Up);
```

### 4. 订阅泄漏

```csharp
// ❌ 创建了订阅但从不释放
for (int i = 0; i < 1000; i++)
{
    await stream.SubscribeAsync<MyEvent>(handler);  // 内存泄漏！
}

// ✅ 管理订阅生命周期
var subscriptions = new List<IMessageStreamSubscription>();
try {
    for (int i = 0; i < 1000; i++)
    {
        var sub = await stream.SubscribeAsync<MyEvent>(handler);
        subscriptions.Add(sub);
    }
} finally {
    foreach (var sub in subscriptions)
    {
        await sub.DisposeAsync();
    }
}
```

---

## 📚 参考

相关文档：
- `EVENTSOURCING.md` - EventSourcing详细指南
- `AI_INTEGRATION.md` - AI Agent集成
- `RUNTIME_GUIDE.md` - 运行时选择

代码示例：
- `examples/Demo.Agents/HierarchicalStreamingAgents.cs` - 层次化Stream示例
- `test/Aevatar.Agents.Core.Tests/Streaming/StreamMechanismTests.cs` - Stream机制测试

---

**记住**: Protobuf + Stream + EventDirection = Aevatar Agent Framework的三大基石 🌌

