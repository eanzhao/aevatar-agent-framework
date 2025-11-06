# AI Agent Framework - 命名更改总结

## 📋 更改概览

为了避免与第三方库（如 Semantic Kernel、OpenAI SDK、Microsoft AutoGen 等）的命名冲突，我们为所有公共接口和类型添加了 `Aevatar` 前缀。

## 🔄 主要命名更改

### 接口重命名
| 原名称 | 新名称 |
|--------|--------|
| `ILLMProvider` | `IAevatarLLMProvider` |
| `IPromptManager` | `IAevatarPromptManager` |
| `IAIToolManager` | `IAevatarToolManager` |
| `IAIMemory` | `IAevatarMemory` |

### 基类重命名
| 原名称 | 新名称 |
|--------|--------|
| `AIGAgentBase<TState>` | `AevatarAIAgentBase<TState>` |

### 核心类型重命名
| 原名称 | 新名称 |
|--------|--------|
| `LLMRequest` | `AevatarLLMRequest` |
| `LLMResponse` | `AevatarLLMResponse` |
| `LLMToken` | `AevatarLLMToken` |
| `LLMSettings` | `AevatarLLMSettings` |
| `ChatMessage` | `AevatarChatMessage` |
| `ChatRole` | `AevatarChatRole` |
| `FunctionCall` | `AevatarFunctionCall` |
| `FunctionDefinition` | `AevatarFunctionDefinition` |
| `TokenUsage` | `AevatarTokenUsage` |

### 工具相关类型
| 原名称 | 新名称 |
|--------|--------|
| `AITool` | `AevatarTool` |
| `ToolParameters` | `AevatarToolParameters` |
| `ToolParameter` | `AevatarToolParameter` |
| `ToolExecutionResult` | `AevatarToolExecutionResult` |
| `ExecutionContext` | `AevatarExecutionContext` |

### 提示词管理类型
| 原名称 | 新名称 |
|--------|--------|
| `PromptTemplate` | `AevatarPromptTemplate` |
| `TemplateParameter` | `AevatarTemplateParameter` |
| `ThoughtStep` | `AevatarThoughtStep` |
| `Example` | `AevatarExample` |

### 记忆管理类型
| 原名称 | 新名称 |
|--------|--------|
| `ConversationMessage` | `AevatarConversationMessage` |
| `MemoryItem` | `AevatarMemoryItem` |
| `RecalledMemory` | `AevatarRecalledMemory` |
| `RecallOptions` | `AevatarRecallOptions` |
| `ContextScope` | `AevatarContextScope` |

### 配置和属性
| 原名称 | 新名称 |
|--------|--------|
| `AIAgentConfiguration` | `AevatarAIAgentConfiguration` |
| `AIEventHandlerAttribute` | `AevatarAIEventHandlerAttribute` |
| `AIProcessingMode` | `AevatarAIProcessingMode` |
| `AIContext` | `AevatarAIContext` |

### Protobuf 消息
| 原名称 | 新名称 |
|--------|--------|
| `AIAgentState` | `AevatarAIAgentState` |
| `AIConfiguration` | `AevatarAIConfiguration` |
| `AIProcessingRequest` | `AevatarAIProcessingRequest` |
| `AIProcessingResponse` | `AevatarAIProcessingResponse` |
| `AIErrorEvent` | `AevatarAIErrorEvent` |
| `AIMetricsEvent` | `AevatarAIMetricsEvent` |
| `ThoughtStepEvent` | `AevatarThoughtStepEvent` |
| `ToolExecutedEvent` | `AevatarToolExecutedEvent` |

## 📁 文件更改

- `AIGAgentBase.cs` → `AevatarAIAgentBase.cs`

## 💡 迁移示例

### Before
```csharp
public class MyAgent : AIGAgentBase<MyState>
{
    protected override void ConfigureAI(AIAgentConfiguration config)
    {
        // configuration
    }
    
    [AIEventHandler]
    protected async Task<IMessage?> HandleEvent(EventEnvelope evt)
    {
        // handler logic
    }
}
```

### After
```csharp
public class MyAgent : AevatarAIAgentBase<MyState>
{
    protected override void ConfigureAI(AevatarAIAgentConfiguration config)
    {
        // configuration
    }
    
    [AevatarAIEventHandler]
    protected async Task<IMessage?> HandleEvent(EventEnvelope evt)
    {
        // handler logic
    }
}
```

## 🎯 为什么要这样做？

1. **避免冲突**: 像 `ChatMessage`、`ChatRole`、`LLMRequest` 这样的名称在多个 AI SDK 中都存在
2. **清晰标识**: 立即识别哪些类型属于 Aevatar 框架
3. **IntelliSense 友好**: 输入 "Aevatar" 即可看到所有框架类型
4. **专业性**: 清晰的命名空间分离显示成熟的设计
5. **未来兼容**: 新的第三方库不会造成命名冲突

## ⚡ 快速查找

需要查找某个类型？在 IDE 中：
- 输入 `IAevatar` 查找所有接口
- 输入 `Aevatar` 查找所有类型
- 使用 "Go to Symbol" 功能搜索具体类型

## 📝 注意事项

- 具体的 Provider 实现类（如 `SemanticKernelProvider`）不需要 `Aevatar` 前缀
- 内部/私有类型不需要前缀
- 扩展方法通常不需要前缀（除非扩展 Aevatar 类型）

---

*更新日期: 2024-01*
*框架版本: 1.0.0-alpha*

