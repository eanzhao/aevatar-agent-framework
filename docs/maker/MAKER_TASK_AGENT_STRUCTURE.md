# MakerTaskAgent 结构拆分说明

```
agents/Aevatar.Agents.Maker/
├── MakerTaskAgent.cs                     # 核心字段、生命周期与配置
├── MakerConsensusAgent.cs                # 专职投票/共识的子 Agent
├── MakerTaskAgent/
│   ├── Context.cs                        # 继承上下文、子任务上下文构建
│   ├── EventHandlers.cs                  # 所有 EventHandler 入口
│   ├── JsonElementExtensions.cs          # JSON 工具扩展
│   ├── ConsensusIntegration.cs           # 与 MakerConsensusAgent 的桥接逻辑
│   ├── MicroMode.cs                      # Micro 模式与摘要去重
│   ├── Orchestration.cs                  # 投票、协作、子任务调度
│   ├── SelfWorker.cs                     # 自旋工人提示与初始化
│   └── Utilities.cs                      # 计划解析、哈希与摘要工具
```

## 分层目的
- **核心文件 < 200 行**：仅保留构造、激活、配置默认值及描述，避免“上帝类”失控。
- **职责颗粒化**：事件处理、微任务、上下文快照、子任务分发、自旋工人等流程分离，便于并行演进。
- **可测试性**：各 partial 可单独注入依赖，未来编写针对性单元测试不再需要加载 1500 行巨石。

## 设计原则
- **单向依赖**：`EventHandlers` 调用 `Orchestration/MicroMode`，后者只依赖核心状态，防止循环引用。
- **状态集中**：核心任务状态仍驻留在主文件；共识相关状态迁移至 `TaskConsensusState`，由 `MakerConsensusAgent` 自行管理。
- **最少分支**：拆分后便于识别需要进一步消除的多分支代码段（如共识与 Micro 交叉流程）。

## 后续建议
1. 为 `MakerConsensusAgent` 编写独立测试用例，验证语义聚类与失败策略；继续覆盖 `MicroMode`/`Orchestration`。
2. 进一步抽象 `SelfWorker` 配置，允许注入不同 LLM provider，而非共享主 Agent 配置。
3. 将 `Utilities` 中的 JSON 解析逻辑下沉为可复用组件，减少其它模块重复解析。 
