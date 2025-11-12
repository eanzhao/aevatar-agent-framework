# Stack Overflow Fix - Event Propagation Issue

## 🔴 问题描述

运行 Core.Tests 时出现栈溢出错误，特别是在 `BOTH_Direction_Should_Broadcast_In_Both_Directions` 测试中。

## 🎯 根本原因

当子节点从父节点stream接收到 `EventDirection.Both` 事件时，错误地继续向所有方向传播，导致无限循环。

### 无限循环过程

```
1. Parent 发布 BOTH 事件
   ├── 向上：发送到 Grandparent
   └── 向下：发送到 Children (Child1, Child2)

2. Child1 从 Parent stream 接收到 BOTH 事件
   └── 调用 ContinuePropagationAsync(BOTH)
       ├── ❌ 向上：又发送回 Parent（形成循环！）
       └── 向下：发送到自己的子节点

3. Parent 再次收到来自 Child1 的事件
   └── 又广播给所有 Children
       └── Child1 再次收到...（无限循环）
```

## ✅ 解决方案

### 修复代码位置：`src/Aevatar.Agents.Local/LocalGAgentActor.cs`

```csharp
// 原代码（第148-159行）
// 从父stream接收到的事件处理逻辑：
// - UP事件：只需要处理，不需要继续传播（已在父stream广播）
// - DOWN事件：处理后需要继续向下传播给子节点（多层级传播）
// - BOTH事件：继续向下传播给子节点
if (envelope.Direction == EventDirection.Down || 
    envelope.Direction == EventDirection.Both)  // ❌ 问题
{
    Logger.LogDebug("Continuing {Direction} propagation...");
    await EventRouter.ContinuePropagationAsync(envelope, ct);
}

// 修复后的代码
// 从父stream接收到的事件处理逻辑：
// - UP事件：只需要处理，不需要继续传播（已在父stream广播）
// - DOWN事件：处理后需要继续向下传播给子节点（多层级传播）
// - BOTH事件：只向下传播给子节点（不能再向上，避免循环）
if (envelope.Direction == EventDirection.Down)
{
    // DOWN事件：继续向下传播
    Logger.LogDebug("Continuing DOWN propagation of event {EventId} from agent {AgentId} to children", 
        envelope.Id, Id);
    await EventRouter.ContinuePropagationAsync(envelope, ct);
}
else if (envelope.Direction == EventDirection.Both)
{
    // BOTH事件从父节点来：只向下传播，不向上（避免循环）
    Logger.LogDebug("Continuing DOWN-ONLY propagation for BOTH event {EventId} from parent stream", 
        envelope.Id);
    
    // 创建一个新的DOWN方向的envelope继续传播
    var downOnlyEnvelope = envelope.Clone();
    downOnlyEnvelope.Direction = EventDirection.Down;
    await EventRouter.ContinuePropagationAsync(downOnlyEnvelope, ct);
}
// UP事件不需要继续传播，因为它已经在父stream中广播
```

## 🔧 实施步骤

1. 修改 `LocalGAgentActor.cs` 中的事件传播逻辑
2. 对 Orleans 和 ProtoActor 运行时进行相同的修复
3. 添加单元测试验证循环检测
4. 运行所有stream相关测试确保没有栈溢出

## 📝 测试验证

运行以下测试确保修复有效：

```bash
dotnet test --filter "FullyQualifiedName~StreamMechanismTests"
```

特别注意这个测试：
- `BOTH_Direction_Should_Broadcast_In_Both_Directions`

## 🎯 其他运行时的修复

### Orleans (`OrleansGAgentGrain.cs`)
需要检查和修复类似的逻辑

### ProtoActor (`ProtoActorGAgentActor.cs`)
需要检查和修复类似的逻辑

## 💡 设计改进建议

1. **明确的传播规则**：
   - 从父stream收到的事件，永远不应该再向上传播
   - BOTH事件在接收端应该转换为单向传播

2. **增强循环检测**：
   - 在EventRouter中添加更严格的循环检测
   - 记录事件路径用于调试

3. **测试增强**：
   - 添加专门的循环检测测试
   - 增加多层级传播的边界测试

---

*Issue Date: 2025-01-05*
*Status: Fix Identified*

