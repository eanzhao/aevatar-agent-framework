# Cognitive Mesh

> **认知网格** - 统一的思维策略执行平台
> 
> 将 MakerSystem（可靠推理）与 CreativeSystem（创造推理）合并的统一架构

---

## 🎯 核心愿景

```
Prompt Engineering  →  Cognitive Architecture Engineering
   (手工调试)              (声明式认知编排)
```

将不同的 AI 推理策略（CoT、ToT、GoT、UoT、MAKER）统一在同一框架下：

- **统一抽象**：`IReasoningStrategy` 接口统一所有策略
- **统一执行**：`CognitiveMeshService` 管理项目和运行
- **统一进度**：`ReasoningProgress` 实时报告各策略状态
- **统一结果**：`ReasoningResult` 可比较、可分析

---

## 📁 项目结构

```
cognitive-mesh/
├── Aevatar.CognitiveMesh.Abstractions/     # 核心抽象接口
│   ├── IReasoningStrategy.cs               # 推理策略接口
│   ├── StrategyKind.cs                     # 策略类型枚举
│   ├── ReasoningOptions.cs                 # 执行选项
│   ├── ReasoningProgress.cs                # 进度报告模型
│   ├── ReasoningResult.cs                  # 结果模型
│   ├── ValidationResult.cs                 # 验证结果
│   ├── IMeshProject.cs                     # 项目接口
│   └── IMeshEventSink.cs                   # SSE 事件接口
│
├── Aevatar.CognitiveMesh/                  # 主服务实现
│   ├── Program.cs                          # ASP.NET Core 入口
│   ├── Services/
│   │   ├── CognitiveMeshService.cs         # 核心服务
│   │   └── StrategyRegistry.cs             # 策略注册表
│   ├── Strategies/
│   │   ├── MakerStrategy.cs                # MAKER 策略适配器
│   │   └── UoTCombinationalStrategy.cs     # UoT 策略适配器
│   ├── Models/
│   │   └── MeshRun.cs                      # 运行状态模型
│   └── wwwroot/                            # 前端 UI
│       ├── index.html
│       ├── app.js
│       └── styles.css
│
├── Aevatar.CognitiveMesh.Dsl/              # DSL 编译器
│   ├── CognitiveDslCompiler.cs             # 编译器入口
│   ├── Models/
│   │   └── MeshDefinition.cs               # DSL 数据模型
│   └── Validation/                         # 校验规则
│
├── Aevatar.CognitiveMesh.AppHost/          # Aspire 编排
│   └── Program.cs
│
└── docs/                                   # 设计文档
    ├── README.md                           # 本文档
    ├── ROADMAP.md                          # 🆕 渐进式路线图
    ├── IMPLEMENTATION_STATUS.md            # 🆕 实现状态
    ├── ARCHITECTURE.md                     # 架构参考
    ├── CONCEPT.md                          # 核心概念
    ├── DSL.md                              # DSL 规范
    ├── PRD.md                              # 产品需求
    └── WORKFLOW_UI.md                      # UI 设计
```

---

## 🚀 快速开始

### 1. 配置 API Key

创建 `cognitive-mesh/Aevatar.CognitiveMesh/appsettings.secrets.json`:

```json
{
  "LLMProviders": {
    "Providers": {
      "deepseek": {
        "ApiKey": "sk-your-deepseek-key"
      },
      "claude": {
        "ApiKey": "sk-your-anthropic-key"
      }
    }
  }
}
```

### 2. 运行

```bash
cd cognitive-mesh/Aevatar.CognitiveMesh
dotnet run
```

### 3. 打开浏览器

访问 `http://localhost:5000`

### 4. 选择项目并启动

- 选择内置项目（如"论文审稿"或"桥梁交通"）
- 点击 **START** 按钮
- 观察实时日志和进度

---

## 🧠 支持的策略

| 策略 | 类型 | 状态 | 描述 |
|------|------|------|------|
| **MAKER** | `Maker` | ✅ 可用 | 分解-共识-合成，多 Agent 投票 |
| **UoT Combinational** | `UotCombinational` | ✅ 可用 | 类比检索 + 思维合成 |
| **UoT Exploratory** | `UotExploratory` | 📋 规划中 | 并行蒙特卡洛搜索 |
| **UoT Transformative** | `UotTransformative` | 📋 规划中 | 元提示范式转换 |
| **Chain of Thought** | `Cot` | 📋 规划中 | 线性推理链 |
| **Tree of Thoughts** | `Tot` | 📋 规划中 | 分支与剪枝 |
| **Graph of Thoughts** | `Got` | 📋 规划中 | 分支重组 |

---

## 📐 核心抽象

### IReasoningStrategy

所有思维策略的统一接口：

