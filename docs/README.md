# Aevatar Agent Framework - 文档导航

## 🌌 文档结构

欢迎来到 Aevatar Agent Framework 文档。本目录包含5个核心文档，涵盖框架的所有重要方面。

---

## 📚 核心文档

### 1. [CORE_CONCEPTS.md](CORE_CONCEPTS.md) ⭐ 必读
**核心概念：序列化、Stream、事件传播**

涵盖内容：
- Protocol Buffers 序列化规则（强制要求）
- Stream 架构和订阅机制
- 事件传播方向（Up/Down/Both）
- 类型过滤和性能优化
- 实战示例和最佳实践

**适合**: 所有开发者必读，理解框架基础

---

### 2. [EVENTSOURCING.md](EVENTSOURCING.md)
**EventSourcing 完整指南**

涵盖内容：
- EventSourcing 架构设计
- 快速开始指南
- MongoDB 和 InMemory 实现
- 事件生命周期和状态重建
- 快照、版本控制和性能优化

**适合**: 需要使用EventSourcing特性的开发者

---

### 3. [AI_INTEGRATION.md](AI_INTEGRATION.md)
**AI 能力集成指南**

涵盖内容：
- **AI Agent 3级架构**（AIGAgentBase → AIGAgentWithToolBase → AIGAgentWithProcessStrategy）
- Microsoft.Extensions.AI 集成
- **LLMProviderFactory** 配置（Azure OpenAI、OpenAI、Ollama）
- **IAevatarTool** 工具系统开发
- **LLM元推理**策略选择（Chain-of-Thought、ReAct、Tree-of-Thoughts）
- 对话历史管理和流式响应

**适合**: 需要构建AI智能体的开发者

**架构**：
```
Level 1: AIGAgentBase                    ← 基础聊天能力
Level 2: AIGAgentWithToolBase            ← 增加工具调用
Level 3: AIGAgentWithProcessStrategy     ← 增加策略选择
```

---

### 4. [RUNTIME_GUIDE.md](RUNTIME_GUIDE.md)
**运行时选择和切换**

涵盖内容：
- Local、Orleans、ProtoActor 对比
- 运行时选择指南
- DI 配置示例（快速切换）
- 性能基准对比
- 迁移策略

**适合**: 需要选择或切换Runtime的开发者

---

### 5. [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md)
**开发者高级指南**

涵盖内容：
- IGAgentActorManager 详解
- 订阅管理器实现细节
- 可观测性和监控
- Orleans/ProtoActor 特定功能
- 扩展点和自定义实现
- 高级模式（Supervisor、Aggregator、Saga）

**适合**: 需要深入理解框架内部机制或扩展框架的开发者

---

## 📖 阅读顺序建议

### 新手入门（必读）
1. ✅ [CORE_CONCEPTS.md](CORE_CONCEPTS.md) - 理解Protobuf、Stream、事件
2. ✅ `../README.md` - 框架概览和快速开始
3. ✅ `../examples/SimpleDemo/` - 运行第一个示例

### 进阶开发（推荐）
4. ✅ [RUNTIME_GUIDE.md](RUNTIME_GUIDE.md) - 了解运行时选择
5. ✅ [EVENTSOURCING.md](EVENTSOURCING.md) - 如果需要事件溯源
6. ✅ [AI_INTEGRATION.md](AI_INTEGRATION.md) - 如果需要AI能力

### 高级开发（可选）
7. ✅ [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md) - 深入内部机制
8. ✅ `../ARCHITECTURE.md` - 完整架构文档
9. ✅ `../CONSTITUTION.md` - 设计哲学

---

## 🎯 快速查找

### 我想...

- **理解Protobuf为什么是强制的** → [CORE_CONCEPTS.md](CORE_CONCEPTS.md) § 序列化规则
- **搞懂UP/DOWN事件方向** → [CORE_CONCEPTS.md](CORE_CONCEPTS.md) § 事件传播方向
- **实现父子Agent协作** → [CORE_CONCEPTS.md](CORE_CONCEPTS.md) § 实战示例
- **使用EventSourcing** → [EVENTSOURCING.md](EVENTSOURCING.md) § 快速开始
- **集成OpenAI** → [AI_INTEGRATION.md](AI_INTEGRATION.md) § 快速开始
- **选择Local还是Orleans** → [RUNTIME_GUIDE.md](RUNTIME_GUIDE.md) § Runtime选择指南
- **扩展框架** → [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md) § 扩展点
- **监控Agent** → [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md) § 可观测性

---

## 🔄 文档维护

### 何时更新文档

- ✅ 添加新功能时
- ✅ API变更时
- ✅ 发现文档错误时
- ❌ 临时变更（这些应该记录在Git commit或PR中）

### 文档更新流程

1. 确定影响的文档（通常是1-2个）
2. 更新相关章节
3. 更新日期和版本号
4. 确保示例代码可运行

### 避免文档膨胀

- ❌ 不要创建临时变更记录文档
- ❌ 不要重复已有文档的内容
- ✅ 使用Git commit message记录变更
- ✅ 使用PR描述记录重构过程
- ✅ 重大变更记录在CHANGELOG（如有）

---

## 🌊 共振之语

*文档不是为了完整，而是为了有用。*  
*越少的文档，越高的价值。*  
*过时的文档比没有文档更危险。*

**5个精准的文档，胜过27个混乱的记录。** 🌌

---

**Last Updated**: 2025-11-17
**Document Version**: 3.0 (3-Level AI Agent Architecture)

