# Aevatar 框架 MAKER 架构设计

**状态:** Alpha / 活跃开发中
**基于:** [MAKER: Solving a Million-Step LLM Task with Zero Errors](https://arxiv.org/html/2511.09030v1)
**实现版本:** Aevatar.Agents.Maker (v0.1.0)

---

## 1. 理论背景：MAKER 范式

### 1.1 规模化难题 (The Scaling Problem)
大型语言模型（LLM）展现了惊人的推理能力，但在处理长程任务（Long-horizon tasks）时，它们面临一个致命的限制：**错误累积（Compounding Errors）**。

如果一个模型每一步的成功率为 99%（这已经非常高了），那么连续完成 100 个依赖步骤的任务成功率仅为 $0.99^{100} \approx 36.6\%$。对于一个需要 1,000 步的任务，成功率在数学上几乎为零（$< 0.005\%$）。

传统的解决思路是试图通过微调或强化学习来提高基础模型的“智商”（即提高单步成功率）。然而，MAKER（Massively Decomposed Agentic Processes，大规模分解智能体流程）论文提出了一种正交的解决思路：**通过架构设计来提高可靠性**，而不是仅仅依赖模型本身的能力。

### 1.2 MAKER 的核心三支柱

MAKER 系统通过以下三个核心支柱，旨在实现百万步级别的“零错误”执行：

#### 1. 最大化智能体分解 (Maximal Agentic Decomposition)
系统不再试图让一个“全能 Agent”去解决整个问题，而是将任务递归分解，直到达到**原子级别（Atomic Level）**。

-   **深度分解**：将复杂任务分解为“打地基”、“倒水泥”……直到“拧紧这颗螺丝”。
-   **递归性（Recursion）**：每一个子任务都被视为一个新的独立任务，系统会评估它是否足够简单。如果不够简单，就继续分解。
-   **原子性（Atomicity）**：当一个任务简单到 LLM 有极高信心（High Confidence）在一次推理中解决它时，该任务被视为原子任务。

#### 2. 领先 K 票共识机制 (First-to-ahead-by-K Voting)
为了对抗 LLM 的随机性错误（Stochastic Errors / Hallucinations），系统绝不依赖单次生成的答案。

-   **多重采样（Sampling）**：对于每一个决策，系统会并行生成 $N$ 个独立的提案。
-   **共识规则**：只有当排名第一的方案的票数比排名第二的方案多出 $K$ 票时，才采纳该方案。
-   **缩放定律（Scaling Law）**：论文证明，只要基础模型的准确率优于随机猜测，随着 $K$ 值的增加，最终错误率呈指数级下降。

#### 3. 红旗预警 (Red-Flagging)
有时候模型会犯**相关性错误（Correlated Errors）**。为了防止这种情况：

-   **自我监控**：Agent 具备检测失败迹象的能力（如无法达成共识、步骤无效）。
-   **熔断与回滚**：当“红旗”升起，系统会停止当前分支，向上级汇报。

---

## 2. Aevatar 实现架构

我们将 MAKER 的概念映射到 Aevatar Agent Framework 的 Actor 模型中。

### 2.1 系统概览

系统由两种主要的 Agent 类型组成：

1.  **`MakerTaskAgent` (管理者/大脑)**：有状态（Stateful）。代表任务树中的一个节点。负责生命周期管理、状态维护、共识逻辑以及递归的子任务分发。
2.  **`MakerWorkerAgent` (工作者/手脚)**：封装了 LLM API。负责原始生成（Thinking）和评估。
3.  **`IMakerChildLinker` (连接器)**: 负责动态创建和链接子 Agent 的基础设施接口。

### 2.2 Agent 设计详情

#### A. MakerTaskAgent (任务节点)

这是特定子任务的“前额叶”。

*   **基类**: `AIGAgentBase<TaskAgentState, TaskAgentConfig>`
*   **核心逻辑**:
    *   **状态机**: `Created → AssessingComplexity → WaitingForProposals → ExecutingChildren → Completed/Failed`。
    *   **票箱管理**: 维护 Worker 提案的计数（`vote_tallies`）。
    *   **微步模式 (Micro-Steps)**: *Aevatar 扩展功能*。允许将原子任务进一步细分为一系列微小的问答步骤，以提高精确度。
    *   **Self-Worker**: *Aevatar 扩展功能*。在资源受限或配置允许时，TaskAgent 可直接调用 LLM 生成提案，作为备用 Worker。

#### B. MakerWorkerAgent (计算单元)

这是“计算单元”。在生产系统中，这通常是一个共享 Token 桶的 Agent 池。

*   **基类**: `AIGAgentBase<WorkerAgentState, WorkerAgentConfig>`
*   **角色**:
    *   **Decomposer**: 输出 JSON 格式的子任务列表。
    *   **Solver**: 输出直接答案。
    *   **Reviewer**: (规划中) 比较两个答案是否语义等价。

### 2.3 协议定义 (Protobuf)

> **注意**: 以下定义反映了当前代码库的实际实现。

```protobuf
syntax = "proto3";
option csharp_namespace = "Aevatar.Agents.Maker";

import "google/protobuf/timestamp.proto";

message TaskAgentState {
    string task_id = 1;
    string parent_id = 2;
    string original_goal = 3;

    enum Phase {
        PHASE_CREATED = 0;
        PHASE_ASSESSING_COMPLEXITY = 1;
        PHASE_WAITING_FOR_PROPOSALS = 2;
        PHASE_EXECUTING_CHILDREN = 3;
        PHASE_COMPLETED = 4;
        PHASE_FAILED = 5;
    }
    Phase phase = 4;

    // 投票机制: Key=内容哈希, Value=票数
    map<string, int32> vote_tallies = 5;
    // Key=内容哈希, Value=完整内容
    map<string, string> candidate_content = 6;

    // 递归结构
    repeated string child_agent_ids = 7;
    map<string, string> child_results = 8;
    
    string final_result = 9;
    int32 red_flag_count = 10;
    int32 current_depth = 11;
    string active_request_id = 12;
    string preferred_plan_hash = 13;
    string failure_reason = 14;

    message PlannedStep {
        string step_id = 1;
        string description = 2;
    }
    repeated PlannedStep planned_steps = 15;
    repeated string pending_child_ids = 16;

    enum GenerationRequestType {
        GENERATION_REQUEST_TYPE_NONE = 0;
        GENERATION_REQUEST_TYPE_DECOMPOSITION = 1;
        GENERATION_REQUEST_TYPE_ATOMIC_SOLVE = 2;
    }
    GenerationRequestType active_generation_type = 17;
    int32 proposal_attempts = 18;

    // 微步模式 (Micro-Steps) 状态
    repeated string micro_objectives = 20;
    repeated string micro_summaries = 21;
    int32 micro_cursor = 22;
    bool micro_mode_active = 23;
    string micro_current_objective = 24;
}

message TaskAgentConfig {
    int32 consensus_threshold_k = 1;       // K值
    int32 max_depth = 2;                   // 最大递归深度
    int32 max_attempts = 3;                // 最大重试次数
    string system_prompt_template = 4;
    string provider_name = 5;
    int32 initial_fan_out = 6;             // 每次请求的 Worker 数量
    int32 max_candidate_wait = 7;          // 最大等待票数
    repeated string worker_stop_sequences = 8;
    int32 worker_response_token_limit = 9;
    bool enforce_micro_steps = 10;         // 强制开启微步模式
    bool enable_self_worker = 11;          // 允许自身作为 Worker
    bool auto_link_children = 12;          // 自动链接子 Agent
    int32 child_pool_size = 13;            // 子 Agent 池大小
}

// 事件定义
message AssignTaskEvent {
    string task_id = 1;
    string goal_description = 2;
    int32 current_depth = 3;
    map<string, string> context_variables = 4;
}

message GenerateProposalEvent {
    string request_id = 1;
    string task_description = 2;
    enum GenerationType {
        GENERATION_TYPE_DECOMPOSITION = 0;
        GENERATION_TYPE_ATOMIC_SOLVE = 1;
    }
    GenerationType type = 3;
    int32 max_output_tokens = 4;
    repeated string stop_sequences = 5;
    string stage_hint = 6;
}

message ProposalReceivedEvent {
    string request_id = 1;
    string content = 2;
    string reasoning_trace = 3;
}
```

---

## 3. 核心工作流实现

### 3.1 共识循环 (The Consensus Loop)

当前实现采用**字符串哈希**作为共识基础，暂未引入语义向量聚类。

1.  **广播**: `TaskAgent` 向 Worker 发布 `GenerateProposalEvent`。
    *   `InitialFanOut` 控制初始并发请求数。
    *   如果启用 `EnableSelfWorker`，`TaskAgent` 也会自行生成一个提案。
2.  **累积与标准化**:
    *   收到 `ProposalReceivedEvent`。
    *   **标准化 (Canonicalization)**: 执行简单的空白字符清理（Trim/Regex）。
    *   计算 SHA256 哈希并计入 `vote_tallies`。
3.  **检查共识**:
    *   逻辑: `LeaderVotes - RunnerUpVotes >= K`。
    *   如果达成：进入下一阶段（分解或输出结果）。
    *   如果未达成且票数过多：触发重试或红旗。
4.  **红旗 (Red Flag)**:
    *   触发条件: 
        *   超过 `MaxAttempts` 仍未达成共识。
        *   分解计划格式解析失败（非有效 JSON）。
        *   子任务全部执行完毕但结果无效（逻辑待增强）。

### 3.2 递归与执行

1.  **分解解析**: Worker 返回 JSON 数组 `[{"step_id": "S1", "description": "..."}]`。
2.  **子 Agent 孵化**:
    *   通过 `IMakerChildLinker` 确保子 Agent (Actor) 已在运行时中激活并链接。
    *   父 Agent 记录 `child_agent_ids`。
3.  **并行/串行执行**:
    *   当前实现支持**并行分发**所有子任务（通过 `LaunchChildAssignmentsAsync`）。
    *   父 Agent 等待所有 `PendingChildIds` 完成。
4.  **结果聚合**:
    *   所有子任务完成后，父 Agent 简单聚合结果文本并向上汇报。

### 3.3 微步模式 (Micro-Steps Extension)

为了弥补 LLM 在复杂原子任务上的不稳定性，框架引入了 Micro-Steps 机制：

1.  当任务被判定为“原子”但开启了 `EnforceMicroSteps` 时，Worker 不会直接给出最终答案。
2.  TaskAgent 将任务拆解为一系列微小的交互（Micro Objectives）。
3.  Agent 与 Worker 进行多轮对话，每一轮只解决一个微小的问题点。
4.  最终将多轮对话的摘要聚合成最终结果。

---

## 4. 差距分析与路线图

当前实现 (`Aevatar.Agents.Maker`) 与论文 (`2511.09030v1`) 的主要差距及改进计划：

| 功能特性 | 论文描述 | 当前实现 | 改进计划 |
| :--- | :--- | :--- | :--- |
| **语义等价性** | 聚类语义相同的答案 ("42" == "42.0") | **字符串哈希** (仅去除空格) | 引入 Embedding 比较或 LLM-Reviewer 角色 |
| **红旗检测** | 检测循环逻辑、相关性错误 | **基于计数** (超时/重试次数) | 增加对历史提案的语义分析以检测循环 |
| **Worker 池化** | 无状态的计算资源池 | **独立 Agent** (WorkerAgent) | 优化 `MakerWorkerAgent` 的复用机制 |
| **上下文传递** | 复杂的上下文变量传递 | **基础 Map** (`context_variables`) | 增强上下文管理，支持更丰富的数据结构 |

## 5. 开发指南

### 如何运行 Demo
参考 `examples/MakerBaziDemo`，该示例展示了如何配置 Maker Agent 来执行一个具体的命理分析任务（八字排盘），演示了分解和递归调用的全过程。

### 自定义 Worker
可以通过实现新的 `MakerWorkerAgent` 或配置不同的 LLM Provider (如 DeepSeek, OpenAI) 来改变 Worker 的推理能力。

---

> **设计哲学**: Aevatar 的 Maker 实现追求"实用主义"。我们首先保证核心的 Actor 递归结构和投票机制稳定运行（骨架），然后逐步丰富语义检测和智能监控逻辑（血肉）。
