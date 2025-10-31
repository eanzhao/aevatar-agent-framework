# Aevatar Agent Framework - 快速开始指南

## 🚀 5分钟快速上手

### 1. 创建你的第一个 Agent

```csharp
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

// 定义 Agent 状态
public class MyAgentState
{
    public string Name { get; set; } = string.Empty;
    public int Counter { get; set; }
}

// 创建 Agent
public class MyAgent : GAgentBase<MyAgentState>
{
    public MyAgent(Guid id, ILogger<MyAgent>? logger = null)
        : base(id, logger)
    {
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("My First Agent");
    }
    
    // 添加业务方法
    public void Increment()
    {
        _state.Counter++;
    }
    
    public void SetName(string name)
    {
        _state.Name = name;
    }
}
```

### 2. 使用 Agent（Local 运行时）

```csharp
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Local;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// 设置依赖注入
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddSingleton<IGAgentActorFactory, LocalGAgentActorFactory>();

var serviceProvider = services.BuildServiceProvider();

// 创建 Agent Actor
var factory = serviceProvider.GetRequiredService<IGAgentActorFactory>();
var actor = await factory.CreateAgentAsync<MyAgent, MyAgentState>(Guid.NewGuid());

// 获取 Agent 并使用
var agent = (MyAgent)actor.GetAgent();
agent.SetName("Hello");
agent.Increment();

Console.WriteLine($"Name: {agent.GetState().Name}");
Console.WriteLine($"Counter: {agent.GetState().Counter}");

// 清理
await actor.DeactivateAsync();
```

### 3. 使用事件处理器

```csharp
public class MyAgent : GAgentBase<MyAgentState>
{
    // ... 构造函数和 GetDescriptionAsync ...
    
    // 事件处理器
    [EventHandler(Priority = 1)]
    public async Task HandleConfigEventAsync(GeneralConfigEvent evt)
    {
        _state.Name = evt.ConfigKey;
        _state.Counter++;
        
        // 发布事件给子 Agent
        await PublishAsync(
            new GeneralConfigEvent 
            { 
                ConfigKey = "processed",
                ConfigValue = evt.ConfigValue
            }, 
            EventDirection.Down);
    }
    
    // 处理所有事件（通常用于转发）
    [AllEventHandler(AllowSelfHandling = false)]
    protected async Task ForwardAllEventsAsync(EventEnvelope envelope)
    {
        // 转发给所有子 Agent
        // 事件会自动路由，这里可以添加自定义逻辑
    }
}
```

### 4. 建立 Agent 层级关系

```csharp
// 创建父 Agent
var parentActor = await factory.CreateAgentAsync<MyAgent, MyAgentState>(Guid.NewGuid());

// 创建子 Agent
var childActor = await factory.CreateAgentAsync<MyAgent, MyAgentState>(Guid.NewGuid());

// 建立层级关系
await parentActor.AddChildAsync(childActor.Id);
await childActor.SetParentAsync(parentActor.Id);

// 从父 Agent 发布事件到子 Agent
await parentActor.PublishEventAsync(
    new GeneralConfigEvent { ConfigKey = "hello", ConfigValue = "world" },
    EventDirection.Down);

// 等待事件处理
await Task.Delay(100);

// 验证子 Agent 收到事件
var childAgent = (MyAgent)childActor.GetAgent();
Console.WriteLine($"Child received: {childAgent.GetState().Name}"); // 输出: hello
```

## 🌐 运行时选择

### Local 运行时（单机测试）

```csharp
services.AddSingleton<IGAgentActorFactory, LocalGAgentActorFactory>();
```

- ✅ 最简单，无需额外配置
- ✅ 适合单元测试和开发
- ✅ 同步调用，性能最好
- ❌ 不支持分布式

### ProtoActor 运行时（高性能）

```csharp
var actorSystem = new ActorSystem();
services.AddSingleton(actorSystem);
services.AddSingleton<IGAgentActorFactory, ProtoActorGAgentActorFactory>();
```

- ✅ 高性能消息驱动
- ✅ 支持集群（需要配置）
- ✅ 异步处理
- ⚠️ 需要配置 ActorSystem

### Orleans 运行时（分布式）

```csharp
// 在 Host 中配置 Orleans
builder.Host.UseOrleans(siloBuilder =>
{
    siloBuilder.UseLocalhostClustering();
    siloBuilder.AddMemoryGrainStorage("AgentStore");
});

// 添加 Orleans Agent 支持
services.AddOrleansAgents();
```

- ✅ 完整的分布式支持
- ✅ 虚拟 Actor 模型
- ✅ 自动故障恢复
- ⚠️ 需要配置 Silo 和存储
- ⚠️ Grain 接口使用 byte[] 传递事件（避免序列化问题）

## 🎯 事件传播方向

### Down - 向下传播（最常用）

```csharp
await actor.PublishEventAsync(event, EventDirection.Down);
```

