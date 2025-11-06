# Agent Factory 使用指南

## 概述

Agent Factory 系统提供了灵活的方式来创建和管理 Agent 实例。现在支持两种模式：

1. **自动发现模式**（推荐） - 无需手动注册，自动为所有 Agent 创建工厂
2. **手动注册模式** - 完全控制 Agent 的创建过程

## 自动发现模式（推荐）

最简单的使用方式，无需手动注册每个 Agent：

```csharp
// Program.cs 或 Startup.cs
services.AddAutoDiscoveryAgentFactory();  // 使用自动发现
services.AddSingleton<LocalGAgentActorFactory>();
services.AddSingleton<IGAgentActorFactory>(sp => 
    sp.GetRequiredService<LocalGAgentActorFactory>());
```

就这样！框架会自动为你的所有 Agent 创建工厂。

## 手动注册模式

如果需要自定义创建逻辑：

```csharp
// 步骤 1：注册默认工厂提供者
services.AddAgentFactory();  // 使用手动模式
services.AddSingleton<LocalGAgentActorFactory>();
services.AddSingleton<IGAgentActorFactory>(sp => 
    sp.GetRequiredService<LocalGAgentActorFactory>());

// 步骤 2：手动注册 Agent 工厂
var factoryProvider = serviceProvider.GetRequiredService<IGAgentActorFactoryProvider>();
factoryProvider.RegisterFactory<MyAgent>(async (factory, id, ct) =>
{
    var agent = new MyAgent(id);
    // ... 自定义创建逻辑
    return actor;
});
```

### 示例：创建 Agent

```csharp
public class MyAgent : GAgentBase<MyAgentState>
{
    public MyAgent(Guid id) : base(id) { }
}

// 使用
var factory = serviceProvider.GetRequiredService<IGAgentActorFactory>();
var actor = await factory.CreateGAgentActorAsync<MyAgent>(Guid.NewGuid());
```

## 手动注册模式

如果需要更多控制，可以手动注册每个 Agent 的工厂：

```csharp
// 1. 注册默认工厂提供者
services.AddAgentFactory();

// 2. 手动注册 Agent 工厂
services.AddSingleton<IGAgentActorFactoryProvider>(sp =>
{
    var provider = new DefaultGAgentActorFactoryProvider();
    
    // 注册具体的 Agent 工厂
    provider.RegisterFactory<MyAgent>(async (factory, id, ct) =>
    {
        // 自定义创建逻辑
        var agent = new MyAgent(id);
        // ... 更多初始化
        return await CreateActor(agent);
    });
    
    return provider;
});
```

## 不同运行时的配置

### Local 运行时

```csharp
// 使用简单模式
services.AddSimpleAgentFactory();
services.AddSingleton<LocalMessageStreamRegistry>();
services.AddSingleton<LocalGAgentActorFactory>();
services.AddSingleton<IGAgentActorFactory>(sp => 
    sp.GetRequiredService<LocalGAgentActorFactory>());
```

### Orleans 运行时

```csharp
services.AddSimpleAgentFactory();
services.AddSingleton<OrleansGAgentActorFactory>();
services.AddSingleton<IGAgentActorFactory>(sp => 
    sp.GetRequiredService<OrleansGAgentActorFactory>());
```

### ProtoActor 运行时

```csharp
services.AddSimpleAgentFactory();
services.AddSingleton<ProtoActorGAgentActorFactory>();
services.AddSingleton<IGAgentActorFactory>(sp => 
    sp.GetRequiredService<ProtoActorGAgentActorFactory>());
```

## 测试环境配置

在测试中，可以使用更简单的配置：

```csharp
var services = new ServiceCollection();
services.AddLogging();
services.AddSimpleAgentFactory();  // 添加这一行即可
services.AddSingleton<LocalMessageStreamRegistry>();
services.AddSingleton<LocalGAgentActorFactory>();

var serviceProvider = services.BuildServiceProvider();
var factory = serviceProvider.GetRequiredService<LocalGAgentActorFactory>();
```

## 迁移指南

### 从旧的反射模式迁移

如果你之前使用 `AgentTypeHelper` 或其他反射方式：

**之前：**
```csharp
// 使用 AgentTypeHelper.ExtractStateType 等
var stateType = AgentTypeHelper.ExtractStateType(agentType);
// 复杂的反射逻辑
```

**现在：**
```csharp
// 只需添加一行
services.AddSimpleAgentFactory();
// 框架自动处理所有细节
```

### 从双泛型迁移到单泛型

**之前：**
```csharp
await manager.CreateAndRegisterAsync<MyAgent, MyAgentState>(id);
```

**现在：**
```csharp
await manager.CreateAndRegisterAsync<MyAgent>(id);
```

## 最佳实践

1. **优先使用简单模式** - 除非有特殊需求，否则使用 `AddSimpleAgentFactory()`
2. **集中注册** - 将所有 Agent 相关的注册放在一个扩展方法中
3. **避免手动反射** - 让框架处理类型推断和实例创建

## 常见问题

### Q: 为什么会出现 "No IGAgentActorFactoryProvider registered" 错误？

A: 确保已经调用了 `AddSimpleAgentFactory()` 或 `AddAgentFactory()`。

### Q: 如何为特定 Agent 自定义创建逻辑？

A: 使用手动注册模式，通过 `RegisterFactory<TAgent>` 方法注册自定义工厂。

### Q: 支持依赖注入吗？

A: 是的！SimpleAgentFactoryProvider 使用 `ActivatorUtilities.CreateInstance` 自动处理依赖注入。
