# MAKER Architecture Design for Aevatar Framework

**Status:** Draft
**Based on:** [MAKER: Solving a Million-Step LLM Task with Zero Errors](https://arxiv.org/html/2511.09030v1)
**Target Framework:** Aevatar Agent Framework

---

## 1. Theoretical Background: The MAKER Paradigm

### 1.1 The Scaling Problem
Large Language Models (LLMs) have demonstrated impressive reasoning capabilities, but they suffer from a critical limitation when applied to long-horizon tasks: **compounding errors**. 

If a model has a 99% success rate per step (which is very high), the probability of successfully completing a task with 100 dependent steps is $0.99^{100} \approx 36.6\%$. For a task with 1,000 steps, success is mathematically impossible ($< 0.005\%$).

Traditional approaches try to improve the base model (making it "smarter"). The MAKER (Massively Decomposed Agentic Processes) paper proposes an orthogonal approach: **improving reliability through architecture**.

### 1.2 Core Components of MAKER

MAKER relies on three pillars to achieve "Zero Error" execution over millions of steps:

#### 1. Maximal Agentic Decomposition
Instead of asking one agent to "solve the whole problem", the system recursively breaks down tasks until they reach an **atomic level**.
- **Decomposition**: A complex task is broken into a sequence of sub-tasks.
- **Recursion**: Each sub-task is treated as a new task and potentially decomposed further.
- **Atomicity**: A task is considered atomic when it is simple enough for the LLM to solve with high confidence in a single inference step.

#### 2. First-to-ahead-by-K Voting (Error Correction)
To combat stochastic errors (random hallucinations), the system never relies on a single generation.
- **Sampling**: For every decision (whether "how to decompose" or "what is the answer"), the system generates multiple independent proposals ($N$).
- **Voting**: These proposals are grouped by semantic similarity.
- **Consensus Rule**: A decision is finalized only when the leading option is ahead of the runner-up by $K$ votes (e.g., 3 votes vs 1 vote).
- **Scaling Law**: The paper proves that as $K$ increases, the probability of error decreases exponentially, provided the base model is better than random guess.

#### 3. Red-Flagging
Sometimes models make **correlated errors** (they are consistently wrong in the same way). 
- Agents are equipped to detect signs of failure (e.g., circular logic, excessive token usage, inability to reach consensus).
- When a "Red Flag" is raised, the system halts that branch and can trigger alternative strategies (backtracking, human intervention, or re-prompting).

---

## 2. Aevatar Implementation Architecture

We translate the MAKER concepts into the Aevatar Agent Framework's actor model. This implementation leverages Aevatar's distributed nature (Orleans/Proto.Actor) to handle the massive concurrency required for voting and recursion.

### 2.1 System Overview

The system is composed of three primary agent types:

1.  **`MakerTaskAgent` (The Manager)**: Stateful. Represents a node in the task tree. Responsible for lifecycle, recursion orchestration, and coordinating other sub-agents.
2.  **`MakerConsensusAgent` (The Arbiter)**: Dedicated vote counter. Listens to `ProposalReceivedEvent`, performs semantic clustering, and emits `ConsensusResultEvent` once the first-to-ahead-by-K rule is satisfied (or fails fast on repeated stalemates).
3.  **`MakerWorkerAgent` (The Worker)**: Stateless (conceptually). A wrapper around the LLM API. Responsible for raw generation (Thinking) and evaluation.

### 2.2 Agent Designs

#### A. MakerTaskAgent

This agent acts as the "frontal lobe" of a specific sub-task. It does not "think" directly; it coordinates workers to think for it.

*   **Base Class**: `AIGAgentBase<TaskAgentState, TaskAgentConfig>`
*   **Responsibilities**:
    *   **State Machine**: Drives the protobuf phases `PHASE_ASSESSING_COMPLEXITY → PHASE_WAITING_FOR_PROPOSALS → PHASE_EXECUTING_CHILDREN/PHASE_COMPLETED/PHASE_FAILED`.
    *   **Vote Counting**: Maintains `vote_tallies`/`candidate_content` inside persisted state.
    *   **Recursion Management**: Emits `AssignTaskEvent` downstream; actual child `MakerTaskAgent` actors must be pre-linked via `ActorHierarchyCoordinator` (or orchestrators provision them ahead of time).
    *   **Stream Hub**: Publishes tasks to workers (Down) and reports results to parents (Up) using `EventDirection`.

#### B. MakerWorkerAgent

This agent acts as the "compute unit". In a production system, this could be a pool of agents sharing a token bucket.

*   **Base Class**: `AIGAgentBase<WorkerAgentState, WorkerAgentConfig>`（backed by `Microsoft.Extensions.AI` providers）
*   **Responsibilities**:
    *   **Decomposer Role**: Given a task description, outputs a JSON list of sub-tasks.
    *   **Solver Role**: Given an atomic task, outputs the direct answer.
    *   **Reviewer Role**: (Optional) Compares two answers to see if they are semantically equivalent (for voting aggregation).

### 2.3 Protocol Definitions (Protobuf)

Adhering to the framework's "Iron Law of Protobuf", all data structures are defined in `.proto` files.

```protobuf
syntax = "proto3";
package aevatar.agents.maker;

import "google/protobuf/timestamp.proto";

// --- State Definitions ---

message TaskAgentState {
    string task_id = 1;
    string parent_id = 2;
    string original_goal = 3;
    
    enum Phase {
        CREATED = 0;
        ASSESSING_COMPLEXITY = 1; // Deciding: Decompose or Solve?
        WAITING_FOR_PROPOSALS = 2; // Gathering votes from Workers
        EXECUTING_CHILDREN = 3;    // Waiting for child agents
        COMPLETED = 4;
        FAILED = 5;
    }
    Phase phase = 4;

    // Voting Mechanism
    // Key: Content Hash, Value: Vote Count
    map<string, int32> vote_tallies = 5;
    // Key: Content Hash, Value: Full Text Content
    map<string, string> candidate_content = 6;
    
    // Recursion
    repeated string child_agent_ids = 7;
    map<string, string> child_results = 8; // child_id -> result
    
    string final_result = 9;
    int32 red_flag_count = 10;
}

message TaskAgentConfig {
    // "K" in First-to-ahead-by-K
    int32 consensus_threshold_k = 1;
    // Maximum depth of recursion tree
    int32 max_depth = 2;
    // Max attempts before Red Flagging
    int32 max_attempts = 3;
    
    string system_prompt_template = 4;
}

// --- Event Definitions ---

// Command: Parent -> Child or User -> Root
message AssignTaskEvent {
    string task_id = 1;
    string goal_description = 2;
    int32 current_depth = 3;
    map<string, string> context_variables = 4;
}

// Request: TaskAgent -> Worker
message GenerateProposalEvent {
    string request_id = 1;
    string task_description = 2;
    enum GenerationType {
        DECOMPOSITION = 0; // Propose a plan
        ATOMIC_SOLVE = 1;  // Propose an answer
    }
    GenerationType type = 3;
}

// Response: Worker -> TaskAgent
message ProposalReceivedEvent {
    string request_id = 1;
    string content = 2; // The plan or the answer
    string reasoning_trace = 3;
}

// Result: Child -> Parent
message TaskOutcomeEvent {
    string task_id = 1;
    bool success = 2;
    string result_data = 3;
    string failure_reason = 4;
}
```

---

## 3. Operational Workflows

### 3.1 The Consensus Loop (Voting)

For any cognitive step (either *how to decompose* or *what is the answer*), the `MakerTaskAgent` executes this loop:

1.  **Broadcast**: Publish `GenerateProposalEvent` to the worker pool stream.
2.  **Accumulate**: Listen for `ProposalReceivedEvent`.
    *   On receipt, compute hash of `content` (normalizing whitespace/formatting).
    *   Increment `vote_tallies[hash]`.
3.  **Check Consensus**:
    *   Sort candidates by vote count.
    *   Let $V_1$ be votes for leader, $V_2$ be votes for runner-up.
    *   If $V_1 - V_2 \ge K$: **Consensus Reached**.
    *   Else: Continue waiting or request more samples.
4.  **Red Flag**: If total votes > Limit and no consensus, switch state to FAILED or ask parent for help.

### 3.2 The Recursion Flow

1.  **Start**: `MakerTaskAgent` reaches consensus on a **Decomposition Plan** (e.g., "Step 1: Fetch Data, Step 2: Process").
2.  **Spawn / Link**: 
    *   The parent publishes `AssignTaskEvent` **Down** and expects additional `MakerTaskAgent` actors to be linked as children (via `ActorHierarchyCoordinator.LinkAsync`).
    *   Orchestrators (e.g., the Bazi demo) are responsible for provisioning those child actors up front; IDs typically follow `parent-step1`, `parent-step2`, etc.
3.  **Execute**:
    *   Send `AssignTaskEvent` to Child 1.
    *   Wait for `TaskOutcomeEvent` from Child 1.
    *   Pass result of Child 1 to Child 2 via `context_variables`.
    *   Send `AssignTaskEvent` to Child 2.
4.  **Compose**:
    *   Once all children are done, the Parent's result is the aggregation of child results.
    *   Parent publishes `TaskOutcomeEvent` (Upstream).

---

## 4. 开发计划

为实现论文级别的 MAKER 能力，开发工作将按阶段推进，每一阶段都有明确目标、核心任务与验收产物。

### 阶段 0：基线梳理（1 周）
- 目标：收敛现有 `MakerTaskAgent`/`MakerWorkerAgent` 行为，补齐文档与配置缺失。
- 任务：
  - 对照 `.proto` 与实现，校准字段含义、默认值、异常路径。
  - 引入集成测试样例，确保现有投票/递归链路可跑通简单任务。
- 产物：基线测试报告 + 稳定可运行的 Demo。

### 阶段 1：递归与任务图增强（2 周）
- 目标：支持“极致分解”所需的动态深度与任务图管理。
- 任务：
  - 引入 `DecompositionPolicy`，允许根据任务特征调整 `MaxDepth`、fan-out、孩子顺序。
  - 为 `TaskAgentState` 增加上下文快照（父节点引用、步骤依赖、检查点）。
  - 实现多阶段 compose：子任务完成后可触发新一轮分解或合成。
- 产物：可视化的任务树追踪工具 + 支撑 >3 层递归的验收用例。

### 阶段 2：投票与裁判流水线（3 周）
- 目标：落地 “first-to-ahead-by-K” 以及论文中的 `N=2^k-1` 多候选策略。
- 任务：
  - 抽象 `VoteSession` 组件，支持流式计票、领先即终止、并发候选补齐。
  - 新增 `SolutionDiscriminatorAgent`、`CompositionDiscriminatorAgent`，专职聚类与语义等价性判定。
  - 在 `MakerWorkerAgent` 中扩展 reviewer 角色，产生投票上下文（置信度、trace embedding）。
- 产物：可配置的投票服务 + 单元测试覆盖领先判定、平票回退等场景。

### 阶段 3：Red-Flagging 与去相关（2 周）
- 目标：把“红旗”从告警升级为可恢复策略，降低相关性错误。
- 任务：
  - 构建 `RedFlagWatcher`，汇总各层信号（失败原因、token 超限、长尾投票）并触发策略。
  - 支持 prompt/model 多样化：同一轮投票可自动切换 provider、temperature、工具链。
  - 定义恢复流程：回退至上层分解、替换子树、或请求人工审查。
- 产物：Red Flag playbook + 回放日志工具，验证可在 3 次失败内恢复。

### 阶段 4：运行时扩展与观测（2 周）
- 目标：支撑百万级步骤运行的可观测性、配额与调度。
- 任务：
  - 增加分布式计数器/指标（步骤编号、投票时长、LLM 成本）。
  - 集成持久化队列或批处理器，保证长链路中断可恢复。
  - 提供运营面板（Orleans Dashboard / Grafana）展示树状进度。
- 产物：监控仪表盘 + 压测报告（展示吞吐与错误率）。

### 阶段 5：验证与工具化（持续）
- 目标：形成可复用的 MAKER 工具集与最佳实践。
- 任务：
  - 创建脚手架 CLI，快速生成 MAKER 任务模板、配置、可视化报告。
  - 引入基准任务（Hanoi、乘法）并自动对比论文数据。
  - 输出开发手册与 runbook，指导业务团队接入。
- 产物：工具包发布 + 文档化指南。

---

## 5. Why This Matters for Aevatar

Aevatar is uniquely positioned to implement MAKER because:
1.  **Stateful Actors**: Voting requires persistent state (the ballot box) which fits `GAgentBase` perfectly.
2.  **Event Streams**: Broadcasting "Requests for Proposals" to a pool of workers is a native pattern in Aevatar streams.
3.  **Hierarchical Addressing**: The parent-child relationship is built into the actor system, making the recursion tree natural to navigate.


