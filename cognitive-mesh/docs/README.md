# Cognitive Mesh (Aevatar Nexus)

> **目录状态**: 实验性架构设计
> **最后更新**: 2025-12-01
> **维护者**: Aevatar Core Team

## 📚 文档索引

本目录包含 **Cognitive Mesh (认知网格)** 的设计规范，这是一个用于定义复杂、可靠且具有创造性的 LLM 推理拓扑的平台。

| 文件 | 描述 |
|------|-------------|
| **[CONCEPT.md](./CONCEPT.md)** | **哲学 (The Philosophy)**。为何 "工作流" 是错误的思维模型，以及 "Cognitive Mesh" 如何实现 *思维宇宙 (Universe of Thoughts)*。 |
| **[PRD.md](./PRD.md)** | **需求 (The Requirements)**。用户故事、功能规格、用户旅程与服务矩阵。 |
| **[ARCHITECTURE.md](./ARCHITECTURE.md)** | **蓝图 (The Blueprint)**。将概念映射到 Aevatar Agents、Orleans 和事件溯源的技术实现细节。 |
| **[DSL.md](./DSL.md)** | **DSL 规范**。最小 Schema、强类型映射、编译与验证流水线、版本化策略。 |

*For English notes, see inline sections inside each document (当前版本以中文为主)。*

## 文档说明
- **CONCEPT.md**：从现象层、本质层、哲学层三段论拆解“为什么认知网格必须取代传统工作流”，并把 UoT 三大思维模式映射为平台使命。
- **PRD.md**：定义产品愿景、核心用户画像、交互旅程、服务矩阵以及 Orleans/Aspire 的落地承诺。
- **ARCHITECTURE.md**：描述 Orleans + Aevatar 体系下的 Agent 拓扑、事件溯源与数据结构，是工程实现的蓝图。
- **DSL.md**：规定认知网格的指令语言，包括 Schema、强类型映射、编译流水线、版本化与 LLM/DSL 协作策略，是防止系统失控的硬约束。

## 🌳 结构

```text
cognitive-mesh/
├── docs/                         # 本目录：概念、PRD、架构、DSL 规范
├── CognitiveMesh.App/            # 运行在 Actor Runtime 之上的服务骨架
├── CognitiveMesh.AppHost/        # Aspire Host，负责编排 Orleans / 观察性组件
├── dsl/
│   ├── Aevatar.CognitiveMesh.Dsl/        # DSL 编译器库
│   └── Aevatar.CognitiveMesh.Dsl.Tests/  # DSL 编译器测试
└── README.md
```

## ⚙️ 技术评估与改进脉络
- **DSL 编译瓶颈**：`Cognitive Compiler` 需把自然语言映射到严格的 `MeshDefinition`，建议阶段性收敛语法并附带自测套件，防止 prompt-to-JSON 黑盒化。
- **状态爆炸与一致性**：UoT 探索模式会让事件流在 Orleans 集群中指数增长，必须以版本戳 + 冲突检测保证 Event Store 与向量检索共享统一真相源。
- **验证链条的可信度**：Zero-Error Loop 需要可执行断言（Schema 校验、AST 规则、数值约束等）支撑，CriticAgent 不应只依赖 LLM 复核。
- **数据泥团征兆**：当前 `MeshDefinition` 将目标/拓扑/预算等全塞入单一对象，后续要支持子 Mesh 或复用会非常痛苦，需拆分为细粒度规格对象。

### 下一步建议
1. **限定 DSL 语法**：定义最小可用的指令集与 Schema，并提供编译器的回归测试。
2. **状态哨兵**：在事件溯源层实现并发冲突检测，避免分支合并产生幽灵状态。
3. **增强验证器**：为 CriticAgent 引入可编程判定或外部断言引擎，让“零错误”不只停留在 LLM 互审。
4. **拆分配置模型**：以 `GoalSpec`、`TopologySpec`、`BudgetSpec` 等对象替代单块配置，降低耦合与序列化复杂度。

## 🔗 核心参考
*   [Universe of Thoughts: Enabling Creative Reasoning with LLMs](https://arxiv.org/html/2511.20471v2)
*   [Solving a Million-Step LLM Task with Zero Errors](https://arxiv.org/html/2511.09030v1)

