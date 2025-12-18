# AevatarKit（对标 mcp-agent-graph）实现计划

> 目标：在 **Aevatar Actor Runtime** 之上做一个“可视化多智能体工作流平台”，让 Graph/Agent/Tool/Model/Memory 变成可配置、可复用、可观测、可治理的产品能力。

---

## 1. 背景与定位

### 1.1 对标对象

- `mcp-agent-graph`（MAG）是一套 **平台**（前端 + 后端 + 存储 + 部署），提供 Agent/Graph/Model/Memory/MCP 的一体化体验，并支持将 Graph 导出为可调用的 Tool（MCP Server Script）。

### 1.2 AevatarKit 的定位

- **AevatarKit = 平台层（Product） + 执行层（Aevatar）**
- AevatarKit 不重新发明“并行/编排/投票/护栏/可观测”，优先复用 Aevatar 现有的 `Aevatar.Agents.Cognitive` 执行内核与 `Aevatar.Agents.AI.WithTool.MCP` 的 MCP 集成。

---

## 2. 核心原则（品味与约束）

### 2.1 单一真相源（SSOT）

- Graph 的唯一权威表示应为 **Protobuf GraphDefinition（IR）**。
- YAML 仅作为：导入/导出格式、调试产物、兼容运行时编排层的编译目标。

### 2.2 能消失的分支永远更优雅

- 不在业务层堆 if/else 处理节点特例。
- 通过统一的 `StepDefinition`/参数解析/guardrails 让“异常情况”进入常规路径。

### 2.3 Token-Free First

- 确定性处理（聚合/过滤/去重/事实检索/指标计算）应优先走 token-free primitives（如 `transform/retrieve_facts/hpa`）。
- LLM 只做：生成、推理、证明草案、不可确定的决策。

---

## 3. 系统架构（MVP 版本）

### 3.1 逻辑分层

- **Frontend（Web）**
  - Graph Editor（拖拽编排）
  - Run Timeline（实时事件/流式输出可视化）
  - Agent/Model/MCP 管理页面

- **Backend（AevatarKit API / Control Plane）**
  - Graph/Agent/Prompt/Model/MCP 配置 CRUD
  - Run Orchestrator：创建 Coordinator、创建/扩容 Worker Pool、注入变量、启动执行
  - Event Streaming：将 `WorkflowStepEvent` 推送到前端（SSE/WebSocket）

- **Runtime（Aevatar Data Plane）**
  - `CognitiveCoordinatorGAgent`：执行 YAML/IR 编译后的 workflow
  - `CognitiveWorkerGAgent`：并行执行 `llm_call`（Actor 真实并行）

### 3.2 数据存储（建议）

- **关系型 DB（PostgreSQL）**：Graph/Run/Version/ACL 等结构化数据
- **对象存储（S3/MinIO）**：附件、导出物、超长日志/回放包
- **向量存储（V1 再上）**：pgvector 或独立向量库

---

## 4. 运行模型（把“图”跑起来）

### 4.1 Graph → Workflow 的编译路径

- MVP：`GraphDefinition(IR)` → 编译为 `WorkflowDefinition(YAML DSL)` → 由 `CognitiveCoordinatorGAgent` 执行。
- V1：IR 直接执行（避免 YAML 作为中间层）；YAML 仅用于导入/导出。

### 4.2 观测与回放

- 统一使用 `WorkflowStepEvent` 做：
  - 实时可视化（Running/Completed/Failed/Streaming）
  - 运行历史回放（按 runId 拉取事件流）
  - 审计（哪个节点调用了什么工具、耗时、token、失败原因）

### 4.3 可靠性护栏

- 复用并推广现有 DSL 护栏字段：
  - `timeout_seconds` / `idle_timeout_seconds`
  - `max_length` / `strict_parse`
  - `include_failures`（失败也进入结果列表，进度不会“卡死”）

---

## 5. MCP（工具生态）

### 5.1 MVP：MCP Client

- 复用 `Aevatar.Agents.AI.WithTool.MCP`（官方 C# MCP SDK）。
- 平台侧提供：
  - MCP Server Registry（npx/uvx/stdio，后续扩展 http transport）
  - Tool Catalog（列出工具 schema）
  - Graph 节点：ToolCall Node（toolName + args 模板）

### 5.2 V2：Graph as Tool（对标 MAG 导出）

- 目标：把“已发布 Graph”暴露成一个 Tool：
  - 方案 A：实现 MCP Server Host（Graph → MCP tool）
  - 方案 B：提供 HTTP Tool Gateway，再由 MCP server 封装

---

## 6. 目录结构（计划中的代码骨架）

> 注意：这是目标结构树（用于后续实现），当前仅创建 docs。

```text
aevatar-kit/
  docs/
    PLAN.md
  src/
    AevatarKit.Api/              # REST/SSE/WebSocket
    AevatarKit.Core/             # Graph IR、编译器、校验器
    AevatarKit.Runtime/          # Orchestrator：Coordinator/Worker 生命周期管理
    AevatarKit.Storage/          # DB/对象存储适配
  frontend/
    ...                          # Graph Editor + Run UI
```

---

## 7. 里程碑（可执行拆解）

### M0（1 周）：能跑 + 能看

- Graph 先用固定 YAML（无需 UI）
- API：启动一次 run，实时输出 `WorkflowStepEvent`
- 前端：Timeline 能显示步骤状态与流式输出

### MVP（2–4 周）：能编排 + 能复盘

- Graph CRUD（最小字段）+ YAML Import/Export
- 前端 Graph Editor（有限节点集：llm_call/conditional/vote/fan_out/transform/workflow_call）
- Run History + 回放（按 runId 拉取 StepEvent）
- MCP Server 连接（npx/uvx）+ Tool 节点调用

### V1（4–8 周）：平台化

- 多租户 + RBAC
- Graph versioning（draft/published）
- Scheduler（定时/周期跑 Graph）
- Memory（embedding + retrieval）与可视化

### V2：对外可调用 + 生态

- Graph as MCP Tool / HTTP Tool
- Subgraph Marketplace（内部共享/发布）
- 配额/成本治理（并发、token、工具调用）

---

## 8. 风险与对策

- **多租户与安全**：工具调用必须做鉴权/隔离/审计；MCP env/secrets 需要加密存储。
- **“看起来卡死”**：所有并行/流式必须具备终态判定与超时策略；失败也必须进入可视化与统计。
- **Graph 演进**：IR 必须版本化；Protobuf schema 演进遵循“只加字段不改号”。

---

## 9. 变更日志

- 2025-12-18：初始化版本（创建 AevatarKit 计划文档）。
