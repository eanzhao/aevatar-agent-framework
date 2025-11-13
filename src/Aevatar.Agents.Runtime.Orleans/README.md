# Aevatar.Agents.Orleans

Aevatar Agent Framework的Orleans实现，提供基于Microsoft Orleans的分布式Actor模型支持。

## 概述

Orleans是微软开源的分布式虚拟Actor模型框架，这个库将Aevatar Agent Framework与Orleans集成，提供企业级分布式运行时支持，适用于大规模云部署场景。

## 主要组件

- **OrleansGAgentActor**: 实现IGAgentActor接口，作为Grain的包装器
- **OrleansGAgentGrain**: Orleans Grain实现，继承自Grain基类
- **OrleansGAgentFactory**: 实现IGAgentActorFactory接口，使用IGrainFactory创建Grain实例
- **OrleansMessageStream**: 实现IMessageStream接口，支持本地Channel和Orleans Streams两种模式
- **IGAgentGrain**: Orleans Grain接口，继承自IGrainWithGuidKey

## 使用方法

### 1. 配置Orleans Host

在应用程序启动时，配置Orleans silo：

```csharp
// 在Program.cs中
var builder = Host.CreateDefaultBuilder(args)
    .UseOrleans((context, siloBuilder) =>
    {
        siloBuilder
            .UseLocalhostClustering()
            .AddMemoryGrainStorage("AgentStore")
            .AddSimpleMessageStreamProvider("StreamProvider")
            .AddMemoryGrainStorage("PubSubStore");
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton<IGAgentActorFactory, OrleansGAgentFactory>();
        // 注册你的业务Agent
        services.AddTransient<YourBusinessAgent>();
    });

var host = builder.Build();
await host.RunAsync();
```

### 2. 使用Orleans Streams（可选）

如果需要使用Orleans内置的Stream功能：

```csharp
services.AddSingleton<IGAgentActorFactory>(sp =>
{
    var grainFactory = sp.GetRequiredService<IGrainFactory>();
    var streamProvider = sp.GetRequiredService<IStreamProvider>();
    return new OrleansGAgentFactory(sp, grainFactory, streamProvider);
});
```

### 3. 客户端连接

```csharp
var client = new ClientBuilder()
    .UseLocalhostClustering()
    .ConfigureServices(services =>
    {
        services.AddSingleton<IGAgentActorFactory, OrleansGAgentFactory>();
    })
    .Build();

await client.Connect();
```

## 性能优势

Orleans实现提供以下优势：

1. **虚拟Actor模型**: 自动管理Actor生命周期，无需手动创建/销毁
2. **位置透明**: Actor可以在集群中任意节点上运行
3. **自动故障转移**: 内置的故障检测和恢复机制
4. **弹性扩展**: 支持动态添加/移除节点
5. **持久化支持**: 内置的状态持久化机制
6. **流式处理**: 原生支持Orleans Streams用于事件驱动架构

## 与其他实现的对比

| 特性 | Local实现 | Orleans实现 | Proto.Actor实现 |
|-----|----------|------------|---------------|
| 复杂度 | 低 | 高 | 中 |
| 性能 | 中 | 高 | 高 |
| 分布式支持 | 无 | 内置 | 可选 |
| 部署复杂度 | 低 | 高 | 中 |
| Actor管理 | 手动 | 自动（虚拟Actor） | 手动 |
| 状态持久化 | 手动 | 内置 | 可选 |
| 适用场景 | 单机测试 | 大规模云部署 | 中小规模分布式系统 |

## 配置选项

Orleans实现支持以下配置选项：

- **集群配置**: 单机、本地集群、云集群
- **存储提供者**: 内存、Azure、AWS、SQL等
- **流提供者**: Simple Message Streams、Azure Event Hubs、Azure Service Bus等
- **序列化**: Protobuf（默认）或其他序列化器
- **持久化策略**: 快照频率、事件存储策略

## 部署场景

### 单机开发/测试

```csharp
siloBuilder.UseLocalhostClustering();
```

### 本地集群

```csharp
siloBuilder
    .UseLocalhostClustering(
        siloPort: 11111,
        gatewayPort: 30000,
        primarySiloEndpoint: new IPEndPoint(IPAddress.Loopback, 11111));
```

### 云部署（Azure）

```csharp
siloBuilder
    .UseAzureStorageClustering(options => 
        options.ConfigureTableServiceClient(connectionString))
    .ConfigureEndpoints(siloPort: 11111, gatewayPort: 30000);
```

## 未来扩展

- 集成Orleans Dashboard用于监控
- 添加分布式事务支持
- 实现Actor版本控制和迁移策略
- 优化Grain激活策略
- 集成分布式追踪（OpenTelemetry）

## 注意事项

1. **Grain接口限制**: Orleans要求所有Grain方法必须返回Task或ValueTask
2. **序列化**: 确保所有消息类型都可以被序列化
3. **状态大小**: 注意Grain状态的大小，避免过大的内存占用
4. **激活策略**: 合理配置Grain的激活和去激活策略
5. **版本兼容性**: 注意升级时的接口版本兼容性

