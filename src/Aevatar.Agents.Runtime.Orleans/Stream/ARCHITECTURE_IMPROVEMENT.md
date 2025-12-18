# Orleans Runtime Stream Architecture Improvement

## 🎯 改进目标

统一 Stream 接口，消除条件判断，简化代码，提高可维护性。

## 📊 当前架构问题

### 问题 1: 代码重复和条件判断
```csharp
// 当前实现：需要管理两种 Stream 类型
private IAsyncStream<byte[]>? _myStream;  // Orleans Stream
private IMessageStream? _massTransitStream;  // MassTransit Stream
private bool _useMassTransitStream;  // 条件判断标志

// 发布事件时需要判断
if (_useMassTransitStream && _massTransitStream != null)
{
    await _massTransitStream.ProduceAsync(envelope);
}
else if (_myStream != null)
{
    await _myStream.OnNextAsync(envelopeBytes);
}
```

### 问题 2: GrainEventPublisher 需要知道两种 Stream
```csharp
// 当前实现：Publisher 需要接收两种 Stream
new GrainEventPublisher(this, _myStream, _massTransitStream, _useMassTransitStream, ...)
```

### 问题 3: 初始化逻辑复杂
- MassTransit Stream 需要等待 Agent 初始化才能获取 category
- 需要重新初始化逻辑

## ✨ 改进后的架构

### 核心设计原则

1. **统一接口**: 统一使用 `IMessageStream` 接口
2. **工厂模式**: 使用 `OrleansStreamFactory` 统一创建 Stream
3. **依赖注入**: Stream Factory 通过 DI 注入
4. **单一职责**: Grain 只负责业务逻辑，不关心 Stream 实现

### 架构图

```
┌─────────────────────────────────────────────────────────┐
│              OrleansGAgentGrain                        │
│  ┌──────────────────────────────────────────────────┐   │
│  │  OrleansStreamFactory                            │   │
│  │  - CreateStreamAsync()                          │   │
│  │  - 自动选择 Orleans/MassTransit                 │   │
│  └──────────────────────────────────────────────────┘   │
│                        ↓                                 │
│  ┌──────────────────────────────────────────────────┐   │
│  │  IMessageStream (统一接口)                       │   │
│  │  - ProduceAsync()                                │   │
│  │  - SubscribeAsync()                              │   │
│  └──────────────────────────────────────────────────┘   │
│                        ↓                                 │
│  ┌──────────────┐              ┌──────────────┐         │
│  │OrleansStream │              │MassTransit  │         │
│  │(Adapter)     │              │Stream       │         │
│  └──────────────┘              └──────────────┘         │
└─────────────────────────────────────────────────────────┘
```

### 关键组件

#### 1. OrleansStreamFactory
```csharp
public class OrleansStreamFactory
{
    // 统一创建 Stream，自动选择实现
    public Task<IMessageStream> CreateStreamAsync(
        Guid agentId,
        string? agentCategory = null,
        Func<string, IStreamProvider>? getStreamProvider = null)
    {
        // 根据配置自动选择 Orleans Stream 或 MassTransit Stream
    }
}
```

**职责**:
- 根据配置选择 Stream Provider
- 创建统一的 `IMessageStream` 实例
- 封装 Orleans Stream 适配逻辑

#### 2. UnifiedGrainEventPublisher
```csharp
internal class UnifiedGrainEventPublisher : IEventPublisher
{
    private readonly IMessageStream? _stream;  // 统一接口
    
    // 只依赖 IMessageStream，不关心实现
    public async Task<string> PublishEventAsync<TEvent>(...)
    {
        await _stream.ProduceAsync(envelope, ct);
    }
}
```

**职责**:
- 统一的事件发布接口
- 只依赖 `IMessageStream`，不关心底层实现
- 简化代码，消除条件判断

#### 3. OrleansMessageStream (已存在)
```csharp
public class OrleansMessageStream : IMessageStream
{
    // 将 Orleans IAsyncStream<byte[]> 适配为 IMessageStream
}
```

**职责**:
- 适配 Orleans Stream 到统一接口
- 处理序列化/反序列化
- 管理订阅生命周期

## 🔄 重构后的 OrleansGAgentGrain

### 简化后的字段
```csharp
public class OrleansGAgentGrain : Grain, IGAgentGrain
{
    // 统一使用 IMessageStream 接口
    private IMessageStream? _myStream;
    private IMessageStreamSubscription? _streamSubscription;
    
    // 注入 Stream Factory
    private OrleansStreamFactory? _streamFactory;
}
```

### 简化的初始化
```csharp
private async Task InitializeStreamAsync()
{
    _streamFactory = ServiceProvider.GetRequiredService<OrleansStreamFactory>();
    
    var agentId = ExtractAgentIdFromGrainKey(this.GetPrimaryKeyString());
    var agentCategory = _agent?.GetType().Name;  // 如果 Agent 已初始化
    
    // 统一创建 Stream，自动选择实现
    _myStream = await _streamFactory.CreateStreamAsync(
        agentId,
        agentCategory,
        this.GetStreamProvider);  // 传递 Orleans StreamProvider 获取函数
    
    // 统一订阅接口
    _streamSubscription = await _myStream.SubscribeAsync<EventEnvelope>(
        async envelope =>
        {
            // 处理事件
            var envelopeBytes = SerializeEnvelope(envelope);
            await HandleEventAsync(envelopeBytes);
        });
}
```

