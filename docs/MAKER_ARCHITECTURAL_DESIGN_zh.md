# Aevatar 框架 MAKER 架构设计

**状态:** 草稿
**基于:** [MAKER: Solving a Million-Step LLM Task with Zero Errors](https://arxiv.org/html/2511.09030v1)
**目标框架:** Aevatar Agent Framework

---

## 1. 理论背景：MAKER 范式详解

### 1.1 规模化难题 (The Scaling Problem)
大型语言模型（LLM）展现了惊人的推理能力，但在处理长程任务（Long-horizon tasks）时，它们面临一个致命的限制：**错误累积（Compounding Errors）**。

如果一个模型每一步的成功率为 99%（这已经非常高了），那么连续完成 100 个依赖步骤的任务成功率仅为 $0.99^{100} \approx 36.6\%$。对于一个需要 1,000 步的任务，成功率在数学上几乎为零（$< 0.005\%$）。

传统的解决思路是试图通过微调或强化学习来提高基础模型的“智商”（即提高单步成功率）。然而，MAKER（Massively Decomposed Agentic Processes，大规模分解智能体流程）论文提出了一种正交的解决思路：**通过架构设计来提高可靠性**，而不是仅仅依赖模型本身的能力。

### 1.2 MAKER 的核心三支柱

MAKER 系统通过以下三个核心支柱，旨在实现百万步级别的“零错误”执行：

#### 1. 最大化智能体分解 (Maximal Agentic Decomposition)
系统不再试图让一个“全能 Agent”去解决整个问题，而是将任务递归分解，直到达到**原子级别（Atomic Level）**。

-   **深度分解**：这就好比把建造一座摩天大楼的任务，分解为“打地基”、“倒水泥”……直到“拧紧这颗螺丝”。
-   **递归性（Recursion）**：每一个子任务都被视为一个新的独立任务，系统会评估它是否足够简单。如果不够简单，就继续分解。
-   **原子性（Atomicity）**：当一个任务简单到 LLM 有极高信心（High Confidence）在一次推理中解决它时，该任务被视为原子任务。
-   **去拟人化**：MAKER 提倡 Agent 的角色微型化（Micro-roles），避免让 Agent 扮演复杂的“人类角色”，而是让它们像流水线上的专用机器一样运作。

#### 2. 领先 K 票共识机制 (First-to-ahead-by-K Voting)
为了对抗 LLM 的随机性错误（Stochastic Errors / Hallucinations），系统绝不依赖单次生成的答案。

-   **多重采样（Sampling）**：对于每一个决策（无论是“如何分解任务”还是“原子任务的答案是什么”），系统都会并行生成 $N$ 个独立的提案。
-   **语义聚类**：系统会自动识别语义相同的答案（例如 "42" 和 "42.0" 归为一类）。
-   **共识规则**：只有当排名第一的方案的票数比排名第二的方案多出 $K$ 票时，才采纳该方案。
    -   例如 $K=3$：如果方案 A 得 5 票，方案 B 得 2 票（5-2=3），则 A 胜出。
    -   如果方案 A 得 4 票，方案 B 得 3 票（4-3=1），则继续采样，直到满足差值。
-   **缩放定律（Scaling Law）**：论文证明，只要基础模型的准确率优于随机猜测，随着 $K$ 值的增加，最终错误率呈指数级下降。这就是实现“百万步零错误”的数学基础。

#### 3. 红旗预警 (Red-Flagging)
有时候模型会犯**相关性错误（Correlated Errors）**（即所有模型都犯同样的错）。为了防止这种情况：

-   **自我监控**：Agent 具备检测失败迹象的能力，例如：
    -   循环逻辑（Circular Logic）：反复提出相同的分解方案。
    -   过量消耗（Excessive Token Usage）：步数超出预期。
    -   无法达成共识：采样了 100 次仍无法满足 $K$ 值差距。
-   **熔断与回滚**：当“红旗”升起，系统会停止当前分支，向上级汇报。上级可以采取替代策略、回滚状态或请求人工干预。

---

## 2. Aevatar 实现架构

我们将 MAKER 的概念映射到 Aevatar Agent Framework 的 Actor 模型中。此实现利用 Aevatar 的分布式特性（Orleans/Proto.Actor）来处理投票和递归所需的大规模并发。

### 2.1 系统概览

系统由两种主要的 Agent 类型组成：

1.  **`MakerTaskAgent` (管理者/大脑)**：有状态（Stateful）。代表任务树中的一个节点。负责生命周期管理、状态维护和共识逻辑。它不直接“思考”，而是指挥 Worker 思考。
2.  **`MakerWorkerAgent` (工作者/手脚)**：无状态（Stateless，概念上）。封装了 LLM API。负责原始生成（Thinking）和评估。

### 2.2 Agent 设计详情

#### A. MakerTaskAgent (任务节点)

这是特定子任务的“前额叶”。

*   **基类**: `AIGAgentBase<TaskAgentState, TaskAgentConfig>`
*   **职责**:
    *   **状态机**: 跟踪任务处于 `ASSESSING`（评估）、`DECOMPOSING`（分解）、`VOTING`（投票）还是 `COMPLETED`（完成）阶段。
    *   **票箱管理**: 维护从 Worker 收到的提案的持久化计数。
    *   **递归管理**: 如果选择分解，则通过 `GAgentFactory` 孵化并监督子 `MakerTaskAgent`。
    *   **流中枢**: 向 Worker 发布任务（Down stream），并向上级汇报结果（Up stream）。

#### B. MakerWorkerAgent (计算单元)

这是“计算单元”。在生产系统中，这可以是一个共享 Token 桶的 Agent 池。

*   **基类**: `AIGAgentBase<WorkerAgentState, WorkerAgentConfig>`
*   **职责**:
    *   **分解者角色 (Decomposer)**: 给定任务描述，输出 JSON 格式的子任务列表。
    *   **解题者角色 (Solver)**: 给定原子任务，输出直接答案。
    *   **评审者角色 (Reviewer)**: (可选) 比较两个答案是否语义等价（用于辅助投票聚合）。

### 2.3 协议定义 (Protobuf)

遵循框架的 "Iron Law of Protobuf"，所有数据结构均在 `.proto` 文件中定义。

```protobuf
syntax = "proto3";
package aevatar.agents.maker;

import "google/protobuf/timestamp.proto";

// --- 状态定义 ---

message TaskAgentState {
    string task_id = 1;
    string parent_id = 2;
    string original_goal = 3;
    
    enum Phase {
        CREATED = 0;
        ASSESSING_COMPLEXITY = 1; // 决策中：分解还是直接解决？
        WAITING_FOR_PROPOSALS = 2; // 投票中：收集 Worker 的方案
        EXECUTING_CHILDREN = 3;    // 等待子 Agent 完成
        COMPLETED = 4;
        FAILED = 5;
    }
    Phase phase = 4;

    // 投票机制
    // Key: 内容哈希, Value: 票数
    map<string, int32> vote_tallies = 5;
    // Key: 内容哈希, Value: 完整文本内容
    map<string, string> candidate_content = 6;
    
    // 递归结构
    repeated string child_agent_ids = 7;
    map<string, string> child_results = 8; // child_id -> result
    
    string final_result = 9;
    int32 red_flag_count = 10;
}

message TaskAgentConfig {
    // First-to-ahead-by-K 中的 "K" 值
    int32 consensus_threshold_k = 1;
    // 最大递归深度
    int32 max_depth = 2;
    // 触发红旗前的最大尝试次数
    int32 max_attempts = 3;
    
    string system_prompt_template = 4;
}

// --- 事件定义 ---

// 命令: Parent -> Child 或 User -> Root
message AssignTaskEvent {
    string task_id = 1;
    string goal_description = 2;
    int32 current_depth = 3;
    map<string, string> context_variables = 4;
}

// 请求: TaskAgent -> Worker
message GenerateProposalEvent {
    string request_id = 1;
    string task_description = 2;
    enum GenerationType {
        DECOMPOSITION = 0; // 提议一个分解计划
        ATOMIC_SOLVE = 1;  // 提议一个直接答案
    }
    GenerationType type = 3;
}

// 响应: Worker -> TaskAgent
message ProposalReceivedEvent {
    string request_id = 1;
    string content = 2; // 计划或答案
    string reasoning_trace = 3;
}

// 结果: Child -> Parent
message TaskOutcomeEvent {
    string task_id = 1;
    bool success = 2;
    string result_data = 3;
    string failure_reason = 4;
}
```

---

## 3. 运行工作流 (Operational Workflows)

### 3.1 共识循环 (The Consensus Loop)

对于任何认知步骤（无论是“如何分解”还是“答案是什么”），`MakerTaskAgent` 都会执行此循环：

1.  **广播 (Broadcast)**: 向 Worker 池的流发布 `GenerateProposalEvent`。
2.  **累积 (Accumulate)**: 监听 `ProposalReceivedEvent`。
    *   收到后，计算 `content` 的哈希值（需标准化空格/格式）。
    *   增加对应哈希的 `vote_tallies` 计数。
3.  **检查共识 (Check Consensus)**:
    *   按票数对候选方案排序。
    *   设 $V_1$ 为第一名的票数，$V_2$ 为第二名的票数。
    *   如果 $V_1 - V_2 \ge K$: **达成共识**。
    *   否则：继续等待或请求更多采样（Back-pressure）。
4.  **红旗 (Red Flag)**: 如果总票数超过限制仍无共识，切换状态至 FAILED 或向父级求助。

### 3.2 递归流程 (The Recursion Flow)

1.  **启动**: `MakerTaskAgent` 对一个 **分解计划**（例如：“步骤1：获取数据，步骤2：处理数据”）达成共识。
2.  **孵化**: 
    *   Agent 使用 `GAgentFactory` 为步骤 1 和步骤 2 创建子 `MakerTaskAgent`。
    *   示例 ID: `parent-step1`, `parent-step2`。
3.  **执行**:
    *   发送 `AssignTaskEvent` 给子 Agent 1。
    *   等待子 Agent 1 的 `TaskOutcomeEvent`。
    *   将子 Agent 1 的结果通过 `context_variables` 传递给子 Agent 2。
    *   发送 `AssignTaskEvent` 给子 Agent 2。
4.  **组合**:
    *   所有子 Agent 完成后，父 Agent 将结果聚合。
    *   父 Agent 发布 `TaskOutcomeEvent`（向上游传播）。

---

## 4. 实施策略

### 第一阶段：基础建设
*   实现 `.proto` 定义。
*   实现 `MakerWorkerAgent`，包含连接 OpenAI/Azure 的简单 Semantic Kernel 逻辑。
*   实现基础的 `MakerTaskAgent`，能接收任务并返回固定字符串（Mock 逻辑）。

### 第二阶段：投票引擎
*   在 `MakerTaskAgent` 中实现 `First-to-ahead-by-K` 逻辑。
*   实现“规范化（Canonicalization）”逻辑（确保 "42" 和 "42.0" 被视为同一票）。

### 第三阶段：递归能力
*   实现将分解计划（JSON）解析为子 Agent 的逻辑。
*   实现 `HandleChildEvent` 逻辑以串联子任务。

### 第四阶段：红旗机制
*   添加检测死循环或停滞状态的逻辑。
*   添加 `HandleRedFlag` 以向上级逐层升级问题。

---

## 5. 为什么 Aevatar 适合实现 MAKER

Aevatar 框架具有实现 MAKER 的天然优势：

1.  **有状态 Actor (Stateful Actors)**：投票机制需要持久化状态（“票箱”），这与 Aevatar 的 `GAgentBase` 完美契合。如果系统崩溃，重启后投票进度不会丢失。
2.  **事件流 (Event Streams)**：向 Worker 池广播“提案请求”是 Aevatar 流（Streams）的原生模式。
3.  **层级寻址 (Hierarchical Addressing)**：Actor 系统内置了父子关系管理，使得导航递归生成的任务树变得非常自然。
4.  **位置透明性**: 无论是运行在本地还是 Orleans 集群，Actor 的通讯方式不变，这允许系统从单机测试无缝扩展到大规模分布式生产环境。

