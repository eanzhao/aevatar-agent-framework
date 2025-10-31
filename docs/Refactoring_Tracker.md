# Aevatar Agent Framework 重构追踪文档

## 📋 重构目标

将 `old/framework` 中过度依赖 Orleans 的框架重构为支持多运行时（Local/ProtoActor/Orleans）的轻量化架构。

## 🎯 核心设计原则

### 1. 分层抽象
- **IGAgent** - 纯业务逻辑层（事件处理器）
- **IGAgentActor** - 运行时抽象层（Stream 管理、层级关系、事件路由）
- **GAgentBase** - 业务逻辑基类（提供事件处理机制）

### 2. 序列化方案
- ✅ **已完成**：统一使用 Protobuf（通过 Grpc.Tools 生成代码）

### 3. 运行时支持
- Local - 本地内存运行
- ProtoActor - 基于 Proto.Actor
- Orleans - 基于 Orleans Grain

## 🔑 关键设计决策

### Decision 1: EventSourcing
- **状态**：保留特性，但作为 TODO 后续通过 Actor 层扩展
- **原因**：先实现核心事件传播机制，EventSourcing 可以作为运行时层的可选特性
- **TODO**：在 Actor 层实现 StateLogEvent 持久化机制

### Decision 2: 事件传播方向
- **保留所有方向**：
  - `Up` - 向父级传播
  - `Down` - 向子级传播
  - `UpThenDown` - 先向上再向下（广播到兄弟节点）
  - `Bidirectional` - 双向传播
- **控制机制**：
  - `MaxHopCount` - 最大跳数
  - `MinHopCount` - 最小跳数
  - `CurrentHopCount` - 当前跳数
  - `ShouldStopPropagation` - 停止传播标志

### Decision 3: Stream 设计
- **粒度**：每个 Agent 一个独立 Stream
- **标识**：基于 `string` 类型的 AgentId（不使用 Orleans GrainId）
- **优势**：兼容多种 Streaming 实现（Orleans Stream、Kafka、本地 Channel 等）

### Decision 4: 泛型参数分配

#### old/framework 的泛型
```csharp
GAgentBase<TState, TStateLogEvent, TEvent, TConfiguration>
```

#### 新架构的泛型分配

**IGAgent 层（业务逻辑）**
```csharp
IGAgent                              // 最小抽象，只有 Id
IGAgent<TState>                      // 有状态的 Agent
```

**GAgentBase 层（业务逻辑基类）**
```csharp
GAgentBase<TState, TEvent>           // 基础事件处理
GAgentBase<TState, TEvent, TConfiguration>  // 带配置支持（可选）
```

**IGAgentActor 层（运行时抽象）**
```csharp
IGAgentActor                         // 不需要泛型，运行时包装器
```

**理由**：
- `TState` - Agent 的业务状态（Agent 层）
- `TEvent` - 业务事件基类，约束事件类型（Agent 层）
- `TConfiguration` - Agent 配置，动态配置行为（Agent 层，可选）
- `TStateLogEvent` - EventSourcing 日志（Actor 层，TODO 后续实现）

## 📦 模块结构

### Aevatar.Agents.Abstractions
核心抽象接口和类型定义

**接口**：
- `IGAgent` - Agent 基础接口
- `IGAgent<TState>` - 有状态的 Agent 接口
- `IGAgentActor` - Actor 运行时接口
- `IGAgentActorFactory` - Actor 工厂接口
- `IMessageStream` - Stream 抽象接口
- `IMessageSerializer` - 序列化接口

**类型**：
- `EventEnvelope` - 事件信封（Protobuf 定义）
- `EventDirection` - 事件传播方向枚举
- `messages.proto` - Protobuf 定义文件

### Aevatar.Agents.Core
业务逻辑核心实现

**类**：
- `GAgentBase<TState, TEvent>` - Agent 基类
- `GAgentBase<TState, TEvent, TConfiguration>` - 带配置的 Agent 基类

**功能**：
- 事件处理器自动发现（反射）
- 事件处理器调用机制
- 优先级支持
- AllEventHandler 支持（转发所有事件）

### Aevatar.Agents.Local
本地运行时实现

**类**：
- `LocalGAgentActor<TState>` - 本地 Actor 实现
- `LocalGAgentFactory` - 本地 Actor 工厂
- `LocalMessageStream` - 基于 Channel 的 Stream

### Aevatar.Agents.ProtoActor
Proto.Actor 运行时实现

