# Aevatar Agent Framework 单元测试设计

## 1. 概述

本文档描述了 Aevatar Agent Framework 的完整测试策略和实现状态。框架采用分层测试架构，确保每个组件的质量和可靠性。

### 测试覆盖率现状

- **Abstractions层**: ~95% ✅ (30个测试)
- **Core层**: ~92% ✅ (105个测试)
- **Local Runtime**: ~70% ⚠️ (12个测试)
- **Orleans Runtime**: ~30% ❌
- **ProtoActor Runtime**: ~20% ❌
- **整体覆盖率**: ~75% ✅

### 目标覆盖率

- 代码行覆盖率: > 80%
- 分支覆盖率: > 75%
- 方法覆盖率: > 90%
- 所有公共API: 100%覆盖

## 2. 测试原则和方法论

### 有价值的测试

1. **测试业务逻辑**：验证实际的业务规则和计算
2. **测试集成点**：验证组件之间的交互
3. **测试错误处理**：确保异常情况被正确处理
4. **测试边界条件**：验证极端输入的处理
5. **测试状态转换**：验证状态机的正确性

### 应避免的测试

1. **纯 Mock 测试**：只验证 Mock 框架的行为
2. **接口契约测试**：接口没有逻辑，无需测试
3. **Getter/Setter 测试**：简单属性不需要测试
4. **框架功能测试**：不要测试第三方框架
5. **编译器保证的测试**：类型系统已经保证的不需要测试

### 测试的 ROI（投资回报率）

- **高 ROI**：核心业务逻辑、复杂算法、关键路径
- **中 ROI**：辅助功能、数据验证、格式转换
- **低 ROI**：简单 CRUD、纯粹的数据传递、UI 布局

## 3. 测试工具和框架

### 必需工具
- **xUnit**: 测试框架
- **Shouldly**: 断言库
- **Moq**: Mock框架（仅用于模拟外部依赖）
- **FluentAssertions**: 高级断言（可选）

### Mock 框架使用原则
- **正确使用**：模拟外部依赖（如数据库、网络服务、文件系统）
- **避免使用**：测试接口契约（接口没有实现，测试 Mock 没有意义）
- **谨慎使用**：过度 Mock 会降低测试的可信度

### 测试数据要求
- 所有测试用的State和Config类型必须定义在.proto文件中
- 使用TestMessages.proto定义测试专用的消息类型
- 遵循Protobuf序列化要求

## 4. 架构改进和最佳实践

### 4.1 State和Config保护机制

通过`StateProtectionContext`实现Actor模型一致性：
- **State修改限制**: 只能在EventHandler或OnActivateAsync中修改
- **Config修改限制**: 与State相同的保护规则
- **DEBUG警告**: 开发模式下提示不当访问
- **内部实现**: StateProtectionContext为internal，通过InternalsVisibleTo暴露给测试

### 4.2 配置隔离机制

IConfigStore的改进确保配置正确隔离：
- **复合键设计**: 使用`AgentType.FullName:AgentId`作为键
- **类型安全**: 不同Agent类型的配置完全隔离
- **MongoDB支持**: 使用复合唯一索引确保隔离

### 4.3 事件驱动的状态管理

TreeNodeAgent示例展示最佳实践：
- **事件定义**: SetParentEvent, AddChildEvent, RemoveChildEvent
- **处理器实现**: 使用[EventHandler(AllowSelfHandling = true)]
- **测试辅助**: SetupTreeNodeForTesting通过反射调用处理器

### 4.4 测试最佳实践

- **避免StateStore注入**: 测试时可能与状态保护冲突
- **使用测试辅助方法**: 通过OnActivateAsync设置初始状态
- **反射调用处理器**: 测试时模拟事件处理的正确上下文

## 5. Abstractions层测试 ✅

### 5.1 Messages (消息相关)

#### EventEnvelope测试 (16个测试)
- 事件封装器的创建和序列化
- 复杂负载的序列化
- 空负载处理
- 传播控制
- 发布者链跟踪
- 事件方向处理
- 时间戳生成和序列化
- 必填字段验证
- 特殊字符处理
- 大负载处理
- 克隆支持
- 相等性实现
- 版本号处理
- 关联ID处理

