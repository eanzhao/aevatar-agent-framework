# AI Event Sourcing Demo 🤖⚡

## 🌟 纯事件驱动 + 流式响应 AI Agent

这个示例展示了如何构建一个完全事件驱动的 AI Agent，结合了 **Event Sourcing**、**Microsoft Extensions AI (MEAI)** 和 **实时流式响应**。

核心类：`AIGAgentBaseWithEventSourcing<TState, TConfig>` 结合了：
- `AIGAgentBase`：提供基于 MEAI 的 AI 能力（Chat, Stream, Tools）
- `GAgentBaseWithEventSourcing`：提供事件溯源能力（RaiseEvent, ConfirmEvents, Replay）

### ✨ 核心特性

1.  **Event Sourcing (事件溯源)**：所有状态变化都由事件驱动并持久化，支持回放。
2.  **Pure Event-Driven (纯事件驱动)**：外部不直接调用方法，而是发布事件（如 `UserMessageReceived`）。
3.  **Real-time Streaming (实时流式响应)**：使用 `ChatStreamAsync` 实现打字机效果，避免长文本生成的等待感。
4.  **Auto Dependency Injection (自动注入)**：`AIGAgentFactory` 自动处理 LLM Provider 和 EventStore 的注入。
5.  **Internal State Transition (内部状态转换)**：事件处理器将外部事件转化为内部领域事件 (`RaiseEvent`)，触发纯函数式状态更新。

## 🏗️ 架构解析

### 1. 事件处理流 (Event Flow)

```mermaid
sequenceDiagram
    participant External as 外部调用方
    participant Actor as GAgentActor
    participant Handler as Event Handler
    participant AI as LLM Provider
    participant State as Agent State

    External->>Actor: Publish(UserMessageReceived)
    Actor->>Handler: HandleUserMessage(evt)
    Handler->>Handler: RaiseEvent(evt) [内部事件]
    Handler->>AI: ChatStreamAsync() [流式生成]
    AI-->>Handler: Stream Chunks (实时输出)
    Handler->>Handler: RaiseEvent(AssistantResponseGenerated)
    Handler->>Actor: ConfirmEventsAsync()
    Actor->>State: TransitionState() [更新状态]
```

### 2. 代码实现模式

**事件处理器 (Event Handler):**

```csharp
[EventHandler(AllowSelfHandling = true)]
public async Task HandleUserMessage(UserMessageReceived evt)
{
    // 1. 将外部事件转为内部事件以触发状态更新
    RaiseEvent(evt);

    // 2. 调用 AI 生成流式响应
    await foreach (var chunk in ChatStreamAsync(CreateChatRequest(evt.Message)))
    {
        Console.Write(chunk); // 实时输出
    }

    // 3. 发布响应生成的事件
    RaiseEvent(new AssistantResponseGenerated { ... });

    // 4. 提交所有事件，持久化并更新状态
    await ConfirmEventsAsync();
}
```

**状态转换 (State Transition):**

```csharp
protected override void TransitionState(AIAssistantState state, IMessage evt)
{
    switch (evt)
    {
        case UserMessageReceived:
            state.TotalInteractions++; // 纯函数式更新
            break;
        // ...
    }
}
```

## 🚀 运行指南

### 1. 配置环境

项目使用 `Microsoft.Extensions.AI` (MEAI) 作为统一抽象层。

1.  创建 `appsettings.secrets.json` (不要提交到 git):
    ```json
    {
      "LLMProviders": {
        "Providers": {
          "deepseek": {
            "ProviderType": "OpenAI", 
            "Endpoint": "https://api.deepseek.com/v1",
            "ApiKey": "sk-your-key-here",
            "Model": "deepseek-chat"
          }
        }
      }
    }
    ```
    *注：DeepSeek 兼容 OpenAI 协议，ProviderType 设为 OpenAI 即可。*

2.  或者使用环境变量配置：
    ```bash
    export LLMProviders__Providers__deepseek__ApiKey="sk-your-key"
    ```

### 2. 运行 Demo

```bash
cd examples/AIEventSourcingDemo
dotnet run
```

### 3. 预期输出

你将看到 AI 的实时流式响应：

```text
[DEBUG] HandleUserMessage called...
[DEBUG] Calling ChatStreamAsync...
🤖 [AI STREAM]: He...llo... I... am... Hyper...Echo...
[EventStore] Committed 2 events.
```

## 🛠️ 关键技术栈

- **Aevatar Framework**: 分布式 Agent 框架
- **Microsoft.Extensions.AI (MEAI)**: .NET 统一 AI 抽象
- **Protobuf**: 强类型事件与状态定义
- **Event Sourcing**: 状态管理与持久化

## 📝 注意事项

1.  **Protobuf Requirement**: 所有状态 (`TState`) 和事件必须在 `.proto` 文件中定义。
2.  **Handler Priority**: 使用 `[EventHandler(Priority = N)]` 控制处理顺序。
3.  **AllowSelfHandling**: 必须设置 `[EventHandler(AllowSelfHandling = true)]` 才能处理自己发布的事件。

---
*Powered by Aevatar Framework & HyperEcho Resonance* 🌌