**类**：
- `ProtoActorGAgentActor<TState>` - Proto.Actor Actor 包装
- `ProtoActorGAgentFactory` - Proto.Actor 工厂
- `ProtoActorMessageStream` - 基于 Proto.Actor Stream

### Aevatar.Agents.Orleans
Orleans 运行时实现

**类**：
- `OrleansGAgentActor` - Orleans Grain 包装
- `OrleansGAgentGrain` - Orleans Grain 实现
- `OrleansGAgentFactory` - Orleans 工厂
- `OrleansMessageStream` - 基于 Orleans Stream

## 🚧 重构任务清单

### Phase 1: 核心抽象重构 (优先级：高) ✅ **完成**
- [x] 重新设计 `EventEnvelope`（添加传播控制字段）
- [x] 重新设计 `IGAgent` 接口
- [x] 重新设计 `IGAgentActor` 接口
- [x] 更新 `messages.proto` 定义
- [x] 添加 `IEventPublisher` 接口
- [x] 添加 `EventHandlerAttribute`、`AllEventHandlerAttribute`、`ConfigurationAttribute`

### Phase 2: GAgentBase 重构 (优先级：高) ✅ **完成**
- [x] 实现事件处理器自动发现机制（反射 + 缓存）
- [x] 实现 `EventHandlerAttribute` 和 `AllEventHandlerAttribute`
- [x] 实现优先级支持
- [x] 移除运行时依赖（Factory、Serializer 等）
- [x] 实现 `GAgentBase<TState>`
- [x] 实现 `GAgentBase<TState, TEvent>` - 带事件类型约束
- [x] 实现 `GAgentBase<TState, TEvent, TConfiguration>` - 带配置支持

### Phase 3: Actor 层实现 (优先级：高) ✅ **完成**
- [x] 实现层级关系管理（Parent/Children）
- [x] 实现事件路由逻辑（Up/Down/UpThenDown/Bidirectional）
- [x] 实现 HopCount 控制
- [x] Local 运行时实现
  - [x] LocalGAgentActor - 完整事件路由
  - [x] LocalGAgentFactory - Actor 工厂
- [ ] ProtoActor 运行时实现
- [ ] Orleans 运行时实现

### Phase 4: 高级特性迁移 (优先级：中)
- [ ] 迁移 Observer 机制
- [ ] 迁移 StateDispatcher（状态投影）
- [ ] 迁移 ResourceContext（资源管理）
- [ ] 迁移 GAgentManager
- [ ] 迁移 GAgentFactory

### Phase 5: EventSourcing 支持 (优先级：低，TODO)
- [ ] 设计 StateLogEvent 抽象
- [ ] Actor 层实现状态持久化
- [ ] 实现 TransitionState 机制
- [ ] 实现事件回放（Replay）
- [ ] Orleans JournaledGrain 集成

### Phase 6: 测试和文档 (优先级：中)
- [ ] 单元测试覆盖
- [ ] 集成测试
- [ ] 性能测试
- [ ] 迁移指南文档
- [ ] API 文档

## 🔍 old/framework 关键特性清单

需要迁移的特性（来自代码分析）：

### 核心机制 ✅ **已完成**
- [x] **序列化**：Protobuf（已完成）
- [x] **事件传播**：Up/Down/UpThenDown/Bidirectional（已完成）
- [x] **层级关系**：Parent/Children 管理（已完成）
- [x] **Stream 机制**：每个 Agent 独立 Stream（通过 Actor 实现）
- [x] **Observer 模式**：通过 EventHandler Attribute 实现
- [x] **事件处理器**：反射自动发现和注册（已完成）

### 事件处理 ✅ **已完成**
- [x] `[EventHandler]` - 标记事件处理方法（已完成）
- [x] `[AllEventHandler]` - 处理所有事件/转发（已完成）
- [x] `Priority` - 处理器优先级（已完成）
- [x] `AllowSelfHandling` - 是否允许处理自己发出的事件（已完成）
- [ ] Response Handler - 返回响应事件（TODO: Phase 4）

### 状态管理 🚧 **部分完成**
- [x] `StateBase` - Parent/Children 在 Actor 层管理（已完成）
- [x] `OnActivateAsync` / `OnDeactivateAsync` - 生命周期回调（已完成）
- [ ] `OnStateChanged` - 状态变更回调（TODO: Phase 5 EventSourcing）
- [ ] `StateDispatcher` - 状态投影和发布（TODO: Phase 4）
- [ ] `GetStateSnapshot` - 状态快照（TODO: Phase 4）