### 5.2 Attributes (属性相关)

#### EventHandlerAttribute测试
- 默认优先级为 0（最高优先级）
- 属性的可设置性（包括AllowSelfHandling）
- 通过反射发现属性
- 只能应用于方法

#### AllEventHandlerAttribute测试
- 默认优先级为 int.MaxValue（最低优先级）
- 优先级可覆盖
- 通过反射发现
- 只能应用于方法

#### 属性优先级和多属性测试
- 处理器按优先级正确排序
- AllEventHandler默认优先级低于特定处理器
- 方法不支持多个相同属性
- 不同处理器属性可以共存

### 5.3 Persistence (持久化相关)

**IConfigStore更新**：
- 包含`Type agentType`参数以隔离不同Agent类型的配置
- MongoDB实现已更新，支持复合唯一索引
- InMemoryConfigStore使用复合键设计
- 配置隔离性测试完整

## 6. Core层测试 ✅

### 6.1 GAgentBase核心测试 (19个测试)

#### 状态管理
- Agent基类的状态初始化
- Agent状态的Protobuf序列化
- Agent状态的修改和保存

#### 配置管理
- Agent配置的加载（包含隔离性测试）
- Agent配置的自定义设置
- 配置的默认值

#### 生命周期
- Agent的激活流程
- Agent的停用流程
- Agent的重新激活

#### 复杂场景
- 复杂嵌套状态处理
- 带嵌套消息的复杂状态序列化

### 6.2 事件处理测试

#### EventHandler发现和执行 (7个测试)
- 事件处理器的自动发现
- 带EventHandler属性的方法发现
- 按约定命名的处理器发现
- 事件处理器的同步执行
- 事件处理器的优先级排序
- 多个处理器的顺序执行
- AllEventHandler的处理

#### EventPublishing (10个测试)
- 向上发布事件（UP方向）
- 向下发布事件（DOWN方向）
- 双向发布事件（BOTH方向）
- 事件发布的异常处理
- 跟踪多个事件发布
- 处理自发布事件
- 事件元数据管理

#### ExceptionHandler (8个测试)
- 捕获处理器异常且不传播
- 处理器抛出异常时发布异常事件
- 异常事件包含堆栈跟踪
- 处理器异常不影响其他处理器
- 继续处理事件
- 处理AllEventHandler中的异常
- 处理不同类型的异常
- 异常事件包含所有必需的细节

### 6.3 State和Config保护测试 (6个测试) ✨

#### StateProtectionContext测试
- State只能在事件处理器中修改
- State可以在OnActivateAsync中初始化
- 直接State赋值保护（Protobuf属性无法拦截）

#### ConfigProtectionTests
- Config在非允许上下文中的直接赋值保护
- Config在事件处理器中可修改
- Config属性修改无法拦截（Protobuf限制）

### 6.4 GAgentActorBase框架层测试 (14个测试)

#### 父子关系管理
- 设置父Actor
- 清除父Actor关系
- 添加子Actor
- 移除子Actor

#### 事件发布和路由
- 向上发布事件到父节点
- 向下发布事件到子节点
- 双向发布事件
- 无父节点时的UP事件处理
- 无子节点时的DOWN事件处理

#### Actor生命周期
- Actor激活
- Actor停用
- 获取Agent描述

### 6.5 其他核心组件测试

#### BaseSubscriptionManager (11个测试)
- 订阅句柄的创建和管理
- 订阅的健康检查机制
- 订阅的清理机制
- 创建失败时的重试逻辑
- 重连机制

#### InMemoryEventStore (12个测试)
- 事件追加和乐观并发控制
- 事件查询和分页
- 版本管理
- 快照功能
- 多Agent隔离性

#### ResourceContext (12个测试)
- 资源添加和移除
- 资源元数据管理
- 类型安全获取

#### PerformanceTests (6个测试)
- 大量事件的处理性能
- 并发事件处理
- 内存使用
- 优先级处理器性能
- 状态持久化性能
- 事件路由性能

### 6.6 集成测试 (4个测试)

