# MAKER Agent Mode

## 概述

MAKER 现在支持两种执行模式：

1. **Pool 模式** (默认) - 使用 `ExecutionPool` 直接调用 LLM，简单高效
2. **Agent 模式** - 使用 Aevatar Agent Framework，支持分布式和大规模场景

## 架构

### Pool 模式

```
MakerExecutor
    ├── ExecutionPool (并行 LLM 调用)
    └── VoteEngine (共识投票)
```

### Agent 模式

```
AgentMakerExecutor
    └── MakerCoordinatorGAgent (AI Agent)
        ├── AIGAgentBase (LLM 能力)
        ├── VoteEngine (共识投票)
        └── 可选: 子 Agent 协调
```

## 使用方式

### Pool 模式 (默认)

```csharp
// Program.cs
services.AddMEAI();
services.AddMakerV2(poolSize: 3, temperatureVariance: 0.1f);
```

### Agent 模式

```csharp
// Program.cs
services.AddMEAI();
services.AddMakerV2WithAgents(defaultProviderName: "default");

// 或者指定模式
services.AddMakerSystem(MakerExecutionMode.Agent, defaultProviderName: "default");
```

## MakerCoordinatorGAgent

核心协调器 Agent，继承自 `AIGAgentBase<MakerCoordinatorState, MakerCoordinatorConfig>`。

### 特性

- **LLM 集成**: 通过 `AIGAgentBase` 获得完整的 LLM 调用能力
- **状态管理**: Protobuf 定义的状态，支持持久化和 EventSourcing
- **事件驱动**: 可发布进度事件，支持分布式监控
- **策略可配置**: 支持自定义分解、求解和组合策略

### 状态定义 (Protobuf)

```protobuf
message MakerCoordinatorState {
    string execution_id = 1;
    string task_description = 2;
    int32 max_depth = 3;
    int32 consensus_k = 4;
    int32 samples_per_round = 5;
    int32 total_llm_calls = 6;
    int32 status = 7;
    // ...
}
```

### 配置定义 (Protobuf)

```protobuf
message MakerCoordinatorConfig {
    int32 default_consensus_k = 1;
    int32 default_max_depth = 2;
    string llm_provider_name = 5;
    float base_temperature = 6;
    float temperature_variance = 7;
    // ...
}
```

## 兼容性

Agent 模式完全兼容现有的 `IMakerExecutor` 接口，可以无缝替换 Pool 模式：

```csharp
// 两种模式使用相同的接口
IMakerExecutor executor = ...; // Pool 或 Agent
var result = await executor.ExecuteAsync(task, options);
```

## 运行时选择

| 场景 | 推荐模式 | 理由 |
|------|---------|------|
| 开发/测试 | Pool | 简单，无需配置 Actor |
| 单机生产 | Pool | 性能最优 |
| 分布式系统 | Agent | 支持 Orleans/ProtoActor |
| 需要状态持久化 | Agent | 内置 EventSourcing |
| 监控/可观测性 | Agent | 事件驱动，易于追踪 |

## 扩展到 Orleans

未来可以将 `MakerCoordinatorGAgent` 部署到 Orleans Silo：

```csharp
// 配置 Orleans 运行时
services.AddAevatarAgentSystem(builder =>
{
    builder.UseOrleansRuntime(siloBuilder => { /* ... */ });
});

// Agent 代码无需修改
services.AddMakerV2WithAgents();
```

## 事件流

Agent 模式支持发布执行进度事件：

```csharp
// 订阅事件
actor.SubscribeToStream(envelope => {
    if (envelope.Payload.Is<MakerProgressEvent>()) {
        var progress = envelope.Payload.Unpack<MakerProgressEvent>();
        Console.WriteLine($"[{progress.Phase}] {progress.Message}");
    }
});
```

