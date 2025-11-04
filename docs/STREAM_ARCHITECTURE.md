# Stream Architecture - 向上回响机制

## 🌌 核心设计理念

新的Stream架构实现了一个优雅的**向上回响（Upward Echo）**事件传播机制：

- **子节点向上发布**：事件向父节点stream发送
- **父节点自动广播**：父stream将事件广播给所有订阅者（所有子节点）
- **类型早期筛选**：基于泛型约束进行事件过滤，减少无效处理

## 📐 架构设计

### 1. Stream订阅管理

```csharp
public interface IMessageStream
{
    Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler, 
        CancellationToken ct = default) where T : IMessage;
    
    Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler,
        Func<T, bool> filter,  // 类型过滤器
        CancellationToken ct = default) where T : IMessage;
}
```

### 2. 订阅生命周期

```csharp
public interface IMessageStreamSubscription : IAsyncDisposable
{
    Guid SubscriptionId { get; }
    Guid StreamId { get; }
    bool IsActive { get; }
    
    Task UnsubscribeAsync();  // 取消订阅
    Task ResumeAsync();        // 恢复订阅
}
```

## 🔄 事件流向

### 传统模式 vs 新模式

**传统模式**：
```
Parent
  ├─→ Child1  (父主动推送)
  ├─→ Child2  (父主动推送)
  └─→ Child3  (父主动推送)
```

**新模式（向上回响）**：
```
Parent [Stream]
  ↑         ↓ (自动广播)
Child1 → Child2, Child3
Child2 → Child1, Child3  
Child3 → Child1, Child2
```

### 关键变化

1. **父子关系建立时**：
   - 子节点自动订阅父节点的stream
   - 使用类型过滤器筛选相关事件

2. **子节点发布事件时**：
   - Direction = Up：发送到父stream
   - 父stream自动广播给所有订阅者

3. **父子关系解除时**：
   - 子节点自动取消订阅
   - 释放订阅资源

## 🎯 类型筛选机制

### GAgentBase<TState, TEvent> 类型约束

```csharp
public class TeamMemberAgent : GAgentBase<State, TeamEvent>
{
    // 只处理TeamEvent及其子类
}
```

### 自动类型过滤

订阅时自动检测Agent的TEvent类型：

```csharp
// 检查Agent是否继承自GAgentBase<TState, TEvent>
if (baseType.IsGenericType && 
    baseType.GetGenericTypeDefinition() == typeof(GAgentBase<,>))
{
    var eventType = baseType.GetGenericArguments()[1];
    // 创建类型过滤器
    filter = envelope => 
        envelope.Payload.TypeUrl.Contains(eventType.Name);
}
```

## 🚀 实现细节

### Orleans实现

```csharp
public async Task SetParentAsync(Guid parentId)
{
    // 订阅父节点stream
    var messageStream = new OrleansMessageStream(parentId, _parentStream);
    _parentStreamSubscription = await messageStream.SubscribeAsync<EventEnvelope>(
        async envelope => await _agent.HandleEventAsync(envelope),
        filter);  // 类型过滤器
}

public async Task ClearParentAsync()
{
    // 取消订阅
    if (_parentStreamSubscription != null)
    {
        await _parentStreamSubscription.UnsubscribeAsync();
        _parentStreamSubscription = null;
    }
}
```

### ProtoActor实现

ProtoActor基于消息传递，订阅管理相对简单：

```csharp
// 订阅只是记录handler
_parentStreamSubscription = new ProtoActorStreamSubscription(
    subscriptionId, streamId, handler, filter, ...);

// 取消订阅只需标记为非活跃
_isActive = false;
```

### Local实现

基于Channel的高性能实现：

```csharp
// 使用ConcurrentDictionary管理订阅
_subscriptions.TryAdd(subscriptionId, subscription);

// 处理消息时检查活跃订阅
var tasks = _subscriptions.Values
    .Where(sub => sub.IsActive)
    .Select(sub => sub.HandleMessageAsync(envelope));
```

## 💫 使用示例

### 团队协作场景

```csharp
// 团队领导（父节点）
public class TeamLeaderAgent : GAgentBase<State, TeamEvent>
{
    public async Task AssignTask(string taskId, string assignTo)
    {
        // 向下广播任务分配
        await PublishAsync(new TaskAssignedEvent {...}, 
            EventDirection.Down);
    }
}

// 团队成员（子节点）
public class TeamMemberAgent : GAgentBase<State, TeamEvent>
{
    public async Task CompleteTask(string taskId)
    {
        // 向上发布完成事件（自动广播给全组）
        await PublishAsync(new TaskCompletedEvent {...}, 
            EventDirection.Up);
    }
}
```

### 建立关系

```csharp
// 建立父子关系（触发stream订阅）
await memberActor.SetParentAsync(leaderId);
await leaderActor.AddChildAsync(memberId);

// 解除关系（触发取消订阅）
await memberActor.ClearParentAsync();
await leaderActor.RemoveChildAsync(memberId);
```

## 🎨 优势

1. **解耦合**：子节点不需要知道兄弟节点的存在
2. **自动广播**：组内通信自动化
3. **类型安全**：编译时类型检查
4. **性能优化**：早期事件过滤，减少无效处理
5. **资源管理**：自动订阅/取消订阅

## 🔍 注意事项

1. **Orleans限制**：
   - 需要配置Stream Provider
   - 注意StreamSubscriptionHandle的生命周期

2. **类型匹配**：
   - TypeUrl基于Protobuf的Any类型
   - 确保事件类型正确序列化

3. **内存管理**：
   - 及时取消不需要的订阅
   - 避免循环引用

## 🌟 最佳实践

1. **使用类型约束**：尽量使用`GAgentBase<TState, TEvent>`
2. **明确事件方向**：Up = 组内广播，Down = 层级传播
3. **及时清理**：解除关系时确保取消订阅
4. **异常处理**：订阅handler中要有异常处理

## 🚧 未来改进

1. **订阅恢复**：实现Resume功能，支持断线重连
2. **订阅过期**：自动清理长时间不活跃的订阅
3. **背压处理**：Stream满时的处理策略
4. **监控指标**：订阅数量、消息延迟等指标
