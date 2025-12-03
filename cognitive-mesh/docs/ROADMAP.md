# Cognitive Mesh 渐进式设计路线图

> **核心理念**：从具体到抽象，从单一到统一，从原型到产品
> 
> 将 MakerSystem（可靠推理）与 CreativeSystem（创造推理）合并为统一的认知网格架构

---

## 🎯 愿景

**Cognitive Mesh** 是一个声明式、有状态的认知架构构建器，旨在：

1. **统一推理策略**：CoT、ToT、GoT、UoT、MAKER 等在同一框架下运行
2. **百万步可靠性**：基于 Actor Model + Event Sourcing 实现断点续跑
3. **可观测认知**：实时可视化思维流、投票共识、分解树
4. **声明式配置**：通过 DSL 定义认知拓扑，而非硬编码流程

---

## 📊 设计原则

### 渐进式演进

```
Phase 1: 策略抽象层          ← 当前阶段 ✅
    ↓
Phase 2: 统一执行引擎        ← 进行中 🔄
    ↓
Phase 3: DSL 编译器
    ↓
Phase 4: 可视化 UI
    ↓
Phase 5: 高级功能
```

### 核心设计决策

| 决策点 | 选择 | 原因 |
|--------|------|------|
| 策略抽象 | `IReasoningStrategy` 接口 | 统一不同推理范式的执行入口 |
| 进度报告 | `ReasoningProgress` 统一结构 | 前端可统一处理不同策略的进度 |
| 结果格式 | `ReasoningResult` 统一结构 | 便于比较、存储、分析 |
| 配置模型 | `ReasoningOptions` 带策略特定字段 | 既统一又灵活 |
| 执行模式 | 后台异步 + SSE 流式推送 | 长时任务的最佳实践 |

---

## 🚀 Phase 1: 策略抽象层 ✅ 已完成

**目标**：定义统一的推理策略接口，适配现有的 MAKER 和 UoT 实现

### 1.1 核心抽象

```
Aevatar.CognitiveMesh.Abstractions/
├── IReasoningStrategy.cs      # 策略接口
├── StrategyKind.cs            # 策略类型枚举
├── ReasoningOptions.cs        # 统一配置
├── ReasoningProgress.cs       # 统一进度
├── ReasoningResult.cs         # 统一结果
├── ValidationResult.cs        # 验证结果
├── IMeshProject.cs            # 项目接口
└── IMeshEventSink.cs          # 事件发送接口
```

### 1.2 策略类型

```csharp
public enum StrategyKind
{
    // ─── 经典策略 ───
    Cot,              // Chain of Thought
    Tot,              // Tree of Thoughts
    Got,              // Graph of Thoughts
    
    // ─── UoT 变体 ───
    UotCombinational, // 组合式：跨域类比 + 思维合成
    UotExploratory,   // 探索式：假设检验 + 验证循环
    UotTransformative,// 变革式：范式转换 + 重构思维
    
    // ─── MAKER ───
    Maker             // 分解-共识-合成
}
```

### 1.3 统一接口

```csharp
public interface IReasoningStrategy
{
    StrategyKind Kind { get; }
    string DisplayName { get; }
    
    Task<ReasoningResult> ExecuteAsync(
        string problem,
        ReasoningOptions options,
        IProgress<ReasoningProgress>? progress = null,
        CancellationToken ct = default);
    
    ValidationResult ValidateOptions(ReasoningOptions options);
}
```

### 1.4 已实现的策略适配器

| 策略 | 适配器 | 状态 |
|------|--------|------|
| MAKER | `MakerStrategy.cs` | ✅ 完成 |
| UoT Combinational | `UoTCombinationalStrategy.cs` | ✅ 完成 |
| CoT | 待实现 | 📋 计划中 |
| ToT | 待实现 | 📋 计划中 |

---

## 🔄 Phase 2: 统一执行引擎 🔄 进行中

**目标**：构建统一的项目管理和执行服务

### 2.1 核心服务架构

```
Aevatar.CognitiveMesh/
├── Services/
│   ├── CognitiveMeshService.cs   # 核心服务
│   └── StrategyRegistry.cs       # 策略注册中心
├── Strategies/
│   ├── MakerStrategy.cs          # MAKER 适配器
│   └── UoTCombinationalStrategy.cs # UoT 适配器
├── Models/
│   └── MeshRun.cs                # 运行状态
└── wwwroot/
    ├── index.html                # 统一前端
    ├── styles.css
    └── app.js
```