### 生命周期 ✅ **已完成**
- [x] `OnActivateAsync` - 激活回调（已完成）
- [x] `OnDeactivateAsync` - 停用回调（已完成）
- [x] Actor 层的 ActivateAsync/DeactivateAsync（已完成）

### 层级关系 ✅ **已完成**
- [x] `AddChildAsync` / `RemoveChildAsync` - 添加/移除子 Agent（已完成）
- [x] `SetParentAsync` / `ClearParentAsync` - 设置/清除父 Agent（已完成）
- [x] `GetChildrenAsync` / `GetParentAsync` - 获取关系（已完成）
- [x] 层级关系存储在 Actor 层（已完成）

### 配置和资源 🚧 **部分完成**
- [x] `ConfigureAsync` / `OnConfigureAsync` - 动态配置（已完成）
- [x] `GetConfigurationType` - 获取配置类型（已完成）
- [ ] `PrepareResourceContextAsync` - 资源上下文准备（TODO: Phase 4）
- [ ] `GetAllSubscribedEventsAsync` - 获取订阅的事件类型（TODO: Phase 4）

### 异常处理 🚧 **部分完成**
- [x] 事件处理器异常自动捕获和记录（已完成）
- [ ] `EventHandlerExceptionEvent` - 事件处理异常事件（TODO: Phase 4）
- [ ] `GAgentBaseExceptionEvent` - 框架异常事件（TODO: Phase 4）
- [ ] 异常自动发布机制（TODO: Phase 4）

### Observability ✅ **已完成**
- [x] Logging - 完整的日志支持（已完成）
- [x] CorrelationId - 在 EventEnvelope 中（已完成）
- [x] Publishers 链追踪（已完成）
- [x] PublishedTimestampUtc - 发布时间戳（已完成）
- [x] EventEnvelopeExtensions - 时间戳辅助方法（已完成）
- [x] GAgentExtensions - Agent 辅助方法（GetStateSnapshot 等，已完成）
- [ ] Logging with scope（TODO: Phase 4 可优化）
- [ ] ActivitySource - 分布式追踪（TODO: Phase 4）

## 📝 实现注意事项

### 1. Stream ID 设计
- old: `StreamId.Create(namespace, grainId.ToString())`
- new: `StreamId.Create(namespace, agentId)` - agentId 是 string 类型

### 2. 事件传播控制
必须在 Actor 层实现，检查 `EventDirection` 和 `HopCount`：
```csharp
if (event.MaxHopCount > 0 && event.CurrentHopCount >= event.MaxHopCount)
    return; // 停止传播

event.CurrentHopCount++;
```

### 3. Parent/Children 管理
- Parent/Children 存储在 Actor 层（不在 Agent 的 State 中）
- Local: 内存 Dictionary
- ProtoActor: Actor 状态
- Orleans: Grain State（可选 EventSourcing）

### 4. 事件处理器发现
使用反射，缓存结果：
```csharp
GetMethods()
  .Where(m => m.HasAttribute<EventHandlerAttribute>() || IsDefaultHandler(m))
  .OrderBy(m => m.GetAttribute<EventHandlerAttribute>()?.Priority ?? 0)
```

### 5. AllEventHandler 转发
用于中间层 Agent，转发所有事件给子节点：
```csharp
[AllEventHandler(allowSelfHandling: true)]
protected virtual async Task ForwardEventAsync(EventWrapperBase eventWrapper)
{
    await SendEventDownwardsAsync(eventWrapper);
}
```

## 🎨 架构图

```
┌─────────────────────────────────────────────────────────┐
│                   Application Layer                      │
│              (CalculatorAgent, WeatherAgent)             │
└────────────────────┬────────────────────────────────────┘
                     │ inherits
┌────────────────────▼────────────────────────────────────┐
│                 GAgentBase<TState, TEvent>               │
│  - Event Handler Discovery (Reflection)                 │
│  - Event Handler Invocation                             │
│  - State Management                                      │
└────────────────────┬────────────────────────────────────┘
                     │ implements
┌────────────────────▼────────────────────────────────────┐
│                   IGAgent<TState>                        │
│  - Id: string                                            │
│  - GetState()                                            │
└──────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│                  IGAgentActor (Runtime)                  │
│  - Parent/Children Management                            │
│  - Stream Subscription/Publishing                        │
│  - Event Routing (Up/Down/UpThenDown/Bidirectional)    │
│  - HopCount Control                                      │
└────────────────────┬────────────────────────────────────┘
                     │ implementations
         ┌───────────┼───────────┐
         │           │           │
    ┌────▼────┐ ┌───▼────┐ ┌───▼─────┐
    │  Local  │ │ Proto  │ │ Orleans │
    │  Actor  │ │ Actor  │ │  Actor  │
    └─────────┘ └────────┘ └─────────┘
```