### 简化的事件发布
```csharp
// 在 HandleEventAsync 中
await _myStream?.ProduceAsync(envelope);  // 统一接口，无需判断

// 在 InjectAgentDependencies 中
AgentEventPublisherInjector.InjectEventPublisher(agent,
    new UnifiedGrainEventPublisher(
        _myStream,
        () => this.GetPrimaryKeyString(),  // Grain ID 获取函数
        _logger,
        GrainFactory));
```

## 📈 改进效果对比

### Before (当前实现)
```csharp
// ❌ 需要管理两种 Stream 类型
private IAsyncStream<byte[]>? _myStream;
private IMessageStream? _massTransitStream;
private bool _useMassTransitStream;

// ❌ 条件判断
if (_useMassTransitStream && _massTransitStream != null)
{
    await _massTransitStream.ProduceAsync(envelope);
}
else if (_myStream != null)
{
    await _myStream.OnNextAsync(envelopeBytes);
}

// ❌ Publisher 需要知道两种 Stream
new GrainEventPublisher(this, _myStream, _massTransitStream, _useMassTransitStream, ...)
```

### After (改进后)
```csharp
// ✅ 统一接口
private IMessageStream? _myStream;

// ✅ 无需条件判断
await _myStream?.ProduceAsync(envelope);

// ✅ Publisher 只依赖统一接口
new UnifiedGrainEventPublisher(_myStream, ...)
```

## 🎯 优势总结

1. **代码简化**: 消除条件判断，代码更清晰
2. **统一接口**: 统一使用 `IMessageStream`，与 Local Runtime 一致
3. **易于扩展**: 添加新的 Stream Provider 只需实现 `IMessageStream`
4. **职责分离**: Grain 不关心 Stream 实现细节
5. **可测试性**: 可以轻松 Mock `IMessageStream` 进行测试

## 🚀 迁移步骤

1. ✅ 创建 `OrleansStreamFactory`
2. ✅ 创建 `UnifiedGrainEventPublisher`
3. ✅ 重构 `OrleansGAgentGrain` 使用新架构
4. ✅ 更新依赖注入配置
5. ⏳ 测试验证

## 🔧 进一步优化 (v2)

### 已完成的优化

1. ✅ **延迟 Stream 初始化**: Stream 在 Agent 初始化时创建，而非 Grain 激活时
2. ✅ **消除代码重复**: 提取 `OnStreamEventReceivedAsync` 回调方法
3. ✅ **消除不必要的序列化**: 新增 `HandleEventDirectAsync` 直接处理 `EventEnvelope`
4. ✅ **Factory 职责增强**: 新增 `RequiresCategoryForStream()` 方法

### 优化效果

```
Before:
  Grain 激活 → 初始化 Stream (无 category)
            → Agent 初始化 → 重新初始化 Stream (有 category) [MassTransit]
            → 重复注入依赖
            
After:
  Grain 激活 → [Orleans Stream: 初始化 Stream (可选)]
            → Agent 初始化 → 初始化 Stream (有 category)
            → 注入依赖 (一次)
```

### 性能改进

| 优化项 | Before | After |
|-------|--------|-------|
| Stream 初始化次数 | 2 (MassTransit) | 1 |
| 依赖注入次数 | 2 (重新初始化时) | 1 |
| 序列化/反序列化 | EventEnvelope→byte[]→EventEnvelope | 无 (直接处理) |
| 代码重复 | 订阅回调代码重复 | 提取为方法 |

## 🔧 进一步优化 (v3)

### 已完成的优化

1. ✅ **统一事件处理**: `HandleEventAsync(byte[])` 委托给 `HandleEventDirectAsync(EventEnvelope)`
2. ✅ **修复缩进不一致**: 代码格式统一
3. ✅ **修复 GetIdAsync**: 使用 `ExtractAgentIdFromGrainKey` 正确解析 Grain Key
4. ✅ **类型解析缓存**: `ResolveAgentType` 使用 `ConcurrentDictionary` 缓存结果

### 性能改进 (v3)

| 优化项 | Before | After | 提升 |
|-------|--------|-------|------|
| 类型解析 | 每次搜索所有程序集 | 缓存命中 O(1) | 显著 |
| HandleEventAsync | 独立实现 | 委托统一处理 | 代码简洁 |
| GetIdAsync | 只解析完整 GUID | 支持 "Type:Id" 格式 | 正确性 |

## 📝 注意事项

1. **向后兼容**: 保持现有 API 不变
2. **性能**: Orleans Stream 适配器不应引入额外开销
3. **错误处理**: 统一错误处理逻辑
4. **日志**: 保持详细的日志记录

