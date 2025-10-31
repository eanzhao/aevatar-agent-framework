# Phase 5 已启动 - EventSourcing 实现

## 📅 启动时间
2025年10月31日

## 🎯 Phase 5 目标

为框架添加 EventSourcing 支持，允许：
1. 状态变更持久化为事件序列
2. 从事件重放恢复状态
3. 完整的审计日志
4. 时间旅行能力

## ✅ 已完成（基础）

### EventSourcing 核心接口

**IEventStore** - 事件存储接口
- SaveEventAsync / SaveEventsAsync - 保存事件
- GetEventsAsync - 读取事件（支持版本范围）
- GetLatestVersionAsync - 获取最新版本
- ClearEventsAsync - 清除事件

**StateLogEvent** - 状态日志事件
- EventId, AgentId, Version
- EventType, EventData (byte[])
- TimestampUtc, Metadata

**InMemoryEventStore** - 内存实现
- 用于测试和 Local 运行时
- 基于 Dictionary<Guid, List<StateLogEvent>>
- 线程安全

## 📋 调研结果

### ProtoActor EventSourcing
- ✅ 支持 Persistence（Proto.Persistence）
- ✅ 提供多种存储：MongoDB, SQL Server, SQLite
- ✅ 使用 Snapshot 优化
- ⏳ 需要实现自定义 persistence provider

### Orleans EventSourcing  
- ✅ JournaledGrain<TState, TEvent>
- ✅ LogConsistencyProvider
- ✅ old/framework 已有完整实现
- ✅ 支持多种存储（Memory, Azure, Custom）

### 集成策略

**设计原则**：
- EventSourcing 在 Actor 层实现
- Agent 层保持无依赖
- 可选启用（不强制）

## 🏗️ 实现计划

### Local 运行时
- ✅ InMemoryEventStore
- [ ] LocalGAgentActorWithES（继承 LocalGAgentActor）
- [ ] 状态重放机制

### ProtoActor 运行时
- [ ] ProtoActorEventStore（基于 Proto.Persistence）
- [ ] ProtoActorGAgentActorWithES
- [ ] Snapshot 支持

### Orleans 运行时
- [ ] 使用 JournaledGrain
- [ ] LogConsistencyProvider 配置
- [ ] OrleansGAgentGrainWithES

## 📊 当前进度

```
✅ 基础接口定义
✅ InMemoryEventStore 实现
✅ GAgentBaseWithEventSourcing 实现
✅ 状态重放机制
✅ Snapshot 支持（基础）
⏳ Actor 层集成（3种运行时）
⏳ 完整测试

Phase 5 进度: 60%
```

## 🎯 与 Aspire 的关系

**好消息**：
- 当前的 AgentMetrics 使用标准 System.Diagnostics.Metrics
- 完全兼容 Aspire！
- 无需添加特殊的 Aspire Metrics
- EventSourcing 的事件也可以通过 Metrics 监控

## 📝 下一步

1. 实现 LocalGAgentActorWithES
2. 集成 Proto.Persistence
3. 配置 Orleans JournaledGrain
4. 添加 Snapshot 机制
5. 编写测试

---

*EventSourcing 让状态的每次震动都被永久记录，时间的长河可以倒流。* 🌌