## 🔗 相关文档

- [AgentSystem_Architecture.md](./AgentSystem_Architecture.md) - 系统架构文档
- [Protobuf_Configuration_Guide.md](./Protobuf_Configuration_Guide.md) - Protobuf 配置指南
- [old/framework/TECHNICAL_DOCUMENTATION.md](../old/framework/TECHNICAL_DOCUMENTATION.md) - 原框架技术文档

## 📅 时间线

- **2025-10-31**：
  - ✅ 创建重构追踪文档，明确设计决策
  - ✅ Phase 1 完成 - 核心抽象重构
  - ✅ Phase 2 完成 - GAgentBase 重构（包括 TEvent 和 TConfiguration 扩展）
  - ✅ Phase 3 完成 - 三种运行时全部实现
  - ✅ 更新示例代码（CalculatorAgent、WeatherAgent）
  - ✅ 单元测试编写（20个测试全部通过）
  - ✅ 示例代码修复（SimpleDemo、Demo.Api）
  - ✅ 所有编译错误和运行时错误修复
  - ✅ 文档完善（6篇指南文档）
- **Phase 4 目标**：特性迁移（StateDispatcher、ResourceContext 等）
- **Phase 5 目标**：EventSourcing 完整实现

## ✨ 当前成果

### 已实现功能
1. **核心抽象层**：
   - `IGAgent` / `IGAgent<TState>` - 纯业务逻辑接口
   - `IGAgentActor` - 运行时抽象接口
   - `IEventPublisher` - 事件发布接口
   - `EventEnvelope` - 完整的事件传播控制（Direction、HopCount、Publishers 链）
   - Attributes - EventHandler、AllEventHandler、Configuration

2. **业务逻辑层**：
   - `GAgentBase<TState>` - 基础 Agent 类
   - `GAgentBase<TState, TEvent>` - 带事件类型约束
   - `GAgentBase<TState, TEvent, TConfiguration>` - 带配置支持
   - 事件处理器自动发现（反射 + 缓存）
   - 优先级支持
   - 自动 Protobuf Unpack
   - AllowSelfHandling 控制

3. **三种运行时实现**：
   - **Local**: LocalGAgentActor + Factory（完整事件路由）
   - **ProtoActor**: ProtoActorGAgentActor + AgentActor + Factory
   - **Orleans**: OrleansGAgentGrain + Actor + Factory（byte[] 序列化）
   - 全部支持 Up/Down/UpThenDown/Bidirectional
   - HopCount 控制（MaxHop/MinHop/CurrentHop）
   - 层级关系管理（Parent/Children）

4. **测试和示例**：
   - 20 个单元测试（100% 通过）
   - SimpleDemo 控制台示例
   - Demo.Api WebAPI 示例
   - 完整的使用文档

### 编译状态
- ✅ Aevatar.Agents.Abstractions
- ✅ Aevatar.Agents.Core
- ✅ Aevatar.Agents.Local
- ✅ Aevatar.Agents.ProtoActor
- ✅ Aevatar.Agents.Orleans
- ✅ Demo.Agents
- ✅ SimpleDemo
- ✅ Demo.Api

### 测试状态
- ✅ Aevatar.Agents.Core.Tests (12/12)
- ✅ Aevatar.Agents.Local.Tests (8/8)
- ✅ 总计: 20/20 通过 (100%)

### Phase 3 新增扩展功能 ✅
- [x] PublishedTimestampUtc - 发布时间戳字段（已完成）
- [x] EventEnvelopeExtensions - 时间戳辅助方法（已完成）
- [x] GAgentExtensions - Agent 辅助方法（已完成）
  - GetStateSnapshot() - 状态快照
  - GetEventHandlerNames() - 获取处理器名称
  - GetSubscribedEventTypes() - 获取订阅的事件类型

### 待实现（Phase 4及后续）
- [ ] StateDispatcher - 状态投影和发布
- [ ] ResourceContext - 资源管理  
- [ ] Response Handler - 返回响应事件
- [ ] ActivitySource - 分布式追踪
- [ ] EventHandlerExceptionEvent - 异常事件自动发布
- [ ] 集成测试（跨运行时）
- [ ] 性能测试和基准测试
- [ ] EventSourcing 完整支持（Phase 5）

---

*语言震动的回响正在构建新的结构维度...*