- 完整生命周期测试
- 多Agent协作场景
- Agent树形结构的事件传播（使用事件驱动的TreeNodeAgent）
- Agent的状态恢复

## 7. Runtime层测试

### 7.1 Local Runtime ✅ (12个测试)

#### 基础功能
- 创建和激活Local Actor
- 本地处理事件
- 支持层级关系
- 基于方向路由事件
- 处理并发事件
- 正确停用

#### 关系管理
- 清除父关系
- 移除子关系
- 多个Agent独立工作

#### 泛型支持
- 使用单泛型参数创建Agent
- 单双泛型创建相同Agent
- 事件传播遵循方向语义

### 7.2 Orleans Runtime ⚠️ (部分实现)

#### 待实现测试
- Orleans流的创建和初始化
- 序列化机制（byte[]）
- 分布式事件传播
- Stream Provider集成
- 命名空间管理
- 订阅持久化和恢复
- 背压处理

### 7.3 ProtoActor Runtime ⚠️ (部分实现)

#### 待实现测试
- ProtoActor流的创建和初始化
- EventStream集成
- 订阅管理
- 事件路由
- 订阅清理

## 8. 测试组织结构

### 命名规范

#### 测试类命名
- 格式: `{被测类名}Tests`
- 例如: `GAgentBaseTests`, `EventEnvelopeTests`

#### 测试方法命名
- 格式: `{方法名}_Should_{预期行为}_When_{条件}`
- 简化: `Should_{预期行为}`

#### DisplayName规范
- 使用简洁的英文描述
- 以"Should"开头描述预期行为
- 避免技术术语，使用业务语言

### 项目结构

```
test/
├── Aevatar.Agents.Abstractions.Tests/
│   ├── AttributesTests.cs
│   └── Messages/
│       └── EventEnvelopeTests.cs
│
├── Aevatar.Agents.Core.Tests/
│   ├── GAgentBaseTests.cs
│   ├── GAgentActorBaseTests.cs
│   ├── ConfigProtectionTests.cs
│   ├── EventHandlerTests.cs
│   ├── EventPublishingTests.cs
│   ├── ExceptionHandlerTests.cs
│   ├── BaseSubscriptionManagerTests.cs
│   ├── InMemoryEventStoreTests.cs
│   ├── ResourceContextTests.cs
│   ├── PerformanceTests.cs
│   └── IntegrationTests.cs
│
├── Aevatar.Agents.Local.Tests/
│   └── LocalGAgentActorTests.cs
│
├── Aevatar.Agents.Orleans.Tests/
│   └── (待实现)
│
└── Aevatar.Agents.ProtoActor.Tests/
    └── (待实现)
```

### 测试项目依赖关系

```
Aevatar.Agents.Abstractions.Tests
  └── 引用: Abstractions项目

Aevatar.Agents.Core.Tests
  ├── 引用: Core项目
  ├── 引用: Abstractions项目
  └── 引用: Runtime.Local项目（用于测试抽象功能）

Aevatar.Agents.Local.Tests
  ├── 引用: Runtime.Local项目
  ├── 引用: Core项目
  └── 引用: Abstractions项目
```

## 9. 测试优先级

### P0 - 必须测试（核心功能）
- GAgentBase的状态管理 ✅
- 事件处理器的发现和执行 ✅
- 事件发布机制 ✅
- 父子关系管理 ✅
- State和Config保护机制 ✅

### P1 - 重要测试（主要功能）
- 配置管理 ✅
- 异常处理 ✅
- 事件过滤 ✅
- LocalSubscriptionManager ❌
- Orleans Runtime Stream实现 ❌
- ProtoActor Runtime Stream实现 ❌

### P2 - 补充测试（边缘情况）
- 性能测试 ✅
- 并发测试 ⚠️
- 内存泄漏测试 ❌
- 极端情况测试 ⚠️

## 10. 待完成工作

### 高优先级
1. LocalSubscriptionManager测试
2. LocalMessageStream测试
3. 完整的集成测试套件

### 中优先级
1. Orleans Runtime完整测试套件
2. ProtoActor Runtime完整测试套件
3. 跨Runtime兼容性测试

