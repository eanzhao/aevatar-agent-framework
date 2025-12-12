# Aevatar.Agents.Cognitive 架构文档

## 目录结构

```
Aevatar.Agents.Cognitive/
├── Agents/                      # Agent 实现
│   ├── CognitiveCoordinatorGAgent.cs  # 工作流协调器
│   └── CognitiveWorkerGAgent.cs       # 并行 Worker
├── Engine/                      # 工作流引擎
│   └── WorkflowParser.cs              # YAML 工作流解析
├── Events/                      # 事件处理
│   └── StepEventEmitter.cs            # 步骤事件发射器
├── Execution/                   # 执行器
│   └── FanOutExecutor.cs              # Fan-out 并行执行器
├── Primitives/                  # DSL 原语
│   ├── IPrimitive.cs                  # 原语接口和上下文
│   ├── LlmCallPrimitive.cs            # LLM 调用
│   ├── VotePrimitive.cs               # 投票共识
│   ├── ConditionalPrimitive.cs        # 条件分支
│   ├── WorkflowCallPrimitive.cs       # 工作流调用
│   ├── CheckpointPrimitive.cs         # 检查点
│   └── WorkflowResult.cs              # 执行结果
├── Template/                    # 模板引擎
│   ├── TemplateEngine.cs              # 模板渲染
│   └── OutputParser.cs                # 输出解析
├── Utilities/                   # 工具类
│   ├── ProtoValueConverter.cs         # Protobuf 值转换
│   └── ParameterResolver.cs           # 参数解析
├── DependencyInjection/         # DI 扩展
│   └── ServiceCollectionExtensions.cs
├── workflows/                   # 内置工作流定义
│   ├── maker-v2.yaml                  # MAKER 系统 v2
│   ├── axiom_theorem_loop.yaml        # 公理 → 定理发现循环（Coordinator 提出，Workers 证明）
│   ├── direct.yaml                    # 直接执行
│   └── uot-combinational.yaml         # UoT 组合
└── cognitive_messages.proto     # Protobuf 消息定义
```

## 核心组件

### 1. CognitiveCoordinatorGAgent
**职责**: 工作流协调与执行

- 解析并执行 YAML 定义的工作流
- 协调 Worker 执行并行任务
- 管理投票共识流程
- 发送步骤事件供前端可视化

### 2. CognitiveWorkerGAgent
**职责**: 并行任务执行

- 接收 Coordinator 派发的任务
- 执行 LLM 调用（支持流式）
- 向上报告执行结果

### 3. FanOutExecutor
**职责**: 并行任务管理

- 准备并行子任务
- 收集执行结果
- 聚合最终输出

```csharp
// 使用示例
var executor = new FanOutExecutor(templateEngine, logger);
var tasks = executor.Prepare(step, childStep, items, variables, executionId);
// 分发任务...
var (results, tokens, calls) = executor.CollectResults("collect");
```

### 4. StepEventEmitter
**职责**: 事件发射

- 统一管理步骤事件的创建
- 跟踪执行时间
- 发送回调通知

```csharp
// 使用示例
var emitter = new StepEventEmitter(logger);
emitter.SetContext(runId, workflowName);
emitter.OnStepEvent += evt => SendToFrontend(evt);
emitter.EmitRunning(step, "Starting...");
emitter.EmitCompleted(step, "Done", assistantResponse: response);
```

### 5. ProtoValueConverter
**职责**: Protobuf 转换

- C# 对象 ↔ Protobuf Value
- Dictionary ↔ Protobuf Struct

```csharp
// 使用示例
var protoValue = ProtoValueConverter.ToProto(myObject);
var csharpObject = ProtoValueConverter.FromProto(protoValue);
```

### 6. ParameterResolver
**职责**: 参数解析

- 从步骤参数解析类型化值
- 支持模板变量替换
- 解析 Red-Flag 策略

```csharp
// 使用示例
var resolver = new ParameterResolver(templateEngine, variables);
var k = resolver.GetInt(parameters, "k", 2);
var prompt = resolver.GetString(parameters, "prompt");
var redFlag = resolver.GetRedFlagStrategy(parameters, defaultStrategy);
```

### 7. TypeConverter
**职责**: 通用类型转换

- 对象转列表
- 结果聚合（collect/flatten/first/last/concat）

## 设计原则

1. **单一职责**: 每个类只做一件事
2. **依赖注入**: 通过构造函数注入依赖
3. **事件驱动**: Actor 间通过 Protobuf 事件通信
4. **可测试性**: 核心逻辑可独立测试
5. **静态工具优先**: 无状态操作使用静态方法

## 数据流

```
YAML Workflow
     │
     ▼
┌─────────────────────┐
│   WorkflowParser    │ ──解析──▶ WorkflowDefinition
└─────────────────────┘
     │
     ▼
┌─────────────────────┐
│ CognitiveCoordinator│ ──协调──▶ 步骤执行
└─────────────────────┘
     │
     ├──────────────────┐
     ▼                  ▼
┌──────────┐     ┌──────────────┐
│ 直接执行  │     │ FanOutExecutor│ ──派发──▶ Workers
└──────────┘     └──────────────┘
     │                  │
     ▼                  ▼
┌─────────────────────┐
│  StepEventEmitter   │ ──推送──▶ 前端可视化
└─────────────────────┘
```

## 组件依赖关系

```
CognitiveCoordinatorGAgent
├── TemplateEngine          # 模板渲染
├── OutputParserFactory     # 输出解析
├── FanOutExecutor          # 并行管理（可选）
├── StepEventEmitter        # 事件发射（可选）
├── ParameterResolver       # 参数解析
├── ProtoValueConverter     # Protobuf 转换
└── VoteEngine (MAKER)      # 投票共识
```

## 扩展点

1. **自定义步骤类型**: 在 `ExecuteStepAsync` 中添加新的 case
2. **自定义输出解析器**: 实现 `IOutputParser` 接口
3. **自定义 Red-Flag 策略**: 实现 `IRedFlagStrategy` 接口
4. **自定义聚合器**: 在 `TypeConverter.ApplyReducer` 中添加
