# Aevatar Agent Framework

🌌 **一份代码，多种运行时** - 多运行时事件驱动智能体框架

[![.NET](https://img.shields.io/badge/.NET-9.0-purple)](https://dotnet.microsoft.com)
[![License](https://img.shields.io/badge/License-MIT-blue)](LICENSE)
[![Status](https://img.shields.io/badge/Status-Production%20Ready-green)](https://github.com/aevatar/aevatar-agent-framework)

## 🎯 核心价值

**写一次 Agent 代码，在多种运行时中自由切换**

```csharp
// 同一份 Agent 代码
public class MyAgent : GAgentBase<MyState> { }

// 切换运行时只需改变一行
services.AddSingleton<IGAgentActorFactory, LocalGAgentActorFactory>();      // 开发测试
services.AddSingleton<IGAgentActorFactory, ProtoActorGAgentActorFactory>(); // 高性能
services.AddSingleton<IGAgentActorFactory, OrleansGAgentActorFactory>();    // 分布式
```

## ⚠️ 重要约束

> **🔴 关键规则：所有需要序列化的类型必须使用 Protobuf 定义！**
> 
> 在使用 Aevatar Agent Framework 时，**所有需要序列化的类型**（Agent State、Event Messages、Event Sourcing Events）都**必须**使用 Protocol Buffers 定义。
>
> **不要手动定义**这些类型的 C# 类，而应该创建 `.proto` 文件并让工具自动生成。这对于 Orleans Streaming 尤其重要，因为它使用 `byte[]` 进行消息传输。
>
> ```protobuf
> // ✅ 正确：使用 proto 定义
> message MyAgentState {
>     string id = 1;
>     int32 count = 2;
>     double balance = 3;  // 注意：decimal 要用 double
> }
> 
> message MyEvent {
>     string event_id = 1;
>     google.protobuf.Any payload = 2;
> }
> ```
> 
> ```csharp
> // ❌ 错误：手动定义 C# 类
> public class MyAgentState  // 不要这样做！
> {
>     public string Id { get; set; }
>     public int Count { get; set; }
> }
> ```
>
> 📖 详见 [序列化规则文档](docs/Serialization_Rules.md)

## ✨ 核心功能

### 🔄 三种运行时
| 运行时 | 特点 | 适用场景 |
|-------|------|---------|
| **Local** | 进程内运行，零配置，<1ms延迟 | 开发、测试、单机应用 |
| **ProtoActor** | Actor模型，高并发，50K msg/s | 高性能服务、实时系统 |
| **Orleans** | 虚拟Actor，自动伸缩，故障恢复 | 分布式系统、云原生 |

### 📨 事件驱动架构
- **Protobuf 消息**：跨平台、高性能序列化
- **智能路由**：Up/Down/UpThenDown/Bidirectional 四种传播模式
- **自动发现**：基于特性的事件处理器自动注册
- **背压控制**：防止消息队列溢出

### 💾 EventSourcing (可选)
- **完整事件记录**：所有状态变更可追溯
- **事件重放**：从事件流恢复状态
- **快照支持**：优化恢复性能
- **多存储后端**：内存、MongoDB、PostgreSQL (计划中)

### 🌳 层级 Agent 管理
- **父子关系**：构建 Agent 树形结构
- **事件传播**：沿层级自动路由事件
- **生命周期管理**：级联激活与停用

### 📊 可观测性
- **内置指标**：事件处理耗时、活跃 Agent 数等
- **结构化日志**：自动包含 Agent 上下文
- **Aspire 集成**：开箱即用的分布式追踪

## 🚀 快速开始

### 1. 定义你的 Agent

```csharp
public class CalculatorAgent : GAgentBase<CalculatorState>
{
    [EventHandler]
    public async Task HandleCalculateEvent(CalculateEvent evt)
    {
        var result = evt.Operation switch
        {
            "+" => evt.A + evt.B,
            "-" => evt.A - evt.B,
            "*" => evt.A * evt.B,
            "/" => evt.A / evt.B,
            _ => throw new NotSupportedException($"Operation {evt.Operation} not supported")
        };
        
        _state.LastResult = result;
        _state.CalculationCount++;
        
        // 发布结果事件
        await PublishAsync(new CalculationResultEvent { Result = result });
    }
}
```

### 2. 选择运行时

```csharp
// Local 运行时 - 最简单
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddSingleton<IGAgentActorFactory, LocalGAgentActorFactory>();
var serviceProvider = services.BuildServiceProvider();

// 创建和使用 Agent
var factory = serviceProvider.GetRequiredService<IGAgentActorFactory>();
var actor = await factory.CreateAgentAsync<CalculatorAgent, CalculatorState>(Guid.NewGuid());

// 发送事件
await actor.PublishEventAsync(
    new CalculateEvent { A = 10, B = 5, Operation = "+" },
    EventDirection.Down
);
```

### 3. EventSourcing 示例

```csharp
public class BankAccountAgent : GAgentBaseWithEventSourcing<BankAccountState>
{
    protected override Task ApplyStateChangeEventAsync<TEvent>(TEvent evt)
    {
        switch (evt)
        {
            case MoneyDeposited deposited:
                _state.Balance += deposited.Amount;
                _state.TransactionCount++;
                break;
                
            case MoneyWithdrawn withdrawn:
                _state.Balance -= withdrawn.Amount;
                _state.TransactionCount++;
                break;
        }
        return Task.CompletedTask;
    }
    
    public async Task DepositAsync(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Amount must be positive");
        
        await RaiseStateChangeEventAsync(new MoneyDeposited 
        { 
            Amount = amount,
            Timestamp = DateTime.UtcNow
        });
    }
}

// 使用 EventSourcing
var eventStore = new InMemoryEventStore();
var bankAccount = new BankAccountAgent(Guid.NewGuid(), eventStore);

await bankAccount.DepositAsync(1000);
await bankAccount.WithdrawAsync(200);

// 从事件恢复
var recovered = new BankAccountAgent(bankAccount.Id, eventStore);
await recovered.OnActivateAsync(); // 自动重放所有事件
Console.WriteLine($"Balance: {recovered.State.Balance}"); // 800
```

## 🏗️ 架构设计

框架采用清晰的分层架构，实现了业务逻辑与运行时的完全解耦：

```
┌─────────────────────────────────────────────────────────┐
│                    业务应用层                            │
├─────────────────────────────────────────────────────────┤
│                 运行时抽象层 (IGAgentActor)              │
├──────────────┬──────────────────┬──────────────────────┤
│    Local     │   ProtoActor     │      Orleans         │
├──────────────┴──────────────────┴──────────────────────┤
│                业务逻辑层 (GAgentBase)                   │
├─────────────────────────────────────────────────────────┤
│            EventSourcing / Streaming / Metrics          │
└─────────────────────────────────────────────────────────┘
```

> 详细架构文档请查看 [ARCHITECTURE.md](ARCHITECTURE.md)

## 📦 项目结构

```
src/
├── Abstractions/        # 核心接口定义
├── Core/               # Agent 基类实现
├── Local/              # Local 运行时
├── ProtoActor/         # ProtoActor 运行时
├── Orleans/            # Orleans 运行时
└── Serialization/      # Protobuf 序列化

examples/
├── SimpleDemo/         # 入门示例
├── EventSourcingDemo/  # EventSourcing 示例
└── Demo.Api/          # Web API 集成示例

test/
└── *.Tests/           # 单元测试
```

## 📊 性能基准

| 指标 | Local | ProtoActor | Orleans |
|-----|-------|-----------|---------|
| **启动时间** | < 1ms | ~10ms | ~100ms |
| **消息延迟** | < 0.1ms | < 1ms | < 5ms |
| **吞吐量** | 100K msg/s | 50K msg/s | 20K msg/s |
| **内存/Agent** | ~50KB | ~100KB | ~500KB |
| **并发 Agents** | 10,000+ | 50,000+ | 100,000+ |

## 📚 文档

- 📖 [架构设计](ARCHITECTURE.md) - 详细架构说明与设计决策
- 🚀 [快速开始](docs/Quick_Start_Guide.md) - 5分钟上手指南
- 🌟 [高级示例](docs/Advanced_Agent_Examples.md) - 复杂场景示例
- 📦 [Protobuf 配置](docs/Protobuf_Configuration_Guide.md) - 消息定义指南
- 📊 [Aspire 集成](docs/Aspire_Integration_Guide.md) - 可观测性配置
- 🔄 [流实现](docs/Streaming_Implementation.md) - 消息流详解
- 📝 [序列化规则](docs/Serialization_Rules.md) - Protobuf 最佳实践

## 🎯 适用场景

### ✅ 非常适合
- **微服务架构**：每个服务可选择合适的运行时
- **事件驱动系统**：原生事件路由和处理
- **CQRS/EventSourcing**：内置支持，可选使用
- **实时系统**：低延迟消息传递
- **游戏服务器**：Actor 模型天然适合游戏实体

### ⚠️ 需要评估
- **简单 CRUD**：可能过度设计
- **同步调用为主**：事件驱动需要思维转变
- **极简应用**：框架有一定学习曲线

## 🛠️ 技术栈

- **运行时**: .NET 9.0
- **序列化**: Google Protobuf 3.27+
- **Actor框架**: Proto.Actor 1.0+
- **分布式**: Microsoft Orleans 9.0+
- **测试**: xUnit + FluentAssertions

## 🚦 项目状态

**版本**: 1.0.0-release
**状态**: 生产就绪 ✅

- ✅ 核心功能完整
- ✅ 三种运行时稳定
- ✅ EventSourcing 支持
- ✅ 完整测试覆盖
- ✅ 生产环境验证

## 🤝 贡献

欢迎贡献代码、文档或想法！请查看 [贡献指南](CONTRIBUTING.md)。

## 🙏 致谢

- Microsoft Orleans 团队的虚拟 Actor 模型
- Proto.Actor 的高性能 Actor 实现  
- Google Protobuf 的优秀序列化方案
- .NET 社区的持续支持