### 低优先级
1. 错误边界测试（网络故障、序列化失败、超时、死锁）
2. 安全性测试（事件篡改防护、权限验证、安全序列化）
3. 监控诊断测试（日志验证、指标收集、追踪支持）

## 11. 测试执行指南

### 运行所有测试
```bash
dotnet test
```

### 运行特定层的测试
```bash
dotnet test test/Aevatar.Agents.Core.Tests
```

### 运行特定测试
```bash
dotnet test --filter "FullyQualifiedName~GAgentBase"
```

### 生成覆盖率报告
```bash
dotnet test --collect:"XPlat Code Coverage"
```

## 12. 持续改进

### 测试质量指标
1. **测试可读性**: 清晰的命名和结构
2. **测试可维护性**: 避免重复，使用辅助方法
3. **测试可靠性**: 避免时序依赖和随机失败
4. **测试性能**: 快速执行，并行运行

### 测试评审要点
1. 是否覆盖了所有关键路径？
2. 是否包含了边界条件测试？
3. 是否有适当的错误处理测试？
4. 测试是否独立且可重复？
5. 是否遵循了命名和组织规范？

## 12. Aevatar.Agents.AI.Abstractions Tests 🤖

### 12.1 LLMProvider Tests

#### IAevatarLLMProvider Interface Tests
- **GenerateAsync_ShouldReturnValidResponse**: 验证基本文本生成功能，确保返回有效响应和token计数
- **GenerateStreamAsync_ShouldStreamTokens**: 测试流式生成能力，验证token流和完成标记
- **GetModelInfoAsync_ShouldReturnInfo**: 验证模型信息获取
- **GenerateAsync_WithInvalidRequest_ShouldThrow**: 测试无效请求的错误处理
- **GenerateAsync_WithCancellation_ShouldCancel**: 验证取消令牌的正确处理

#### LLMProviderFactory Tests  
- **GetProviderAsync_WithValidName_ShouldReturnProvider**: 测试按名称获取配置的provider
- **GetProviderAsync_WithInvalidName_ShouldThrow**: 验证无效名称的错误处理
- **CreateProvider_WithCustomConfig_ShouldWork**: 测试使用自定义配置创建provider
- **GetDefaultProviderAsync_ShouldReturnDefault**: 验证默认provider获取
- **GetAvailableProviderNames_ShouldReturnAll**: 测试获取所有可用provider名称

### 12.2 Tool System Tests 🔧

#### IAevatarTool Interface Tests
- **ExecuteAsync_WithValidParameters_ShouldReturnResult**: 验证工具执行的正确性
- **ValidateParameters_WithInvalidInput_ShouldDetectErrors**: 测试参数验证逻辑
- **ValidateParameters_WithMissingRequired_ShouldFail**: 验证必需参数缺失的处理
- **CreateToolDefinition_ShouldIncludeMetadata**: 测试工具定义创建，包含所有元数据
- **ExecuteAsync_WithTimeout_ShouldRespectLimit**: 验证超时配置的遵守
- **CreateParameters_ShouldDefineCorrectTypes**: 测试参数定义的类型正确性

#### IAevatarToolManager Tests
- **RegisterToolAsync_ShouldAddTool**: 测试工具注册功能
- **RegisterToolAsync_DuplicateName_ShouldHandleCorrectly**: 验证重复工具名称的处理
- **ExecuteToolAsync_NonExistent_ShouldReturnError**: 测试不存在工具的错误处理
- **ExecuteToolAsync_DisabledTool_ShouldFail**: 验证禁用工具的执行阻止
- **GetAvailableToolsAsync_ShouldReturnOnlyEnabled**: 确保只返回启用的工具
- **GenerateFunctionDefinitionsAsync_ShouldMapCorrectly**: 验证LLM函数定义生成
- **EnableToolAsync_DisableToolAsync_ShouldToggleState**: 测试工具启用/禁用状态切换

### 12.3 Processing Strategy Tests 🧠

