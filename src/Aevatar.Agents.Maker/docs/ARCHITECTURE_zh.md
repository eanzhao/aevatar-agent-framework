# MAKER 架构设计

## 设计哲学

### 核心理念

**论文精神的忠实实现**：MAKER论文的核心洞察是——不要让LLM做复杂的事，让它做简单的事，然后通过投票获得可靠性。

设计原则：
1. **用户只关心任务，框架处理复杂性**
2. **组合优于继承**——通过策略接口扩展，而非继承重写
3. **显式优于隐式**——投票参数自动计算，但可查询
4. **单一职责**——每个组件只做一件事

### 与V1的对比

| 维度 | V1 | V2 |
|-----|-----|-----|
| 用户入口 | 继承`MakerTaskAgent`，override 5个方法 | 调用`ExecuteAsync()`，可选注入策略 |
| 核心代码 | 1600+行，6个partial文件 | ~800行，职责分离 |
| 用户代码 | 八字Demo 457行，论文Demo 303行 | 八字 60行，论文 50行 |
| 投票 | 嵌入TaskAgent + 可选ConsensusAgent | 独立VoteEngine |
| 论文对齐 | 部分（缺少N=2k-1，去相关） | 完全 |

## 组件架构

```
┌─────────────────────────────────────────────────────────────────┐
│                      IMakerExecutor                             │
│                           │                                      │
│                    MakerExecutor                                 │
│  ┌────────────────────────┼────────────────────────┐            │
│  │                        │                        │            │
│  ▼                        ▼                        ▼            │
│ VoteEngine           Strategies              ExecutionPool      │
│  - First-K            - IDecompositionStrategy  - Parallel LLM  │
│  - Clustering         - ISolutionStrategy       - Decorrelation │
│  - N=2k-1             - ICompositionStrategy    - Multi-model   │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │                    RedFlagHandler                           │ │
│  │  - 错误检测                                                  │ │
│  │  - 恢复策略（重试/接受最佳/强制原子/放弃）                  │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

## 文件结构

```
Aevatar.Agents.Maker.V2/
├── Core/
│   ├── IMakerExecutor.cs      # 用户入口接口
│   ├── MakerExecutor.cs       # 核心编排器实现
│   ├── MakerOptions.cs        # 配置（ReliabilityLevel→K→N自动计算）
│   ├── MakerResult.cs         # 结果（含完整Trace）
│   └── MakerProgress.cs       # 进度回调
├── Voting/
│   └── VoteEngine.cs          # First-to-ahead-by-K投票引擎
├── Strategies/
│   ├── IDecompositionStrategy.cs   # 分解策略接口
│   ├── ISolutionStrategy.cs        # 求解策略接口
│   ├── ICompositionStrategy.cs     # 聚合策略接口
│   ├── DefaultDecomposer.cs        # 默认分解器
│   ├── DefaultSolver.cs            # 默认求解器
│   └── DefaultComposer.cs          # 默认聚合器
├── Execution/
│   └── ExecutionPool.cs       # 并行LLM执行池（支持去相关）
├── RedFlag/
│   └── RedFlagHandler.cs      # 红旗处理与恢复
├── Adapters/
│   └── AevatarLLMAdapter.cs   # 与现有Aevatar LLM基础设施集成
├── Examples/
│   ├── BaziDecomposer.cs      # 八字领域策略示例
│   ├── PaperDecomposer.cs     # 论文领域策略示例
│   └── UsageExamples.cs       # 使用示例
└── MakerServiceCollectionExtensions.cs  # DI注册
```

## 核心流程

### 1. 任务执行流程

```
ExecuteAsync(task)
    │
    ├── IsAtomic? ──Yes──► SolveAtomicAsync ──► Vote ──► Result
    │       │
    │      No
    │       │
    │       ▼
    │   DecomposeAsync ──► Vote ──► Steps[]
    │       │
    │       ▼
    │   For each step:
    │       ExecuteAsync(step) ──► childResults
    │       │
    │       ▼
    │   ComposeAsync(childResults) ──► Result
    │
    └── Return MakerResult with full Trace
```

### 2. 投票流程

```
VoteEngine (K=2, N=3)
    │
    ├── 并行请求3个LLM响应
    │       │
    │       ▼
    │   Submit vote for each response
    │       │
    │       ▼
    │   Check: leader - runnerUp >= K ?
    │       │
    │      Yes ──► 共识达成，返回winner
    │       │
    │      No + 投票数达到N ──► 新一轮
    │       │
    │   Max轮次用尽 ──► RedFlag或接受最佳
```

### 3. 去相关机制

```
ExecutionPool
    │
    ├── 多Provider（不同模型）
    │       deepseek-chat
    │       gpt-4
    │       claude-3
    │
    ├── Temperature变化
    │       request[0].temp = 0.2
    │       request[1].temp = 0.25
    │       request[2].temp = 0.15
    │
    └── 并行执行，流式返回
```

## 扩展指南

### 添加领域策略

只需实现接口，无需继承任何类：

```csharp
public class MyDomainDecomposer : IDecompositionStrategy
{
    public string BuildDecompositionPrompt(string task, IReadOnlyDictionary<string, string> ctx)
    {
        // 返回领域特定的分解prompt
    }
    
    public bool IsAtomic(string task, int depth, int maxDepth)
    {
        // 领域特定的原子性判断
    }
    
    public IReadOnlyList<(string, string)> ParseDecomposition(string output)
    {
        // 可以复用DefaultDecomposer
        return new DefaultDecomposer().ParseDecomposition(output);
    }
}
```

### 自定义红旗处理

```csharp
public class MyRedFlagHandler : IRedFlagHandler
{
    public RedFlagRecovery HandleRedFlag(RedFlagContext ctx)
    {
        // 领域特定的恢复策略
        if (ctx.Type == RedFlagType.NoConsensus && ctx.RecoveryAttempts < 3)
        {
            return new RedFlagRecovery
            {
                Action = RecoveryAction.Retry,
                ModifiedPrompt = "请更明确地..." + ctx.TaskDescription
            };
        }
        
        return new RedFlagRecovery { Action = RecoveryAction.Abort };
    }
}
```

## 数学基础

### First-to-ahead-by-K

设基础模型每步错误率为 `p`，则：

- 单次调用错误概率：`p`
- K次投票中至少K票错误的概率：`O(p^K)`

例如 `p = 0.1`（90%准确率）：
- K=1: 错误率 10%
- K=2: 错误率 ~1%
- K=3: 错误率 ~0.1%
- K=5: 错误率 ~0.001%

### 样本数量

`N = 2K - 1` 确保在最坏情况下仍能达成共识：
- K=1: N=1（单次调用）
- K=2: N=3（保证leader能领先runner-up至少2票）
- K=3: N=5
- K=5: N=9

## 最佳实践

1. **从Medium开始**——除非有特殊需求，否则`ReliabilityLevel.Medium`足够大多数场景
2. **复用默认解析器**——自定义Decomposer时，`ParseDecomposition`可以直接用`DefaultDecomposer`
3. **Context传递上下文**——使用`context`字典在步骤间传递信息，而不是修改prompt
4. **检查Trace**——出问题时查看`result.Trace`了解投票详情和红旗事件
5. **多Provider去相关**——生产环境配置多个不同的LLM Provider

