# Agent ID 使用指南

## 概述

Aevatar Agent Framework 使用 **字符串 ID** 来标识 Agent。系统内部存在两种 ID 格式：

| 格式 | 示例 | 用途 |
|------|------|------|
| 原始 Guid | `"abc-123-def"` | Manager 操作 |
| 完整 GrainKey | `"CalculatorAgent:abc-123-def"` | Stream 路由 |

## 快速规则

```
┌─────────────────────────────────────────────────────────────────┐
│  创建时传入：原始 Guid                                           │
│  Manager 操作：用原始 Guid（你传入的那个）                        │
│  SendToAsync：用 actor.Id（完整格式）                            │
└─────────────────────────────────────────────────────────────────┘
```

## API 分类

### 1. 创建类 API（传入原始 Guid）

```csharp
// 传入原始 Guid 字符串
var myId = Guid.NewGuid().ToString();  // "abc-123"
var actor = await factory.CreateGAgentActorAsync<CalculatorAgent>(myId);
// actor.Id = "CalculatorAgent:abc-123"（自动组装）

// 或让系统自动生成
var actor = await factory.CreateGAgentActorAsync<CalculatorAgent>();
```

### 2. Manager 操作（用原始 Guid）

Manager 内部用**原始 Guid** 作为存储 key：

```csharp
var rawId = Guid.NewGuid().ToString();  // "abc-123"
var actor = await manager.CreateAndRegisterAsync<CalculatorAgent>(rawId);

// ✅ 查找：用原始 Guid
var found = await manager.GetActorAsync(rawId);  // "abc-123"

// ✅ 链接：用原始 Guid
await manager.LinkParentChildAsync(parentRawId, childRawId);

// ✅ 停用：用原始 Guid
await manager.DeactivateAndUnregisterAsync(rawId);

// ❌ 错误：用 actor.Id 会找不到！
var notFound = await manager.GetActorAsync(actor.Id);  // null!
```

### 3. SendToAsync（用 actor.Id）

点对点通信需要**完整的 GrainKey** 才能路由到正确的 Stream：

```csharp
var senderId = Guid.NewGuid().ToString();
var sender = await manager.CreateAndRegisterAsync<SimpleAgent>(senderId);

var receiverId = Guid.NewGuid().ToString();
var receiver = await manager.CreateAndRegisterAsync<SimpleAgent>(receiverId);

// ✅ 正确：用 actor.Id（完整格式）
await sender.SendToAsync(receiver.Id, message);  
// receiver.Id = "SimpleAgent:xxx" → 路由到正确的 Grain

// ❌ 错误：用原始 Guid 无法路由！
await sender.SendToAsync(receiverId, message);  // 找不到 Stream!
```

## 完整示例

```csharp
// ====== 创建阶段 ======
var parentRawId = Guid.NewGuid().ToString();  // 保存原始 ID
var parentActor = await manager.CreateAndRegisterAsync<TeamLeaderAgent>(parentRawId);

var childRawId = Guid.NewGuid().ToString();  // 保存原始 ID
var childActor = await manager.CreateAndRegisterAsync<TeamMemberAgent>(childRawId);

// ====== Manager 操作：用原始 Guid ======
await manager.LinkParentChildAsync(parentRawId, childRawId);  // ✅ 原始 Guid
var found = await manager.GetActorAsync(parentRawId);  // ✅ 原始 Guid

// ====== 点对点通信：用 actor.Id ======
await parentActor.SendToAsync(childActor.Id, message);  // ✅ actor.Id（完整格式）

// ====== 清理：用原始 Guid ======
await manager.DeactivateAndUnregisterAsync(parentRawId);  // ✅ 原始 Guid
await manager.DeactivateAndUnregisterAsync(childRawId);   // ✅ 原始 Guid
```

## 推荐的变量命名

```csharp
// 清晰的命名约定
var calculatorRawId = Guid.NewGuid().ToString();  // 原始 Guid
var calculatorActor = await manager.CreateAndRegisterAsync<CalculatorAgent>(calculatorRawId);
var calculatorGrainKey = calculatorActor.Id;  // 完整格式，用于 SendToAsync

// 使用
await manager.GetActorAsync(calculatorRawId);  // Manager 操作
await sender.SendToAsync(calculatorGrainKey, msg);  // 点对点通信
```

## API 速查表

| API | 用什么 ID | 示例 |
|-----|-----------|------|
| `CreateGAgentActorAsync<T>(id)` | 原始 Guid | `"abc-123"` |
| `CreateAndRegisterAsync<T>(id)` | 原始 Guid | `"abc-123"` |
| `GetActorAsync(id)` | 原始 Guid | `"abc-123"` |
| `LinkParentChildAsync(p, c)` | 原始 Guid | `"abc-123"` |
| `DeactivateAndUnregisterAsync(id)` | 原始 Guid | `"abc-123"` |
| `ExistsAsync(id)` | 原始 Guid | `"abc-123"` |
| `SendToAsync(targetId, evt)` | **actor.Id** | `"AgentType:abc-123"` |

## 类型安全

相同 Guid + 不同类型 = 不同 Agent（不冲突）：

```csharp
var sameGuid = "xyz-789";

var calc = await factory.CreateGAgentActorAsync<CalculatorAgent>(sameGuid);
// calc.Id = "CalculatorAgent:xyz-789"

var weather = await factory.CreateGAgentActorAsync<WeatherAgent>(sameGuid);
// weather.Id = "WeatherAgent:xyz-789"

// 两个完全不同的 Grain！
```

## 常见问题

### Q: 为什么 Manager 和 SendToAsync 用不同的 ID？

A: 
- **Manager** 是本地缓存，存储时用你传入的原始 Guid
- **SendToAsync** 是跨网络的 Stream 路由，需要完整 GrainKey 定位目标

### Q: 如何知道用哪种 ID？

A:
- Manager 的方法（Create/Get/Link/Deactivate） → 用**原始 Guid**
- SendToAsync → 用 **actor.Id**

### Q: 为什么不统一？

A: 这是当前实现的特点。如果需要统一，可以修改 Manager 用 `actor.Id` 作为 key。但这会是一个 breaking change。

## 内部原理

```
用户传入 "abc-123"
        ↓
┌─────────────────────────────────────┐
│       Manager 存储                   │
│  _actors["abc-123"] = actor         │  ← 用原始 Guid
└─────────────────────────────────────┘
        ↓
┌─────────────────────────────────────┐
│       Actor 属性                     │
│  actor.Id = "AgentType:abc-123"     │  ← 完整 GrainKey
└─────────────────────────────────────┘
        ↓
┌─────────────────────────────────────┐
│       Orleans Grain                  │
│  GetPrimaryKeyString()               │
│  = "AgentType:abc-123"              │  ← 完整 GrainKey
└─────────────────────────────────────┘
        ↓
┌─────────────────────────────────────┐
│       Stream 路由                    │
│  StreamId = "AgentType:abc-123"     │  ← 需要完整格式
└─────────────────────────────────────┘
```
