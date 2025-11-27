# MAKER V2 - 多智能体知识增强推理系统

基于 Aevatar Agent Framework 实现的 MAKER（Multi-Agent Knowledge-Enhanced Reasoning）算法。该系统通过多个 LLM Agent 的共识投票机制，实现比单次推理更高的准确率。

## 目录

- [系统概述](#系统概述)
- [核心架构](#核心架构)
- [核心算法](#核心算法)
- [配置参数详解](#配置参数详解)
- [配置示例](#配置示例)
- [API 参考](#api-参考)
- [集成指南](#集成指南)
- [Red Flag 质量控制](#red-flag-质量控制)
- [性能调优](#性能调优)
- [故障排除](#故障排除)

---

## 系统概述

### 什么是 MAKER？

MAKER 是一个多智能体推理框架，通过以下方式提升 LLM 的准确性：

1. **分解（Decompose）**：将复杂任务拆分为简单子任务
2. **采样（Sample）**：多个 Worker 独立生成解决方案
3. **投票（Vote）**：通过共识机制选出最佳答案
4. **合成（Compose）**：将子任务结果组合成最终输出

### 核心优势

| 优势 | 说明 |
|------|------|
| **更高准确率** | 共识投票能捕获 LLM 的随机错误 |
| **可扩展复杂度** | 递归分解可处理任意复杂任务 |
| **成本可控** | 基于预算的限制防止 Token 失控 |
| **灵活模式** | 双模式设计（Production vs Academic） |
| **可观测性** | 通过 SSE 实时推送进度 |

### 适用场景

✅ **适合：**
- 正确性比速度更重要的任务
- 复杂的多步推理
- 事实核查和验证
- 数学证明和计算
- 关键业务决策

❌ **不适合：**
- 简单的单次查询
- 实时聊天应用
- 有严格延迟要求的任务
- 需要多样性输出的创意任务

---

## 核心架构

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     MakerCoordinatorGAgent（协调者）                      │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                      递归任务执行流程                             │    │
│  │  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐          │    │
│  │  │   评估      │ →  │   分解      │ →  │   执行      │          │    │
│  │  │  原子性    │    │  或求解    │    │  子任务    │          │    │
│  │  └─────────────┘    └─────────────┘    └─────────────┘          │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                              │                                           │
│                              │ 事件（DOWN 方向）                          │
│                              ↓                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                     Worker 池（N 个 Agent）                       │    │
│  │  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐│    │
│  │  │Worker 0 │  │Worker 1 │  │Worker 2 │  │Worker 3 │  │Worker N ││    │
│  │  │ T=0.25  │  │ T=0.35  │  │ T=0.28  │  │ T=0.32  │  │ T=...   ││    │
│  │  └────┬────┘  └────┬────┘  └────┬────┘  └────┬────┘  └────┬────┘│    │
│  │       │            │            │            │            │      │    │
│  │       └────────────┴────────────┴────────────┴────────────┘      │    │
│  │                              │                                    │    │
│  │                              │ ProposalResult（UP 方向）          │    │
│  │                              ↓                                    │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                              │                                           │
│                              ↓                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                      VoteEngine（投票引擎）                        │    │
│  │  ┌─────────────────────────────────────────────────────────┐    │    │
│  │  │  语义聚类（基于 Embedding 的相似度计算）                   │    │    │
│  │  │  First-to-ahead-by-K 共识算法                           │    │    │
│  │  │  达成共识时提前终止                                       │    │    │
│  │  └─────────────────────────────────────────────────────────┘    │    │
│  └─────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────┘
```

### 组件说明

| 组件 | 职责 |
|------|------|
| **MakerCoordinatorGAgent** | 协调整个执行流程 |
| **MakerWorkerGAgent** | 执行单个 LLM 调用 |
| **VoteEngine** | 实现带语义聚类的共识投票 |
| **IDecompositionStrategy** | 定义任务分解方式 |
| **ISolutionStrategy** | 定义原子任务求解方式 |
| **ICompositionStrategy** | 定义结果合成方式 |
| **IRedFlagStrategy** | 定义内容质量验证规则 |

---

## 核心算法

### 1. First-to-ahead-by-K 投票算法

MAKER 使用的共识算法：

```
给定：K（共识阈值），N = 2K-1（每轮采样数）

对于每个收到的提案：
  1. 计算语义 Embedding
  2. 在现有聚类中找最相似的（余弦相似度 ≥ 阈值）
  3. 如果找到：加入该聚类，票数+1
     否则：创建新聚类
  4. 检查共识：领先者票数 - 第二名票数 ≥ K？
     如果是：达成共识 → 提前终止
     如果否：继续采样
```

**示例（K=2, N=3）：**

| 提案 | 操作 | 聚类A | 聚类B | 差距 | 结果 |
|------|------|-------|-------|------|------|
| P1: "4" | 创建 A | 1 | 0 | 1 | 继续 |
| P2: "4" | 加入 A | 2 | 0 | 2 | **达成共识！** |

### 2. 语义聚类

与精确匹配不同，MAKER 使用基于 Embedding 的相似度：

```
提案 A: "新加坡是东南亚的城市国家"
提案 B: "新加坡位于东南亚，是一个城市国家"

余弦相似度 = 0.94 > 0.85（阈值）
→ 同一聚类，合并计票
```

### 3. 流式竞速模式

Worker 不需要等待所有结果：

```
T=0ms:    派发 5 个 Worker
T=800ms:  Worker 2 返回 → 投票 → A 获得 1 票
T=1200ms: Worker 0 返回 → 投票 → A 获得 2 票 → 达成共识！
T=1201ms: 取消剩余 Worker（3, 4, 1）
          → 节省了约 2 秒等待时间
```

### 4. 持续采样

如果第一批未达成共识，继续添加 Worker：

```
第1批：5 个 Worker → 无共识（A:2, B:2, C:1）
第2批：再加 5 个 Worker → A:4, B:3, C:3 → 无共识
第3批：再加 5 个 Worker → A:6, B:4, C:5 → 达成共识！（A 领先 2 票）
```

---

## 配置参数详解

### MakerOptions

MAKER 执行的主要配置对象。

#### 可靠性设置

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Reliability` | `ReliabilityLevel` | `Medium` | 预设可靠性级别 |
| `CustomK` | `int?` | `null` | 直接覆盖 K 值 |

**ReliabilityLevel 可靠性级别：**

| 级别 | K | N（采样数） | 适用场景 |
|------|---|-------------|----------|
| `Low` | 1 | 1 | 快速探索，可接受错误 |
| `Medium` | 2 | 3 | 大多数任务的平衡选择 |
| `High` | 3 | 5 | 重要任务 |
| `VeryHigh` | 4 | 7 | 关键任务 |
| `Critical` | 5 | 9 | 必须正确 |
| `UltraCritical` | 7 | 13 | 极端关键 |
| `Extreme` | 10 | 19 | 最高可靠性 |

#### 预算控制

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `MaxTotalLlmCalls` | `int` | `500` | 最大 LLM API 调用次数 |
| `MaxTotalTokens` | `long` | `2,000,000` | 最大消耗 Token 数 |
| `MaxDuration` | `TimeSpan` | `30 分钟` | 最大执行时间 |
| `DepthWarningThreshold` | `int` | `10` | 深度超过此值时发出警告 |
| `HardDepthCap` | `int` | `50` | 绝对最大深度（安全网） |

#### 执行模式

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Mode` | `ExecutionMode` | `Production` | 执行策略 |

**ExecutionMode 执行模式：**

| 模式 | 行为 | 成本 | 准确率 |
|------|------|------|--------|
| `Production` | 先评估原子性，按需分解 | 💰 较低 | ✓ 良好 |
| `Academic` | 默认强制分解（论文方法） | 💰💰 较高 | ✓✓ 更好 |

#### 分解设置

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Granularity` | `DecompositionGranularity` | `Balanced` | 每次分解的子任务数量 |
| `ContextIsolation` | `ContextIsolationMode` | `Full` | 上下文传递给子任务的方式 |

**DecompositionGranularity 分解粒度：**

| 值 | 每次分解步数 | 树形状 | 适用场景 |
|----|--------------|--------|----------|
| `Balanced` | 3-6 | 宽而浅 | 通用任务 |
| `Binary` | 2 | 二叉树 | 精确控制 |
| `Single` | 1 | 线性链 | 极端粒度 |

**ContextIsolationMode 上下文隔离模式：**

| 值 | 行为 | 上下文大小 | 适用场景 |
|----|------|------------|----------|
| `Full` | 继承所有父级上下文 + 所有兄弟结果 | 📈 增长 | 连贯写作 |
| `Minimal` | 只继承领域上下文 + 上一步结果 | 📊 可控 | 深层递归 |
| `None` | 无继承，从零开始 | 📉 最小 | 独立子任务 |

#### 投票设置

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `ClusteringMethod` | `string` | `"semantic"` | `"semantic"` 或 `"exact"` |
| `SemanticSimilarityThreshold` | `float` | `0.85` | 聚类相似度阈值 |
| `BaseTemperature` | `float` | `0.3` | 基础 LLM 温度 |
| `TemperatureVariance` | `float` | `0.1` | 温度去相关范围 |
| `UseMultipleProviders` | `bool` | `false` | 使用多个 LLM 提供商（未来） |
| `RedFlagThreshold` | `int` | `3` | 最大红旗次数后中止 |

#### Red Flag 质量控制设置

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `RedFlagStrategy` | `IRedFlagStrategy?` | `null` | 自定义内容验证策略 |
| `RedFlagOptions` | `RedFlagOptions` | `(见下)` | 默认策略的选项 |

**RedFlagOptions 选项：**

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `MaxContentLength` | `int` | `8000` | 最大允许内容长度（写小说可加大） |
| `MinContentLength` | `int` | `10` | 最小要求内容长度 |
| `EnableRefusalDetection` | `bool` | `true` | 检测内容开头的 LLM 拒绝 |
| `EnableDegenerationDetection` | `bool` | `true` | 检测重复输出（生成循环） |
| `EnableLengthValidation` | `bool` | `true` | 验证内容长度限制 |
| `CustomRefusalPrefixes` | `IReadOnlyList<string>?` | `null` | 自定义拒绝前缀 |

**可用的 Red Flag 策略：**

| 策略 | 适用场景 |
|------|----------|
| `DefaultEnglishRedFlagStrategy` | 英文内容（默认） |
| `ChineseRedFlagStrategy` | 中文内容 |
| `CodeAwareRedFlagStrategy` | 代码生成（放宽拒绝检测） |
| `CompositeRedFlagStrategy` | 组合多个策略 |
| `NoOpRedFlagStrategy` | 禁用所有检查 |

#### 策略覆盖

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Decomposer` | `IDecompositionStrategy?` | `null` | 自定义分解策略 |
| `Solver` | `ISolutionStrategy?` | `null` | 自定义求解策略 |
| `Composer` | `ICompositionStrategy?` | `null` | 自定义合成策略 |
| `Context` | `Dictionary<string, string>?` | `null` | 初始上下文变量 |

#### 超时设置

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `StepTimeout` | `TimeSpan` | `60 秒` | 单个步骤超时时间 |

#### 回调

| 参数 | 类型 | 说明 |
|------|------|------|
| `OnProgress` | `Action<MakerProgress>?` | 进度回调 |

---

## 配置示例

### 1. 简单任务（省钱模式）

适合简单直接的任务，速度优先。

```json
{
    "name": "快速摘要",
    "task": "用3句话总结这篇文章",
    "reliability": "Low",
    "maxTotalLlmCalls": 50,
    "maxTotalTokens": 100000,
    "maxDurationMinutes": 5,
    "executionMode": "Production",
    "granularity": "Balanced",
    "contextIsolation": "Full"
}
```

**特点：**
- K=1, N=1（单次采样，无投票）
- 快速执行
- 成本低
- 非关键任务可接受的错误率

---

### 2. 通用任务（平衡模式）

大多数任务的默认配置。

```json
{
    "name": "国家介绍",
    "task": "全面介绍新加坡这个国家",
    "reliability": "Medium",
    "maxTotalLlmCalls": 200,
    "maxTotalTokens": 500000,
    "maxDurationMinutes": 15,
    "executionMode": "Production",
    "granularity": "Balanced",
    "contextIsolation": "Full",
    "context": {
        "language": "中文",
        "style": "百科全书式",
        "length": "1500-2000字"
    }
}
```

**特点：**
- K=2, N=3（基础共识）
- 每次分解 3-6 个子任务
- 完整上下文继承保证连贯性
- 成本与准确率的良好平衡

---

### 3. 数学证明（高精度模式）

需要逻辑正确性的任务。

```json
{
    "name": "数学推导",
    "task": "用数学归纳法证明：1+2+3+...+n = n(n+1)/2",
    "reliability": "High",
    "maxTotalLlmCalls": 300,
    "maxTotalTokens": 800000,
    "maxDurationMinutes": 20,
    "executionMode": "Academic",
    "granularity": "Binary",
    "contextIsolation": "Minimal",
    "context": {
        "rigor": "严格数学证明",
        "audience": "本科生水平"
    }
}
```

**特点：**
- K=3, N=5（更强共识）
- Academic 模式：总是先分解
- Binary 分解实现精确控制
- Minimal 上下文防止混淆

---

### 4. 事实核查（关键模式）

准确性至关重要的任务。

```json
{
    "name": "历史时间线",
    "task": "整理二战关键事件时间线，要求日期精确",
    "reliability": "Critical",
    "maxTotalLlmCalls": 500,
    "maxTotalTokens": 2000000,
    "maxDurationMinutes": 30,
    "executionMode": "Academic",
    "granularity": "Binary",
    "contextIsolation": "Minimal",
    "context": {
        "accuracy": "日期必须精确到天",
        "source": "主流历史学界共识"
    }
}
```

**特点：**
- K=5, N=9（强共识要求）
- Academic 模式进行最大程度审查
- 更高的 Token 预算
- 允许更长的执行时间

---

### 5. 逻辑推理（逐步模式）

需要仔细顺序推理的任务。

```json
{
    "name": "逻辑谜题",
    "task": "解题：Alice、Bob、Carol 住在不同的房子（红、绿、蓝）。Alice 不住红房子。Bob 不住绿房子或蓝房子。谁住哪个房子？",
    "reliability": "High",
    "maxTotalLlmCalls": 300,
    "maxTotalTokens": 800000,
    "maxDurationMinutes": 20,
    "executionMode": "Academic",
    "granularity": "Single",
    "contextIsolation": "Minimal",
    "context": {
        "method": "逐步演绎推理",
        "verification": "答案必须满足所有约束条件"
    }
}
```

**特点：**
- Single 粒度（思维链）
- 每一步验证后再继续
- Minimal 上下文防止推理错误

---

### 6. 创意写作（多样性模式）

可以接受多样化输出的任务。

```json
{
    "name": "科幻小说",
    "task": "写一篇2000字的关于AI意识觉醒的科幻小说",
    "reliability": "Low",
    "maxTotalLlmCalls": 300,
    "maxTotalTokens": 1500000,
    "maxDurationMinutes": 25,
    "executionMode": "Production",
    "granularity": "Balanced",
    "contextIsolation": "Full",
    "context": {
        "tone": "发人深省，悬疑",
        "setting": "2050年",
        "style": "阿西莫夫式硬科幻"
    }
}
```

**特点：**
- Low 可靠性（K=1），因为创意有主观性
- Full 上下文保证叙事连贯
- Production 模式避免过度分解

---

### 7. 中文任务（中文 Red Flag）

需要中文内容验证的任务。

```json
{
    "name": "中文报告",
    "task": "撰写一份关于人工智能发展趋势的分析报告",
    "reliability": "Medium",
    "maxTotalLlmCalls": 300,
    "maxTotalTokens": 1000000,
    "maxDurationMinutes": 20,
    "executionMode": "Production",
    "granularity": "Balanced",
    "contextIsolation": "Full",
    "context": {
        "language": "中文",
        "format": "正式报告",
        "length": "3000字"
    }
}
```

**代码配置：**

```csharp
var options = new MakerOptions
{
    Reliability = ReliabilityLevel.Medium,
    RedFlagStrategy = new ChineseRedFlagStrategy(new RedFlagOptions
    {
        MaxContentLength = 15000  // 中文报告可能较长
    }),
    Context = new Dictionary<string, string>
    {
        ["language"] = "中文",
        ["format"] = "正式报告"
    }
};
```

---

### 8. 代码生成（代码友好模式）

生成代码的任务。

```json
{
    "name": "API 设计",
    "task": "设计一个完整的任务管理系统 REST API",
    "reliability": "High",
    "maxTotalLlmCalls": 400,
    "maxTotalTokens": 1500000,
    "maxDurationMinutes": 25,
    "executionMode": "Production",
    "granularity": "Balanced",
    "contextIsolation": "Full",
    "context": {
        "format": "OpenAPI 3.0 YAML",
        "auth": "JWT Token",
        "versioning": "URL 路径版本控制"
    }
}
```

**代码配置：**

```csharp
var options = new MakerOptions
{
    Reliability = ReliabilityLevel.High,
    // 代码场景使用 CodeAwareRedFlagStrategy
    // - 关闭拒绝检测（代码注释可能包含 "I cannot"）
    // - 放宽长度限制（代码可能很长）
    RedFlagStrategy = new CodeAwareRedFlagStrategy()
};
```

---

### 9. 极端复杂任务（研究模式）

研究或极端复杂问题。

```json
{
    "name": "复杂分析",
    "task": "解决这个需要大量推理的复杂多部分问题",
    "reliability": "Extreme",
    "maxTotalLlmCalls": 2000,
    "maxTotalTokens": 10000000,
    "maxDurationMinutes": 120,
    "executionMode": "Academic",
    "granularity": "Single",
    "contextIsolation": "None",
    "depthWarningThreshold": 30,
    "hardDepthCap": 100
}
```

**特点：**
- Extreme 可靠性（K=10, N=19）
- Single 粒度（线性链）
- None 上下文隔离（每步独立）
- 扩展限制用于长时间分析

---

## 投票参数速查表

| 任务类型 | K | 粒度 | 上下文 | 模式 |
|----------|---|------|--------|------|
| 快速查询 | 1 | Balanced | Full | Production |
| 一般写作 | 2 | Balanced | Full | Production |
| 技术文档 | 2-3 | Balanced | Full | Production |
| 数学/逻辑 | 3-5 | Binary/Single | Minimal | Academic |
| 事实核查 | 4-5 | Binary | Minimal | Academic |
| 研究分析 | 4-7 | Binary | Minimal | Academic |
| 关键决策 | 5-10 | Single | None | Academic |

---

## API 参考

### IMakerExecutor

```csharp
public interface IMakerExecutor
{
    Task<MakerResult> ExecuteAsync(
        string task,
        MakerOptions? options = null,
        CancellationToken ct = default);
}
```

### MakerResult

```csharp
public record MakerResult
{
    public bool Success { get; init; }          // 是否成功
    public string? Content { get; init; }       // 最终结果
    public string? Error { get; init; }         // 错误信息
    public MakerTrace? Trace { get; init; }     // 执行追踪
}

public record MakerTrace
{
    public TaskNode? RootTask { get; init; }    // 任务树根节点
    public int TotalLLMCalls { get; init; }     // LLM 调用总数
    public long TotalTokens { get; init; }      // 总 Token 数
    public long PromptTokens { get; init; }     // Prompt Token 数
    public long CompletionTokens { get; init; } // Completion Token 数
    public TimeSpan Duration { get; init; }     // 执行时长
    public IReadOnlyList<RedFlagEvent>? RedFlags { get; init; }  // 红旗事件
}
```

### MakerProgress

```csharp
public record MakerProgress
{
    public MakerPhase Phase { get; init; }       // 当前阶段
    public string? TaskId { get; init; }         // 任务 ID
    public string? Message { get; init; }        // 消息
    public int Depth { get; init; }              // 递归深度
    public DateTime Timestamp { get; init; }     // 时间戳
    public VotingProgress? Voting { get; init; } // 投票进度
    public LLMProposal? Proposal { get; init; }  // 提案详情
    public string? RedFlagReason { get; init; }  // 红旗原因
}
```

---

## 集成指南

### 基本使用

```csharp
// 注册服务
services.AddMakerV2();

// 注入并使用
public class MyService
{
    private readonly IMakerExecutor _maker;
    
    public MyService(IMakerExecutor maker)
    {
        _maker = maker;
    }
    
    public async Task<string> AnalyzeAsync(string input)
    {
        var result = await _maker.ExecuteAsync(
            $"分析以下内容：{input}",
            new MakerOptions
            {
                Reliability = ReliabilityLevel.High,
                RedFlagStrategy = new ChineseRedFlagStrategy(),
                OnProgress = p => Console.WriteLine($"[{p.Phase}] {p.Message}")
            });
        
        return result.Success 
            ? result.Content! 
            : throw new Exception(result.Error);
    }
}
```

### 使用自定义策略

```csharp
var options = new MakerOptions
{
    Decomposer = new MyCustomDecomposer(),
    Solver = new MyCustomSolver(),
    Composer = new MyCustomComposer(),
    RedFlagStrategy = new ChineseRedFlagStrategy(),
    Context = new Dictionary<string, string>
    {
        ["domain"] = "金融",
        ["language"] = "中文"
    }
};
```

### 从 JSON 配置加载

```csharp
var config = ProjectConfig.FromJson(jsonString);
var options = config.BuildOptions(progress => 
    Console.WriteLine($"[{progress.Phase}] {progress.Message}"));

var result = await maker.ExecuteAsync(config.Task, options);
```

---

## Red Flag 质量控制

MAKER 包含可插拔的质量控制系统（`IRedFlagStrategy`），在进入投票池之前拒绝有问题的 LLM 输出。

### 默认检查（DefaultEnglishRedFlagStrategy）

| 红旗 | 触发条件 | 操作 |
|------|----------|------|
| 内容过长 | > MaxContentLength (8000) | 拒绝进入投票 |
| 内容过短 | < MinContentLength (10) | 拒绝进入投票 |
| LLM 拒绝 | 以 "I cannot", "I'm sorry" 等开头 | 拒绝进入投票 |
| 过度重复 | 相同模式重复多次 | 拒绝进入投票 |
| 分解失败 | 未解析出有效步骤 | 回退到直接求解 |
| 共识失败 | 达到最大采样仍无共识 | 使用最佳候选 |

### 中文内容检查（ChineseRedFlagStrategy）

```csharp
// 中文拒绝前缀
"我无法", "我不能", "抱歉", "对不起", "很抱歉",
"作为AI", "作为一个AI", "作为语言模型", ...
```

### 自定义 Red Flag 策略

```csharp
// 中文内容
var options = new MakerOptions
{
    RedFlagStrategy = new ChineseRedFlagStrategy(new RedFlagOptions
    {
        MaxContentLength = 10000
    })
};

// 代码生成（放宽限制）
var options = new MakerOptions
{
    RedFlagStrategy = new CodeAwareRedFlagStrategy()
};

// 禁用所有检查
var options = new MakerOptions
{
    RedFlagStrategy = NoOpRedFlagStrategy.Instance
};

// 自定义策略
public class MyDomainRedFlagStrategy : IRedFlagStrategy
{
    public bool Validate(string content, string proposalId, out string? reason)
    {
        reason = null;
        
        // 你的领域特定验证逻辑
        if (content.Contains("非法操作"))
        {
            reason = "检测到非法操作";
            return false;
        }
        
        return true;
    }
}
```

---

## 性能调优

### 降低成本

1. 降低 `Reliability` 级别
2. 使用 `Production` 模式
3. 减少 `MaxTotalLlmCalls`
4. 使用 `Balanced` 粒度

### 提高准确率

1. 提高 `Reliability` 级别
2. 使用 `Academic` 模式
3. 使用 `Binary` 或 `Single` 粒度
4. 增加 Token 预算

### 处理深层递归

1. 使用 `Minimal` 或 `None` 上下文隔离
2. 设置适当的 `DepthWarningThreshold`
3. 确保 `HardDepthCap` 合理

---

## 故障排除

### "未达成共识"

- 提高 `Reliability` 级别
- 检查任务是否过于模糊
- 对于主观任务，降低 `SemanticSimilarityThreshold`

### "预算超限"

- 增加 `MaxTotalLlmCalls` 或 `MaxTotalTokens`
- 使用 `Production` 模式
- 使用 `Balanced` 粒度

### "无限分解循环"

- 检查自定义 `IsAtomic` 实现
- 确保设置了 `HardDepthCap`
- 对问题任务使用 `Single` 粒度

### "上下文过大"

- 使用 `Minimal` 或 `None` 上下文隔离
- 减少初始上下文大小

---

## 许可证

MIT License - 详见 LICENSE 文件。

---

## 参考资料

- MAKER 论文：[Multi-Agent Knowledge-Enhanced Reasoning](https://arxiv.org/abs/...)
- Aevatar Agent Framework：[文档](https://github.com/...)

