# MakerCoordinatorGAgent 架构设计

## 概述

`MakerCoordinatorGAgent` 是 MAKER 系统的核心协调器，负责编排整个任务执行流程。原始文件超过 1900 行，经过 P0-1 重构后拆分为 6 个 partial class 文件。

## 文件结构

```
src/Aevatar.Agents.Maker/Agents/
├── MakerCoordinatorGAgent.cs   (555 行) - 核心状态、API、事件处理
├── MakerTaskExecutor.cs        (658 行) - 任务执行逻辑
├── MakerVotingCoordinator.cs   (291 行) - 投票协调
├── MakerProviderValidator.cs   (324 行) - Provider 发现与验证
├── MakerStateRecovery.cs       (163 行) - 状态持久化 (P0-2)
├── MakerProgressReporter.cs    (62 行)  - 进度报告与 RedFlag
└── MakerWorkerGAgent.cs        (336 行) - Worker Agent (独立)
```

## 职责划分

### MakerCoordinatorGAgent.cs
- **字段定义**: 策略、运行时状态、异步协调状态
- **Public API**: SetDependencies, GetResult, GetStatus 等
- **Event Handlers**: HandleStartMakerTaskRequest, HandleWorkerInitialized, HandleProposalResult
- **Event Handler Helpers**: 初始化、执行、失败处理的辅助方法

### MakerTaskExecutor.cs
- **TaskExecutionPhase**: 执行阶段枚举
- **TaskExecutionContext**: 执行上下文
- **ExecuteTaskAsync**: 迭代式任务执行（栈实现，避免递归）
- **CheckBudget**: 预算检查
- **AssessAtomicityAsync**: 原子性评估
- **SolveAtomicTaskAsync**: 原子任务求解
- **BuildChildContext**: 子任务上下文构建

### MakerVotingCoordinator.cs
- **RunVotingWithWorkersAsync**: 流式竞赛投票
- **ParseAtomicityResponse**: JSON 响应解析
- **DecorrelateTemperature**: 温度去相关

### MakerProviderValidator.cs
- **DiscoverAndValidateProvidersAsync**: Provider 发现与验证
- **ValidateSingleProviderAsync**: 单个 Provider 验证
- **ValidateProvidersAsync**: 批量验证（兼容）
- **ExtractConciseError**: 错误信息提取

### MakerStateRecovery.cs
- **PersistRuntimeState**: 状态持久化
- **RestoreRuntimeState**: 状态恢复
- **HasInterruptedExecution**: 中断检测
- **OptionsToProto/ProtoToOptions**: 配置转换

### MakerProgressReporter.cs
- **ReportProgress**: 进度报告
- **AddRedFlag**: 红旗事件记录

## 设计原则

1. **Partial Class**: 使用 partial class 保持访问权限，同时分离关注点
2. **单一职责**: 每个文件只处理一种类型的逻辑
3. **迭代执行**: 任务执行使用显式栈，避免深度递归导致的栈溢出
4. **流式竞赛**: 投票采用流式模式，支持早期终止

## 数据流

```
StartMakerTaskRequest
    ↓
InitializeExecutionState
    ↓
InitializeWorkers (→ InitializeWorkerRequest DOWN)
    ↓
ExecuteTaskAsync (iterative stack)
    ├── AssessAtomicity
    ├── RunVotingWithWorkers (→ GenerateProposalRequest DOWN)
    │       ↑
    │   HandleProposalResult (← ProposalResult UP)
    │       ↓
    │   VoteEngine.SubmitVote
    │       ↓ (consensus)
    │   Early Termination
    ├── SolveAtomicTask
    └── Compose Results
    ↓
MakerTaskCompleted
```

## 重构效果

| 指标 | 重构前 | 重构后 | 改进 |
|------|--------|--------|------|
| 主文件行数 | 1926 | 555 | -71% |
| 最大单文件 | 1926 | 658 | -66% |
| 文件数量 | 1 | 6 | +5 |

---

*文档版本: 1.0.0*  
*创建日期: 2025-11-27*

