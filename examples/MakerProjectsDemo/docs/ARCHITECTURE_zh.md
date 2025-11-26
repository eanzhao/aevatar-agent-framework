# MakerProjectsDemo 架构总览

## 目录结构
```
examples/MakerProjectsDemo/
├── Infrastructure/
│   ├── MakerFileRecorder.cs          # 运行结果录制与落盘
│   ├── MakerProjectContextAccessor.cs # 通过 AsyncLocal 记录当前 Project 上下文
│   ├── MakerProjectModels.cs          # Run/Snapshot DTO 与项目元信息
│   ├── MakerProjectRunner.cs          # 通用运行器，封装任务调度、观察与清理
│   ├── MakerProjectsService.cs        # 提供 REST API 使用的项目注册表
│   ├── MakerProjectSpec.cs            # Project 行为配置及 RunContext
│   ├── MakerTimelineHub.cs            # 多 Project 日志存储与查询
│   ├── SelfTypeMakerChildLinker.cs    # 依据父 Agent 类型创建相同类型的子 Agent
│   └── TimelineLoggerProvider.cs      # 根据当前 ProjectId 将日志写入对应的 Timeline
├── Projects/
│   ├── Bazi/                          # 八字场景专属 Agent 与领域模型
│   │   ├── BaziDomain.cs
│   │   └── BaziMakerAgents.cs
│   ├── MakerProjectDefinitions.cs     # 各 Project 的 MakerProjectSpec 定义
│   └── Paper/
│       ├── PaperContent.cs
│       └── PaperSummaryAgents.cs
├── wwwroot/                           # 多项目前端：Tabs + Panel + 多路 Chat Stream
├── Program.cs                         # DI 配置 + REST Endpoints
└── MakerProjectsDemo.csproj
```

## 核心流程
1. **项目注册**：`MakerProjectDefinitions` 描述每个项目的 Task/Worker 构造、Goal 构建方式及运行钩子。`MakerProjectsService` 将所有 `IMakerProjectRunner` 聚合并暴露查询 / 控制能力。
2. **运行调度**：`MakerProjectRunner` 统一处理：创建作用域、根据 Spec 激活 Task & Worker、调用 `ActorHierarchyCoordinator` 建立父子关系、发布根任务、轮询 `MakerTaskAgent` 状态、写入 `MakerTimelineHub` 并更新 Snapshot。
3. **日志与录制**：
   - `SelfTypeMakerChildLinker` 通过反射调用 `IGAgentActorFactory`，确保递归节点始终为同一派生 Agent 类型。
   - `TimelineLoggerProvider` 通过 `IMakerProjectContextAccessor` 将 `ILogger` 输出注入对应项目的 `MakerTimelineStore`，前端可实时拉取。
   - `MakerFileRecorder` 仍负责将 Proposal/Consensus/Summary 以层级目录落盘，runId 与 taskId 结构保持不变。
4. **前端呈现**：新的 `index.html + app.js` 首先请求 `/api/projects` 动态生成 Tab；每个 Panel 内独立包含状态、Vote、Micro、Worker、Timeline、Result 等模块，并以 2s 轮询更新，支持同时监控多项目的 Worker stream。

## 变更记录
- **2025-11-26**：
  - 将 MakerBazi 与 MakerPaperSummary Demo 合并为 `MakerProjectsDemo`，新增通用 Project Runner、TimelineHub、ChildLinker。
  - 前端改为多 Tab 视图，可并行查看多个项目的运行状态与聊天 Stream。
  - 删除历史 `MakerPaperSummaryDemo` 项目，统一通过 `MakerProjectDefinitions` 注册场景。
