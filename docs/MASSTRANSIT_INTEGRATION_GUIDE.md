# MassTransit Stream Plugin 集成指南

## 1. 架构与原理

MassTransit Stream 插件为 Aevatar 框架提供了基于消息队列（如 Kafka, RabbitMQ）的事件流支持，采用**插件化架构**设计，使核心逻辑与底层传输解耦。

### 核心组件架构

该架构采用“依赖倒置”原则，插件不直接依赖运行时，而是通过抽象接口进行交互。

1.  **公共层 (Plugins.MassTransit)**
    *   **`MassTransitMessageStreamProvider`**: 负责创建和管理流实例。
    *   **`StreamMessageDispatcher`**: MassTransit 的消费者（Consumer）。负责监听消息队列，收到消息后，根据 `StreamId` 将消息分发给内存中的 `IMessageStream`。
    *   **关键机制**: 如果内存中找不到对应的 Stream（意味着 Actor 未激活），Dispatcher 会调用 `IStreamNotFoundHandler` 尝试唤醒 Actor。

2.  **抽象层 (Abstractions)**
    *   **`IStreamNotFoundHandler`**: 定义了“当消息到达但接收者不在家时该怎么办”的策略接口。
    *   **`[StreamTopic]` Attribute**: 用于 Agent 类上的声明式 Topic 路由配置。

3.  **运行时适配层 (Runtime Adaptation)**
    *   **Orleans Runtime**: 实现了 `OrleansStreamNotFoundHandler`。利用 Virtual Actor 特性，通过 `GrainFactory.GetGrain(...).ActivateAsync()` 自动唤醒 Grain。
    *   **Local Runtime**: 实现了 `LocalStreamNotFoundHandler`。由于 Local Runtime 是内存模型，不支持自动唤醒（除非手动创建），因此该 Handler 主要负责记录警告日志或抛出异常以触发重试。

### 数据流向图 (跨 Agent 通信)

```mermaid
graph TD
    A[Sender Agent] -->|Publish| B(GAgentActor)
    B -->|GetStream(Category)| C[MassTransitMessageStreamProvider]
    C -->|Lookup Topic| D{Topic Mapping?}
    
    D -->|Found| E[Target Topic]
    D -->|Not Found| F[Default Topic (TopicPrefix)]
    
    E -->|ProduceAsync (Key=StreamId)| G((Kafka))
    F -->|ProduceAsync (Key=StreamId)| G
    
    G -->|MassTransit Consumer| H[StreamMessageDispatcher]
    H -->|Dispatch by StreamId| I{Stream Exists?}
    
    I -->|Yes| J[MassTransitMessageStream]
    I -->|No| K[IStreamNotFoundHandler]
    
    K -->|Orleans Mode| L[OrleansStreamNotFoundHandler]
    L -->|Activate| M(Orleans Grain)
    M -->|Register Stream| J
    
    K -->|Local Mode| N[LocalStreamNotFoundHandler]
    N -->|Log Warning / Error| O[End / Retry]
    
    J -->|Deserialize| P[EventEnvelope]
    P -->|HandleEventAsync| Q(GAgentActor)
    Q -->|ProcessEvent| R[Receiver Agent]
```

---

## 2. 动态 Topic 路由与配置

MassTransit 插件支持灵活的 **Category -> Topic** 映射机制，允许将不同类型的 Agent 路由到不同的 Kafka Topic。

### 优先级规则

路由配置遵循以下优先级（由高到低）：

1.  **`appsettings.json` 配置**: 运维人员可以在部署时覆盖一切代码设定。
2.  **`[StreamTopic]` 注解**: 开发者在代码中声明的默认 Topic。
3.  **类名回退**: 如果无注解，使用 Agent 的类名作为 Category。
4.  **默认 Topic**: 如果 Category 没有在映射表中找到对应 Topic，则使用全局的 `TopicPrefix` (默认 `agent-events`)。

### 方式一：使用注解（推荐）

