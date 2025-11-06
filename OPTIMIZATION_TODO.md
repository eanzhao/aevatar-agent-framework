# Aevatar Agent Framework 优化计划

## 🎯 优化项目清单

### 🔴 高优先级 (High Priority)

#### 1. ✅ 统一父子订阅机制 [IN PROGRESS]
**价值**：
- 解决当前订阅实现分散在各runtime中的问题
- 确保Orleans/Local/ProtoActor的订阅行为一致
- 提供订阅失败后的自动重试能力
- 增强系统的可靠性和容错性

**实现方式**：
- 创建 `ISubscriptionManager` 接口统一管理订阅
- 实现重试策略（指数退避、最大重试次数）
- 添加订阅健康检查机制
- 在 `GAgentActorBase` 中集成统一订阅逻辑

#### 2. ✅ 改进事件去重机制 [IN PROGRESS]
**价值**：
- 避免当前HashSet方式的内存泄漏风险
- 提供更智能的去重策略（基于时间窗口）
- 降低内存占用，提升系统稳定性

**实现方式**：
- 使用 `MemoryCache` 替代 HashSet
- 实现基于时间的自动过期（TTL）
- 可选：实现LRU缓存策略
- 添加去重指标监控

#### 3. ⏳ 性能优化：Source Generator消除反射 [PLANNED]
**价值**：
- 大幅提升事件处理性能（预计3-5倍）
- 编译时类型安全检查
- 减少运行时开销
- 更好的IDE支持和代码提示

**实现方式**：
- 创建 Source Generator 项目
- 分析标记了 `[EventHandler]` 的方法
- 生成高性能的事件处理器映射代码
- 生成直接调用代码替代反射
- 保持向后兼容（渐进式迁移）

### 🟡 中优先级 (Medium Priority)

#### 4. 统一Stream抽象层增强
**价值**：
- 提供更强大的Stream功能（健康检查、重连、背压）
- 统一不同runtime的Stream行为
- 提升消息传输的可靠性

**实现方式**：
- 扩展 `IMessageStream` 接口
- 添加 `IsHealthyAsync()` 健康检查
- 实现 `ReconnectAsync()` 自动重连
- 支持背压（Back-Pressure）策略
- 添加消息缓冲和批处理能力

#### 5. 完成TODO项功能
**价值**：
- 补全框架的核心功能
- 提供完整的EventSourcing支持
- 增强Orleans环境的可用性

**待完成项**：
- Orleans EventSourcing中的动态Agent创建
- Proto.Persistence集成
- 重新设计Orleans事件处理架构

#### 6. 事件处理管道化（Pipeline Pattern）
**价值**：
- 提供灵活的事件处理中间件机制
- 便于添加横切关注点（日志、监控、验证）
- 提高代码的可维护性和可扩展性

**实现方式**：
- 创建 `IEventMiddleware` 接口
- 实现 `EventProcessingPipeline` 类
- 预置中间件：验证、去重、日志、指标
- 支持自定义中间件插入

#### 7. 增强重试机制
**价值**：
- 提高系统的容错能力
- 减少瞬时故障的影响
- 提供灵活的重试策略

**实现方式**：
- 实现 Polly 集成或自定义重试逻辑
- 支持多种重试策略（固定间隔、指数退避、抖动）
- 添加断路器（Circuit Breaker）模式
- 重试指标收集

### 🟢 低优先级 (Low Priority)

#### 8. AI Agent接口设计
**价值**：
- 为未来集成Microsoft Semantic Kernel做准备
- 提供AI能力的标准化接口
- 支持提示词管理和工具注册

**实现方式**：
- 定义 `IAIGAgent` 接口
- 设计 `AIContext` 和 `AITool` 抽象
- 实现提示词模板管理
- 设计AI响应到事件的转换机制

#### 9. 批处理优化
**价值**：
- 提高高吞吐场景的性能
- 减少网络往返次数
- 优化资源利用率

**实现方式**：
- 添加 `IBatchEventHandler` 接口
- 实现事件批量聚合逻辑
- 支持批处理配置（批大小、超时）
- 保持与单事件处理的兼容性

#### 10. 事件流可视化和调试工具
**价值**：
- 提供直观的事件流动可视化
- 便于调试和问题诊断
- 提升开发体验

**实现方式**：
- 创建 `IEventFlowVisualizer` 接口
- 收集事件追踪数据
- 生成事件流图（Mermaid/GraphViz）
- 可选：Web UI展示

#### 11. 增强监控和可观测性
**价值**：
- 提供生产环境的深度洞察
- 支持分布式追踪
- 便于性能分析和优化

**实现方式**：
- 深度集成 OpenTelemetry
- 添加自定义指标和维度
- 实现分布式追踪上下文传播
- 支持多种导出器（Jaeger、Zipkin、Prometheus）

## 📊 实施计划

### Phase 1 - 当前进行中 (Current)
- [x] 更新 .cursorrules 文档
- [ ] 实现统一父子订阅机制
- [ ] 改进事件去重机制

### Phase 2 - 下一阶段 (Next)
- [ ] 评估 Source Generator 方案
- [ ] 增强 Stream 抽象层
- [ ] 完成 TODO 项功能

### Phase 3 - 未来计划 (Future)
- [ ] 事件处理管道化
- [ ] AI Agent 接口设计
- [ ] 批处理优化
- [ ] 可视化和调试工具

## 📝 注意事项

1. **向后兼容性**：所有优化都应保持API的向后兼容
2. **渐进式改进**：优先解决影响最大的问题
3. **性能基准**：每个优化都应有性能测试验证
4. **文档更新**：每个功能都需要相应的文档和示例

## 🔗 相关文档

- [Framework Architecture](./ARCHITECTURE.md)
- [Development Rules](./.cursorrules)
- [Stream Architecture](./docs/STREAM_ARCHITECTURE.md)

---

*Last Updated: 2025-01-05*
*Status: Active Development*

