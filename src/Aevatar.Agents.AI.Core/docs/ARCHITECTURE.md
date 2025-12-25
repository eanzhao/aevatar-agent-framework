# Aevatar.Agents.AI.Core — 架构说明

## 目标

- **单一入口**：`Aevatar.Agents.AI.Core` 是 AI Agent 能力的唯一工程入口。
- **工具能力内置**：`AIGAgentBase` 内置 Function Calling + Tool Call Loop；工具系统实现也随 `AI.Core` 一并发布。
- **兼容性优先**：工具相关类型 **保留命名空间** `Aevatar.Agents.AI.WithTool.*`，但它们 **由 `AI.Core` 程序集提供**（不再存在独立的 `Aevatar.Agents.AI.WithTool` 工程）。

## 目录结构（关键子集）

```
src/Aevatar.Agents.AI.Core/
├── AIGAgentBase.cs
├── AIGAgentBase.Tools.cs                 # Tool loop / 默认工具注册 / allowlist 等
├── AIGAgentBase.AgentSkills.cs           # SKILL.md 按需加载（可动态注册 dotnet-file tools）
├── Helpers/
├── Messages/
├── WithTool/                             # 工具系统实现（原 AI.WithTool）
│   ├── Abstractions/                     # ToolDefinition / IAevatarToolManager 等
│   ├── Tools/                            # AevatarToolManager + 内置/核心/自定义工具
│   ├── MCP/                              # Model Context Protocol 支持
│   └── tool_messages.proto               # Tool 相关事件/消息（Protobuf）
└── ai_messages.proto
```

## 依赖边界

- **对外**：业务工程只需要引用 `Aevatar.Agents.AI.Core`（即可获得工具/MCP 能力）。
- **对内**：工具系统代码位于 `WithTool/` 目录，但仍使用 `Aevatar.Agents.AI.WithTool.*` 命名空间以避免破坏上层代码。

## 变更记录（合并 WithTool）

- **Removed**：独立工程 `src/Aevatar.Agents.AI.WithTool`（工程级别）。
- **Moved**：原 WithTool 源码迁移至 `src/Aevatar.Agents.AI.Core/WithTool/`，作为 `AI.Core` 的一部分编译。
- **Protobuf**：`tool_messages.proto` 由 `AI.Core` 统一生成代码（满足“跨边界类型必须 Protobuf”铁律）。