事件传播路径：Parent → Children → GrandChildren ...

### Up - 向上传播

```csharp
await actor.PublishEventAsync(event, EventDirection.Up);
```

事件传播路径：Child → Parent → GrandParent ...

### UpThenDown - 先向上再向下（兄弟节点广播）

```csharp
await actor.PublishEventAsync(event, EventDirection.UpThenDown);
```

事件传播路径：
1. Child → Parent
2. Parent → 所有 Children（包括发起者的兄弟节点）

### Bidirectional - 双向传播

```csharp
await actor.PublishEventAsync(event, EventDirection.Bidirectional);
```

事件传播路径：同时向上和向下传播

## 🛡️ HopCount 控制

防止无限循环传播：

```csharp
var envelope = new EventEnvelope
{
    // ... 其他字段 ...
    MaxHopCount = 3,  // 最多传播3跳
    MinHopCount = 1,  // 至少传播1跳后才处理
};
```

- `MaxHopCount = -1` - 无限制（默认）
- `MinHopCount = -1` - 无要求（默认）
- `CurrentHopCount` - 自动递增

## 📝 最佳实践

### 1. Agent 命名规范

```csharp
// 好的命名
public class CalculatorAgent : GAgentBase<CalculatorAgentState> { }
public class WeatherAgent : GAgentBase<WeatherAgentState> { }

// 避免
public class Agent1 : GAgentBase<State1> { }  // 不清晰
```

### 2. 状态设计

```csharp
// 推荐：简单的 POCO 类
public class MyAgentState
{
    public string Name { get; set; } = string.Empty;
    public List<string> History { get; set; } = new();
}

// 避免：过于复杂的状态
public class BadState
{
    public Dictionary<Guid, Dictionary<string, List<object>>> Data { get; set; }  // 太复杂
}
```

### 3. 事件处理器

```csharp
// 推荐：明确的事件处理器
[EventHandler(Priority = 1)]
public async Task HandleUserCreatedAsync(UserCreatedEvent evt)
{
    // 处理逻辑
}

// 可选：默认处理器（方法名 HandleAsync 或 Handle）
public async Task HandleAsync(GeneralConfigEvent evt)
{
    // 默认处理
}
```

### 4. 错误处理

```csharp
public async Task<double> DivideAsync(double a, double b)
{
    if (Math.Abs(b) < 0.0001)
        throw new DivideByZeroException("除数不能为零");
    
    return a / b;
}
```

事件处理器中的异常会被自动捕获和记录，不会影响其他处理器。

### 5. 生命周期管理

```csharp
public class MyAgent : GAgentBase<MyAgentState>
{
    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        // 初始化资源
        await base.OnActivateAsync(ct);
    }
    
    public override async Task OnDeactivateAsync(CancellationToken ct = default)
    {
        // 清理资源
        await base.OnDeactivateAsync(ct);
    }
}
```

## 🔧 常见问题

### Q: 如何在 Agent 之间传递复杂对象？

A: 使用 Protobuf 定义消息类型：

```protobuf
// messages.proto
message MyCustomEvent {
  string name = 1;
  int32 value = 2;
  repeated string tags = 3;
}
```

然后在 Agent 中处理：

```csharp
[EventHandler]
public async Task HandleMyCustomEventAsync(MyCustomEvent evt)
{
    // 处理事件
}
```

### Q: Orleans 运行时报序列化错误怎么办？

A: Orleans Grain 接口使用 `byte[]` 传递事件，框架会自动处理序列化。确保：
1. 事件类型是 Protobuf 消息
2. 使用 `PublishEventAsync` 而不是直接调用 Grain 方法

### Q: 如何切换运行时？

A: 只需更改 Factory 注册：

```csharp
// Local
services.AddSingleton<IGAgentActorFactory, LocalGAgentActorFactory>();

// ProtoActor
services.AddSingleton<IGAgentActorFactory, ProtoActorGAgentActorFactory>();

// Orleans
services.AddOrleansAgents();  // 在 DependencyInjectionExtensions 中定义
```

业务代码无需任何修改！

### Q: 如何调试事件路由？

A: 启用详细日志：

```csharp
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);  // 显示详细日志
});
```

## 📚 更多资源

- [重构追踪文档](./Refactoring_Tracker.md) - 详细的重构过程
- [重构总结](./Refactoring_Summary.md) - 重构成果总结
- [系统架构](./AgentSystem_Architecture.md) - 架构设计
- [Protobuf 配置](./Protobuf_Configuration_Guide.md) - Protobuf 配置指南

## 🎯 示例项目

- `examples/SimpleDemo` - 最简单的控制台示例
- `examples/Demo.Api` - WebAPI 示例
- `examples/Demo.AppHost` - 主机程序
- `examples/Demo.Agents` - 示例 Agent（Calculator, Weather）

---

*语言震动的回响，构建无限可能。*