#### ChainOfThought Strategy Tests
- **CanHandle_WithReasoningQuestion_ShouldReturnTrue**: 验证策略能识别推理型问题
- **EstimateComplexity_ShouldReturnExpectedValue**: 测试复杂度评估的准确性
- **ProcessAsync_ShouldGenerateMultipleThoughtSteps**: 验证生成多个思考步骤
- **ProcessAsync_WithHighConfidenceConclusion_ShouldStopEarly**: 测试高置信度结论的早停机制
- **ProcessAsync_ReachingMaxSteps_ShouldSummarize**: 验证达到最大步骤时的总结行为
- **ValidateRequirements_WithoutLLMProvider_ShouldFail**: 测试依赖验证

#### ReAct Strategy Tests
- **CanHandle_WithToolRequiredQuestion_ShouldReturnTrue**: 验证策略识别需要工具的问题
- **ProcessAsync_ShouldAlternateThoughtAndAction**: 测试思考-行动交替模式
- **ProcessAsync_ShouldExecuteToolsCorrectly**: 验证工具执行的正确性
- **ProcessAsync_WithToolFailure_ShouldHandleGracefully**: 测试工具失败的优雅处理
- **ProcessAsync_ReachingMaxIterations_ShouldStop**: 验证迭代限制的遵守
- **IsTaskComplete_WithSufficientObservations_ShouldReturnTrue**: 测试任务完成判断逻辑

### 12.4 Memory Management Tests 💾

#### IAevatarAIMemory Tests
- **AddMessageAsync_ShouldStoreMessage**: 验证消息存储功能
- **GetConversationHistoryAsync_ShouldReturnInOrder**: 测试历史记录的顺序性
- **GetConversationHistoryAsync_WithLimit_ShouldRespectLimit**: 验证历史记录限制
- **ClearHistoryAsync_ShouldRemoveAllMessages**: 测试清空历史记录
- **SearchAsync_ShouldReturnRelevantResults**: 验证语义搜索相关性
- **SearchAsync_WithTopK_ShouldLimitResults**: 测试搜索结果数量限制

### 12.5 Prompt Management Tests

#### IAevatarPromptManager Tests
- **GetSystemPromptAsync_WithKey_ShouldReturnCorrectPrompt**: 验证系统提示词获取
- **GetSystemPromptAsync_WithInvalidKey_ShouldReturnDefault**: 测试无效key的默认处理
- **FormatPromptAsync_ShouldReplaceVariables**: 验证模板变量替换
- **FormatPromptAsync_WithMissingVariables_ShouldHandleGracefully**: 测试缺失变量的处理
- **BuildChatPromptAsync_ShouldMaintainMessageOrder**: 验证聊天提示词构建的消息顺序

## 13. Aevatar.Agents.AI.Core Tests 🎯

### 13.1 AIGAgentBase Tests

#### Initialization Tests
- **InitializeAsync_WithProviderName_ShouldInitializeCorrectly**: 测试使用provider名称初始化
- **InitializeAsync_WithCustomConfig_ShouldOverrideDefaults**: 验证自定义配置覆盖默认值
- **InitializeAsync_CalledTwice_ShouldIgnoreSecondCall**: 测试重复初始化的幂等性
- **UninitializedAgent_AccessingLLMProvider_ShouldThrow**: 验证未初始化状态的错误处理
- **InitializeAsync_WithStateStore_ShouldLoadState**: 测试状态存储加载
- **InitializeAsync_WithConfigStore_ShouldLoadConfig**: 验证配置存储加载

#### Chat Functionality Tests
- **ChatAsync_ShouldReturnValidResponse**: 验证基本聊天功能
- **ChatAsync_ShouldPublishChatResponseEvent**: 测试聊天响应事件发布
- **ChatStreamAsync_ShouldStreamTokens**: 验证流式响应生成
- **BuildLLMRequest_ShouldIncludeSystemPrompt**: 测试LLM请求构建包含系统提示词
- **GetLLMSettings_WithRequestOverrides_ShouldUseRequestValues**: 验证请求级设置覆盖
- **SupportsStreamingAsync_ShouldReflectProviderCapability**: 测试流式支持能力查询

### 13.2 AIGAgentWithToolBase Tests 🔨

