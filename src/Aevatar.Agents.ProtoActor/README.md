# Aevatar.Agents.ProtoActor

Aevatar Agent Framework的Proto.Actor实现，提供基于Proto.Actor的Actor模型支持。

## 概述

Proto.Actor是一个轻量级的跨平台Actor模型实现，这个库将Aevatar Agent Framework与Proto.Actor集成，提供另一种运行时选择，类似于现有的Local和Orleans实现。

## 主要组件

- **ProtoActorGAgentActor**: 实现IGAgentActor接口，使用Proto.Actor的Actor模型
- **ProtoActorGAgentFactory**: 实现IGAgentFactory接口，创建Proto.Actor实例
- **ProtoActorMessageStream**: 实现IMessageStream接口，使用Proto.Actor的消息传递机制
- **StreamActor**: 处理消息流分发的专用Actor
- **AgentActor**: 处理代理业务逻辑的Actor实现

## 使用方法

在应用程序启动时，通过依赖注入配置Proto.Actor实现：

```csharp
// 在Program.cs或Startup.cs中
services.AddProtoActorAgents();
```

## 性能优势

Proto.Actor实现提供以下优势：

1. **高性能**: 使用Proto.Actor的高性能消息传递机制
2. **可扩展性**: 支持本地和分布式Actor部署
3. **无锁并发**: 基于消息传递的无锁并发模型
4. **跨平台**: 支持多种平台和语言

## 与其他实现的对比

| 特性 | Local实现 | Orleans实现 | Proto.Actor实现 |
|-----|----------|------------|---------------|
| 复杂度 | 低 | 高 | 中 |
| 性能 | 中 | 高 | 高 |
| 分布式支持 | 无 | 内置 | 可选 |
| 部署复杂度 | 低 | 高 | 中 |
| 适用场景 | 单机测试 | 大规模云部署 | 中小规模分布式系统 |

## 配置选项

Proto.Actor实现支持以下配置选项：

- Actor池大小
- 消息缓冲区容量
- 远程部署配置（可选）

## 未来扩展

- 集成Proto.Remote用于分布式部署
- 添加Proto.Persistence支持事件溯源
- 实现Proto.Cluster用于弹性伸缩