### 2.2 执行流程

```
┌─────────────────────────────────────────────────────────────┐
│                    CognitiveMeshService                      │
├─────────────────────────────────────────────────────────────┤
│  1. 接收执行请求 (projectId)                                 │
│  2. 查找项目配置 (MeshProject)                               │
│  3. 获取对应策略 (StrategyRegistry)                          │
│  4. 动态加载内容 (论文等外部资源)                            │
│  5. 创建 MeshRun 状态                                        │
│  6. 后台执行策略                                             │
│  7. SSE 推送进度事件                                         │
│  8. 保存结果和产物                                           │
└─────────────────────────────────────────────────────────────┘
```

### 2.3 SSE 事件流

```typescript
// 事件类型
type MeshEvent = 
  | ProgressEvent      // 进度更新
  | VotingEvent        // 投票事件 (MAKER)
  | ProposalEvent      // 提案事件 (MAKER)
  | StreamingEvent     // 流式输出
  | FileEvent          // 文件生成
  | CompleteEvent      // 完成事件
  | ErrorEvent         // 错误事件

// 关键修复：多态序列化
JsonSerializer.Serialize(evt, evt.GetType(), jsonOptions);
//                            ^^^^^^^^^^^^^^
//                            使用运行时类型，确保派生类字段被序列化
```

### 2.4 进度映射

| MAKER 阶段 | 统一进度 |
|-----------|---------|
| `Decomposing` | `Phase: "Decomposing"` |
| `Voting` | `Phase: "Voting"`, `Voting: {...}` |
| `Solving` | `Phase: "Solving"` |
| `Composing` | `Phase: "Composing"` |

| UoT 阶段 | 统一进度 |
|---------|---------|
| `AnalogyRetrieval` | `Phase: "AnalogyRetrieval"`, `AnalogiesFound: N` |
| `ThoughtExtraction` | `Phase: "ThoughtExtraction"`, `ThoughtsExtracted: N` |
| `Synthesis` | `Phase: "Synthesis"`, `CandidatesGenerated: N` |
| `Evaluation` | `Phase: "Evaluation"`, `CandidatesPassed: N` |

### 2.5 当前问题与修复

| 问题 | 根因 | 修复 |
|------|------|------|
| SSE 显示 `[undefined]` | JSON 序列化丢失派生类字段 | 使用 `evt.GetType()` |
| 论文审稿无内容 | Task 只有指令，无论文 | 动态加载论文文件 |
| API Key 缺失 | Provider Name 为空 | 工厂方法设置默认 provider |

---

## 📐 Phase 3: DSL 编译器

**目标**：支持声明式定义认知拓扑

### 3.1 DSL Schema (v0.1)

```yaml
# cognitive-mesh.yaml
mesh:
  id: "paper-review-v1"
  name: "论文审稿系统"
  version: "1.0"

goal:
  description: "审阅并改进学术论文至发表水平"
  success_criteria:
    - "所有关键问题已解决"
    - "语言流畅准确"
    - "结构清晰完整"

budget:
  max_llm_calls: 500
  max_tokens: 2_000_000
  max_duration: "30m"

nodes:
  - id: "structure-review"
    type: "maker"
    config:
      reliability: "high"
      task: "分析论文结构，识别组织问题"
    
  - id: "language-polish"
    type: "maker"
    config:
      reliability: "medium"
      task: "改进语言表达，提升学术性"

  - id: "creative-insights"
    type: "uot-combinational"
    config:
      domain_hint: "academic writing, scientific communication"
      task: "提出创新性改进建议"

edges:
  - from: "structure-review"
    to: "language-polish"
    condition: "structure-review.success"
    
  - from: "structure-review"
    to: "creative-insights"
    parallel: true

constraints:
  - type: "dependency"
    rule: "language-polish AFTER structure-review"
```

### 3.2 编译器流程

```
DSL 文本
    ↓ Parse
MeshDefinition (AST)
    ↓ Validate
验证结果 (结构 + 语义)
    ↓ Compile
执行计划 (节点依赖图)
    ↓ Execute
运行时调度
```

### 3.3 现有 DSL 基础

```
Aevatar.CognitiveMesh.Dsl/
├── Models/
│   └── MeshDefinition.cs      # AST 模型
├── Validation/
│   └── IValidationRule.cs     # 验证规则接口
└── CognitiveDslCompiler.cs    # 编译器入口
```

---

## 🎨 Phase 4: 可视化 UI