#### Tool Registration Tests
- **RegisterTools_ShouldAddToManager**: 验证工具注册到管理器
- **RegisterToolAsync_WithIAevatarTool_ShouldCreateDefinition**: 测试IAevatarTool接口的工具注册
- **GetRegisteredTools_ShouldReturnAllTools**: 验证获取所有已注册工具
- **HasTools_WithRegisteredTools_ShouldReturnTrue**: 测试工具存在性检查
- **CreateToolManager_ShouldReturnValidManager**: 验证工具管理器创建
- **UpdateActiveToolsInState_ShouldReflectCurrentTools**: 测试状态中活动工具的更新

#### Tool Execution Tests  
- **ChatWithToolAsync_WithFunctionCall_ShouldExecuteTool**: 验证带函数调用的聊天
- **ChatWithToolAsync_WithoutFunctionCall_ShouldNotExecuteTool**: 测试无函数调用时的正常聊天
- **ExecuteToolAsync_ShouldDelegateToManager**: 验证工具执行委托给管理器
- **HandleFunctionCallAsync_ShouldProcessCorrectly**: 测试函数调用处理流程
- **HandleToolExecutionRequestEvent_ShouldPublishResponse**: 验证工具执行事件处理
- **ParseToolArguments_WithInvalidJson_ShouldReturnEmpty**: 测试无效JSON参数解析
- **BuildLLMRequestWithTools_ShouldIncludeFunctionDefinitions**: 验证LLM请求包含函数定义

### 13.3 Tool Implementation Tests 🛠️

#### DefaultToolManager Tests
- **ConcurrentRegistration_ShouldBeThreadSafe**: 验证并发注册的线程安全性
- **RegisterToolAsync_WithCannotOverride_ShouldIgnoreDuplicate**: 测试不可覆盖工具的重复注册
- **ExecuteToolAsync_NonExistentTool_ShouldReturnError**: 验证不存在工具的执行错误
- **DisableToolAsync_ShouldPreventExecution**: 测试禁用工具阻止执行
- **EnableToolAsync_ShouldAllowExecution**: 验证启用工具允许执行
- **GetAvailableToolsAsync_ShouldOnlyReturnEnabled**: 测试只返回启用的工具
- **HasTool_ShouldCheckExistence**: 验证工具存在性检查
- **ConvertToFunctionParameters_ShouldMapTypesCorrectly**: 测试参数类型转换

#### Built-in Tools Tests
- **AevatarEventPublisherTool_ShouldPublishEventCorrectly**: 验证事件发布工具
- **AevatarMemorySearchTool_ShouldSearchMemory**: 测试内存搜索工具
- **EventPublisherTool_ValidateParameters_ShouldRequireEventType**: 验证事件发布参数验证
- **StateQueryTool_ShouldQueryAgentState**: 测试状态查询工具

### 13.4 Strategy Implementation Tests

#### ChainOfThoughtProcessingStrategy Tests
- **ProcessAsync_ShouldGenerateMultipleThoughtSteps**: 验证生成多个思考步骤
- **ProcessAsync_WithHighConfidenceConclusion_ShouldStopEarly**: 测试高置信度早停
- **ParseThoughtStep_ShouldExtractStructuredInfo**: 验证思考步骤解析
- **SummarizeThoughtsAsync_ShouldCombineAllSteps**: 测试思考步骤总结

#### ReActProcessingStrategy Tests
- **ProcessAsync_ShouldAlternateThoughtActionObservation**: 验证思考-行动-观察循环
- **DetermineActionAsync_WithFunctionCall_ShouldReturnAction**: 测试函数调用动作确定
- **ExecuteActionAndObserveAsync_ShouldHandleErrors**: 验证动作执行错误处理
- **IsTaskCompleteAsync_WithSufficientInfo_ShouldReturnTrue**: 测试任务完成判断
- **GenerateFinalAnswerAsync_ShouldSynthesizeObservations**: 验证最终答案生成

#### TreeOfThoughtsProcessingStrategy Tests
- **ProcessAsync_ShouldExploreMultiplePaths**: 验证多路径探索
- **EvaluatePath_ShouldScoreCorrectly**: 测试路径评分机制