在 Agent 类上直接声明目标 Topic：

```csharp
[StreamTopic("finance-billing")]
public class BillingAgent : GAgentBase<BillingState>
{
    // ...
}
```

在启动时，插件会自动扫描并注册该 Topic。

### 方式二：使用配置文件

在 `appsettings.json` 中配置映射：

```csharp
"MassTransit": {
  "Stream": {
    "TopicPrefix": "agent-events",
    "TopicMapping": {
      "BillingAgent": "finance-billing-v2", // 覆盖代码中的 v1
      "UserAgent": "user-events"
    }
  }
}
```

---

## 3. Runtime 集成指南

### 通用步骤 (适用于所有 Runtime)

1.  **引用插件**:
    ```xml
    <ProjectReference Include="..\..\..\src\Aevatar.Agents.Plugins.MassTransit\Aevatar.Agents.Plugins.MassTransit.csproj" />
    ```

2.  **代码集成 (支持自动扫描)**:
    在 `Program.cs` 中注册插件时，传入包含 Agent 的程序集：

    ```csharp
    // 自动扫描程序集中的 [StreamTopic] 注解
    services.AddMassTransitStreamPlugin(
        configuration, 
        typeof(MyAgent).Assembly,
        typeof(AnotherAgent).Assembly
    );
    ```

### Orleans Runtime 集成

```csharp
host.ConfigureServices((context, services) =>
{
    services.AddMassTransitStreamPlugin(context.Configuration, typeof(MyAgent).Assembly);

    services.AddAevatarAgentSystem(builder =>
    {
        builder.UseOrleansRuntime(context.Configuration);
    });
});
```

### Local Runtime 集成

```csharp
services.AddMassTransitStreamPlugin(configuration, typeof(MyAgent).Assembly);
services.AddAevatarLocalRuntime();
```

---

## 4. 性能优化特性

插件内置了多项针对 Kafka 的性能优化：

1.  **Partition Ordering (Key-based)**:
    Producer 发送消息时，使用 `StreamId` 作为 Kafka Message Key。这确保了同一个 Agent 的所有消息都会落入同一个 Partition，从而利用 Kafka 原生的分区顺序性保证消息处理顺序，无需在 Consumer 端进行昂贵的重排序。

2.  **Batch Processing**:
    Consumer 配置了 `CheckpointMessageCount = 100` 和 `CheckpointInterval = 5s`，启用批量提交 Offset，大幅减少 Kafka 交互开销。

3.  **Concurrency**:
    默认启用 `UseConcurrencyLimit(50)`，允许单个 Consumer 实例并发处理不同 Partition 的消息，最大化吞吐量。

---

## 5. 验证与测试工具

### 性能基准测试 (Orleans vs MassTransit)

位于 `apps/Aevatar.App/benchmarks`。

**运行方式**：
```bash
cd apps/Aevatar.App/benchmarks
./run-benchmark-comparison.sh
```

**最新 Benchmark 结果 (Kafka Mode)**：
*   **MassTransit**: ~12,500 msg/s (得益于批处理和并发优化)
*   **Orleans Stream**: ~13.5 msg/s (受限于逐条确认机制)

---

## 6. 常见问题排查

1.  **消息发送到了错误的 Topic？**
    *   检查 `appsettings.json` 中的 `TopicMapping` 是否覆盖了预期配置。
    *   检查 Agent 类上的 `[StreamTopic]` 注解是否正确。
    *   如果没有配置，消息会默认发送到 `TopicPrefix` 指定的 Topic。

2.  **收不到消息？**
    *   **Orleans**: 检查 Silo 日志中是否包含 `No stream found`，如果数量在减少，说明 Auto-Activation 正在工作。
    *   **Local**: Local 模式不支持自动唤醒，必须先创建 Agent。

3.  **Kafka Consumer Group 冲突？**
    *   所有 Topic 默认使用配置中的同一个 Consumer Group ID。MassTransit 会自动管理订阅。
