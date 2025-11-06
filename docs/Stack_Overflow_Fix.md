# 栈溢出问题修复记录

## 问题描述

`DOWN_Direction_Should_Broadcast_To_All_Children` 测试容易引发栈溢出异常。

## 根本原因

DOWN 方向的事件传播存在无限递归风险：

1. **缺少循环检测**：DOWN 方向传播没有检查节点是否已被访问过
2. **无递归深度限制**：没有强制执行 MaxHopCount 限制
3. **缺少去重机制**：同一事件可能被多次处理
4. **测试超时保护不足**：测试没有超时限制，栈溢出时会无限等待

## 递归链条分析

```
父节点发布 DOWN 事件
  ↓ RouteEventAsync
  ↓ SendToChildrenAsync (发送给所有子节点)
  ↓ 
子节点收到事件
  ↓ HandleEventAsync
  ↓ ContinuePropagationAsync (继续传播)
  ↓ SendToChildrenAsync (如果有子节点)
  ↓
如果存在循环引用或配置错误 → 无限递归 → 栈溢出
```

## 修复方案

### 1. 添加 DOWN 方向的循环检测
```csharp
// EventRouter.cs - SendToChildrenAsync
if (envelope.Publishers.Contains(childId.ToString()))
{
    _logger.LogWarning("Event {EventId} already visited child {ChildId}, skipping to avoid loop");
    continue;
}
```

### 2. 强制执行递归深度限制
```csharp
// 检查最大跳数
if (envelope.MaxHopCount > 0 && envelope.CurrentHopCount >= envelope.MaxHopCount)
{
    return;
}

// 安全阈值防止栈溢出
const int SafetyMaxHops = 100;
if (envelope.CurrentHopCount >= SafetyMaxHops)
{
    _logger.LogError("Event exceeded safety max hop count, force stopping");
    return;
}
```

### 3. 设置合理的默认 MaxHopCount
```csharp
// EventRouter.cs - CreateEventEnvelope
MaxHopCount = 50, // 之前是 -1（无限制）
```

### 4. 添加事件去重机制
```csharp
// GAgentActorBase.cs
private readonly HashSet<string> _processedEventIds = new();

private bool TryRecordEventId(string eventId)
{
    lock (_eventIdLock)
    {
        if (_processedEventIds.Contains(eventId))
            return false;
        _processedEventIds.Add(eventId);
        // 防止内存泄漏的清理逻辑...
        return true;
    }
}
```

### 5. 防止自引用
```csharp
// EventRouter.cs - SendToChildrenAsync
if (childId == _agentId)
{
    _logger.LogError("Agent attempted to send event to itself as child");
    continue;
}
```

### 6. 测试超时保护
```csharp
[Fact(Timeout = 5000)] // 5秒超时
public async Task DOWN_Direction_Should_Broadcast_To_All_Children()
```

## 修复后的效果

- ✅ 测试正常通过（~1秒完成）
- ✅ 事件正确传播到所有子节点
- ✅ 没有无限递归
- ✅ 循环引用被正确检测和阻止
- ✅ 内存使用受控（去重缓存有大小限制）

## 最佳实践建议

1. **始终设置 MaxHopCount**：不要使用 -1（无限制）
2. **监控日志**：注意循环检测警告
3. **测试超时**：所有涉及事件传播的测试都应该有超时保护
4. **父子关系验证**：添加子节点时验证不是自己
5. **定期清理**：事件ID缓存需要定期清理防止内存泄漏

## 相关文件

- `/src/Aevatar.Agents.Core/EventRouting/EventRouter.cs`
- `/src/Aevatar.Agents.Core/GAgentActorBase.cs`
- `/test/Aevatar.Agents.Core.Tests/Streaming/StreamMechanismTests.cs`