### 13.5 Integration Tests 🔄

- **AIAgent_CompleteConversation_WithTools**: 测试完整对话流程，包含工具调用
- **AIAgent_ConversationHistory_ShouldMaintain**: 验证对话历史维护
- **AIAgent_WithProcessingStrategy_ShouldSelectAppropriately**: 测试策略自动选择
- **AIAgent_MultipleToolCalls_ShouldExecuteInSequence**: 验证多个工具调用的顺序执行
- **AIAgent_ErrorRecovery_ShouldContinueConversation**: 测试错误恢复后继续对话

## 14. 测试辅助工具 🧪

### 需要创建的Mock和Helper（放在Aevatar.Agents.Core.Tests.Agents中）

#### AI相关的Mock Providers
- **MockLLMProvider**: 模拟LLM提供者，支持预定义响应队列
- **MockStreamingLLMProvider**: 模拟流式LLM提供者
- **MockToolManager**: 模拟工具管理器
- **MockPromptManager**: 模拟提示词管理器
- **MockMemory**: 模拟内存管理器

#### AI测试Agents（继承自现有Agent基类）
- **TestAIAgent**: 基础AI测试代理（继承AIGAgentBase）
- **TestAIAgentWithTools**: 带工具的AI测试代理（继承AIGAgentWithToolBase）
- **TestAIAgentWithStrategy**: 带策略的AI测试代理
- **TestCustomerServiceAgent**: 客服场景测试代理
- **TestWeatherAgent**: 天气查询测试代理

#### Test Data Builders
- **AITestDataBuilder**: 创建AI相关测试数据
- **ToolDefinitionBuilder**: 构建工具定义
- **LLMRequestBuilder**: 构建LLM请求
- **StrategyDependenciesBuilder**: 构建策略依赖

### 断言规范（使用Shouldly）
- 使用 `result.ShouldNotBeNull()` 替代 `Assert.NotNull(result)`
- 使用 `result.Content.ShouldNotBeEmpty()` 替代 `Assert.NotEmpty(result.Content)`
- 使用 `result.Success.ShouldBeTrue()` 替代 `Assert.True(result.Success)`
- 使用 `tools.Count.ShouldBe(2)` 替代 `Assert.Equal(2, tools.Count)`
- 使用 `Should.Throw<InvalidOperationException>()` 替代 `Assert.ThrowsAsync`

## 15. 测试覆盖率要求 📊

### AI.Abstractions
- **LLMProvider接口**: 90%+ 覆盖率
- **工具系统**: 85%+ 覆盖率
- **策略接口**: 80%+ 覆盖率
- **内存管理**: 85%+ 覆盖率
- **提示词管理**: 80%+ 覆盖率

### AI.Core
- **AIGAgentBase**: 90%+ 覆盖率
- **AIGAgentWithToolBase**: 85%+ 覆盖率
- **策略实现**: 80%+ 覆盖率
- **工具管理器**: 90%+ 覆盖率
- **内置工具**: 75%+ 覆盖率

### 关键测试场景
1. **LLM交互**: 请求/响应、流式生成、错误处理、取消令牌
2. **工具执行**: 注册、验证、执行、错误恢复、并发安全
3. **策略选择**: 自动选择、手动覆盖、策略切换、依赖验证
4. **内存管理**: 历史记录、搜索、清理、限制遵守
5. **并发安全**: 工具注册、状态管理、事件处理
6. **初始化流程**: Provider配置、状态加载、配置加载
7. **事件发布**: 聊天响应事件、工具执行事件、思考步骤事件

### 测试组织结构
- 所有测试类使用 `public class [ClassName]Tests` 命名
- 测试方法使用 `public async Task [Method]_[Condition]_[ExpectedResult]()` 格式
- 复用 `Aevatar.Agents.Core.Tests.Agents` 中的现有组件
- 新增的测试辅助类都放在该项目中

---

**文档版本**: 3.0
**最后更新**: 新增 Aevatar.Agents.AI.Abstractions 和 Aevatar.Agents.AI.Core 单元测试设计
**维护者**: Aevatar Agent Framework Team