**目标**：实时观察、理解、干预认知过程

### 4.1 UI 设计原则

```
传统 Workflow UI: 用户画 → 系统执行
    ↓
Cognitive Mesh UI: AI 生成 → 用户观察/干预
```

### 4.2 视觉组件

| 组件 | 用途 | 数据源 |
|------|------|--------|
| 分解树 | 展示任务分解层级 | MAKER Trace |
| 投票面板 | 展示共识过程 | VotingEvent |
| 类比网络 | 展示跨域连接 | UoT Analogies |
| Token 流 | 实时 LLM 输出 | StreamingEvent |
| 进度条 | 整体完成度 | ProgressPercent |
| 时间线 | 事件历史 | Timeline |

### 4.3 干预命令

```typescript
// 用户可发送的干预命令
type InterventionCommand =
  | { type: "pause" }
  | { type: "resume" }
  | { type: "abort" }
  | { type: "retry", taskId: string }
  | { type: "override", taskId: string, content: string }
  | { type: "adjust_budget", tokens?: number, calls?: number }
```

### 4.4 当前前端状态

- ✅ 项目列表侧边栏
- ✅ 状态芯片（Status, Phase, Depth, Tokens, Calls）
- ✅ 系统日志流
- ✅ 任务表格
- ✅ 共识投票表格
- ✅ Worker 卡片（流式输出）
- ✅ 文件浏览器
- ✅ 最终结果展示
- 📋 分解树可视化（计划中）
- 📋 类比网络图（计划中）

---

## ⚡ Phase 5: 高级功能

### 5.1 百万步可靠性

```
┌─────────────────────────────────────────────────────────────┐
│                    Reliability Stack                         │
├─────────────────────────────────────────────────────────────┤
│  Event Sourcing       事件溯源，完整重放                     │
│  Checkpointing        定期快照，快速恢复                     │
│  Circuit Breaker      熔断保护，防止级联失败                 │
│  Retry with Backoff   指数退避，处理瞬时故障                 │
│  Provider Failover    多提供商，自动切换                     │
└─────────────────────────────────────────────────────────────┘
```

### 5.2 多策略编排

```csharp
// 串行执行
mesh.Then("maker", options1)
    .Then("uot", options2);

// 并行执行
mesh.Parallel(
    ("maker", options1),
    ("uot", options2)
).WaitAll();

// 条件分支
mesh.If(ctx => ctx.Complexity > 0.7)
    .Then("maker-high")
    .Else("maker-medium");
```

### 5.3 知识库集成

```
┌─────────────────────────────────────────────────────────────┐
│                    Knowledge Layer                           │
├─────────────────────────────────────────────────────────────┤
│  Vector Store         类比检索、语义搜索                     │
│  Document Loader      论文、代码、数据加载                   │
│  Memory Bank          历史结果、学习经验                     │
│  Tool Registry        外部工具、API 集成                     │
└─────────────────────────────────────────────────────────────┘
```

### 5.4 评估与优化

```
┌─────────────────────────────────────────────────────────────┐
│                    Evaluation Framework                      │
├─────────────────────────────────────────────────────────────┤
│  A/B Testing          策略对比实验                           │
│  Cost Analysis        Token 消耗分析                         │
│  Quality Metrics      输出质量评分                           │
│  Latency Profiling    延迟瓶颈分析                           │
└─────────────────────────────────────────────────────────────┘
```

---

## 📅 里程碑计划

| 里程碑 | 内容 | 状态 |
|--------|------|------|
| M1 | 策略抽象 + 适配器 | ✅ 完成 |
| M2 | 统一执行引擎 + 基础 UI | 🔄 90% |
| M3 | DSL 编译器 v0.1 | 📋 计划中 |
| M4 | 可视化增强 | 📋 计划中 |
| M5 | 百万步可靠性 | 📋 计划中 |
| M6 | 多策略编排 | 📋 计划中 |

---

## 🔗 相关文档

- [ARCHITECTURE.md](./ARCHITECTURE.md) - 系统架构详解
- [CONCEPT.md](./CONCEPT.md) - 核心概念说明
- [DSL.md](./DSL.md) - DSL 规范
- [WORKFLOW_UI.md](./WORKFLOW_UI.md) - UI 设计文档
- [PRD.md](./PRD.md) - 产品需求文档
- [IMPLEMENTATION_STATUS.md](./IMPLEMENTATION_STATUS.md) - 实现状态

---

*Last Updated: 2025-12-03*