```csharp
public interface IReasoningStrategy
{
    StrategyKind Kind { get; }
    string DisplayName { get; }
    string Description { get; }
    
    Task<ReasoningResult> ExecuteAsync(
        string problem,
        ReasoningOptions options,
        IProgress<ReasoningProgress>? progress = null,
        CancellationToken ct = default);
    
    ValidationResult ValidateOptions(ReasoningOptions options);
}
```

### ReasoningOptions

策略无关的通用配置 + 策略特定配置：

```csharp
// 通用配置
var options = new ReasoningOptions
{
    ProviderName = "deepseek",
    MaxLlmCalls = 500,
    MaxTokens = 2_000_000,
    MaxDuration = TimeSpan.FromMinutes(30)
};

// MAKER 特定（工厂方法）
options = ReasoningOptions.ForMaker(
    reliability: MakerReliability.High,
    maxLlmCalls: 100,
    providerName: "deepseek"
);

// UoT 特定（工厂方法）
options = ReasoningOptions.ForUotCombinational(
    domainHint: "transportation, distributed systems",
    maxAnalogies: 5,
    providerName: "claude"
);
```

### ReasoningProgress

统一进度报告（包含策略特有字段）：

```csharp
public sealed record ReasoningProgress
{
    // ─── 通用字段 ───
    public required string Phase { get; init; }
    public float ProgressPercent { get; init; }
    public string? Message { get; init; }
    public string? TaskId { get; init; }
    
    // ─── MAKER 特有 ───
    public int? Depth { get; init; }
    public VotingProgress? Voting { get; init; }
    public ProposalProgress? Proposal { get; init; }
    public StreamingTokenProgress? StreamingToken { get; init; }
    
    // ─── UoT 特有 ───
    public int? AnalogiesFound { get; init; }
    public int? ThoughtsExtracted { get; init; }
    public int? CandidatesGenerated { get; init; }
    public int? CandidatesPassed { get; init; }
}
```

---

## 🔌 API Endpoints

| Endpoint | Method | 描述 |
|----------|--------|------|
| `/api/strategies` | GET | 列出可用策略 |
| `/api/projects` | GET | 列出所有项目 |
| `/api/projects/{id}` | GET | 获取单个项目 |
| `/api/projects/{id}/run` | POST | 启动执行 |
| `/api/projects/{id}/status` | GET | 获取运行状态 |
| `/api/projects/{id}/snapshot` | GET | 获取运行快照 |
| `/api/projects/{id}/events` | GET (SSE) | 实时事件流 |
| `/api/projects/{id}/files` | GET | 获取生成的文件列表 |
| `/api/projects/{id}/files/{cat}/{name}` | GET | 获取文件内容 |
| `/api/sample-problems` | GET | 获取 UoT 示例问题 |

---

## 🗺️ 演进路线

详见 [ROADMAP.md](./ROADMAP.md)

| 阶段 | 目标 | 状态 |
|------|------|------|
| **Phase 1** | 策略抽象层 | ✅ 完成 |
| **Phase 2** | 统一执行引擎 | 🔄 90% |
| **Phase 3** | DSL 编译器 | 📋 计划中 |
| **Phase 4** | 可视化增强 | 📋 计划中 |
| **Phase 5** | 高级功能 | 📋 计划中 |

---

## 📖 文档索引

| 文档 | 说明 |
|------|------|
| [ROADMAP.md](./ROADMAP.md) | 渐进式设计路线图，详细的阶段规划 |
| [IMPLEMENTATION_STATUS.md](./IMPLEMENTATION_STATUS.md) | 当前实现状态、已解决问题、已知限制 |
| [ARCHITECTURE.md](./ARCHITECTURE.md) | 系统架构详解 |
| [CONCEPT.md](./CONCEPT.md) | 核心概念说明（Actor Model、UoT 模式） |
| [DSL.md](./DSL.md) | DSL 规范（声明式认知拓扑） |
| [PRD.md](./PRD.md) | 产品需求文档 |
| [WORKFLOW_UI.md](./WORKFLOW_UI.md) | UI 设计文档 |

---

## 🔗 相关资源

- [MAKER Paper](https://arxiv.org/abs/2411.00332) - 分解-共识-合成的理论基础
- [Universe of Thoughts](https://arxiv.org/html/2511.20471v2) - 创意推理框架
- [Aevatar Agent Framework](../../README.md) - 底层 Actor 框架

---

## 🐛 常见问题

### Q: SSE 事件显示 `[undefined]`

**A**: JSON 序列化问题，已修复。确保使用最新代码。

### Q: 论文审稿提示"请提供论文"

**A**: 已修复。现在会自动加载 `articles/minimal_axiomatic_ontology_universe.md`。

### Q: Provider 创建失败

**A**: 检查 `appsettings.secrets.json` 是否正确配置 API Key。

详见 [IMPLEMENTATION_STATUS.md](./IMPLEMENTATION_STATUS.md) 的"已解决问题"部分。

---

*Last Updated: 2025-12-03*
