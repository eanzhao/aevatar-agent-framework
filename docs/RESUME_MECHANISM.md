# Resume Mechanism - 订阅恢复机制

## 🔄 概述

Resume机制允许在订阅中断后恢复消息接收，无需重新建立完整的订阅关系。这对于以下场景特别重要：

- 网络临时中断
- 节点重启
- 资源临时不可用
- 订阅暂停/恢复

## 📐 设计原理

### IMessageStreamSubscription接口

```csharp
public interface IMessageStreamSubscription : IAsyncDisposable
{
    bool IsActive { get; }
    Task UnsubscribeAsync();  // 暂停订阅
    Task ResumeAsync();        // 恢复订阅
}
```

### 三种实现策略

#### 1. Orleans实现 - 完整恢复

Orleans支持通过`StreamSubscriptionHandle`完整恢复订阅：

```csharp
public async Task ResumeAsync()
{
    if (_handle != null)
    {
        // 使用保存的observer恢复订阅
        _handle = await _handle.ResumeAsync(_observer);
        _isActive = true;
    }
    else
    {
        // handle已释放，重新订阅
        _handle = await _stream.SubscribeAsync(_observer);
        _isActive = true;
    }
}
```

**特点**：
- 保持原有的订阅位置
- 支持从断点续传
- 需要保存observer引用

#### 2. ProtoActor实现 - 状态恢复

ProtoActor基于Actor消息传递，恢复简单：

```csharp
public Task ResumeAsync()
{
    // 只需重新激活标志
    _isActive = true;
    return Task.CompletedTask;
}
```

**特点**：
- 基于内存，快速恢复
- 不保证消息不丢失
- 适合短暂暂停

#### 3. Local实现 - Channel恢复

Local基于内存Channel：

```csharp
public Task ResumeAsync()
{
    _isActive = true;
    // 注意：Channel关闭后无法恢复
    return Task.CompletedTask;
}
```

**特点**：
- 最简单的恢复机制
- Channel必须仍然活跃
- 适合同进程恢复

## 🎯 使用场景

### 场景1：网络抖动恢复

```csharp
// 检测到网络问题
if (networkIssue)
{
    // 暂停订阅，避免错误累积
    await subscription.UnsubscribeAsync();
    
    // 等待网络恢复
    await WaitForNetworkRecovery();
    
    // 恢复订阅
    await subscription.ResumeAsync();
}
```

### 场景2：资源限制管理

```csharp
public class ResourceManagedAgent : GAgentBase<State>
{
    private IMessageStreamSubscription? _parentSubscription;
    
    // 内存压力时暂停订阅
    public async Task OnMemoryPressure()
    {
        if (_parentSubscription?.IsActive == true)
        {
            await _parentSubscription.UnsubscribeAsync();
            Logger.LogWarning("Subscription paused due to memory pressure");
        }
    }
    
    // 资源恢复后继续
    public async Task OnResourcesAvailable()
    {
        if (_parentSubscription?.IsActive == false)
        {
            await _parentSubscription.ResumeAsync();
            Logger.LogInformation("Subscription resumed");
        }
    }
}
```

### 场景3：批处理控制

```csharp
public class BatchProcessor : GAgentBase<BatchState>
{
    private IMessageStreamSubscription? _subscription;
    private int _processedCount = 0;
    private const int BatchSize = 100;
    
    [EventHandler]
    public async Task HandleMessage(DataEvent evt)
    {
        _processedCount++;
        
        // 达到批次大小，暂停接收新消息
        if (_processedCount >= BatchSize)
        {
            await _subscription!.UnsubscribeAsync();
            
            // 处理批次
            await ProcessBatch();
            
            // 重置计数器并恢复
            _processedCount = 0;
            await _subscription.ResumeAsync();
        }
    }
}
```

## 🚨 错误处理

### 恢复失败的处理策略

```csharp
public async Task SafeResumeAsync(IMessageStreamSubscription subscription)
{
    try
    {
        await subscription.ResumeAsync();
    }
    catch (InvalidOperationException ex)
    {
        Logger.LogError(ex, "Failed to resume subscription");
        
        // 尝试创建新订阅
        var newSubscription = await CreateNewSubscription();
        
        // 清理旧订阅
        await subscription.DisposeAsync();
    }
}
```

### Orleans特定的恢复策略

```csharp
// Orleans支持带fallback的恢复
catch (Exception ex)
{
    Console.WriteLine($"Resume failed: {ex.Message}");
    
    // 自动fallback到新订阅
    try
    {
        _handle = await _stream.SubscribeAsync(_observer);
        _isActive = true;
    }
    catch
    {
        throw new InvalidOperationException(
            "Failed to resume or recreate subscription");
    }
}
```

## ⚡ 性能考虑

### 恢复延迟

| 运行时 | 恢复延迟 | 消息保证 | 资源消耗 |
|--------|----------|----------|----------|
| Orleans | ~10-50ms | 有序，不丢失 | 中等 |
| ProtoActor | <1ms | 可能丢失 | 低 |
| Local | <1ms | 取决于Channel | 最低 |

### 最佳实践

1. **快速失败**：设置恢复超时
```csharp
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
await subscription.ResumeAsync().WaitAsync(cts.Token);
```

2. **指数退避**：多次失败时延长重试间隔
```csharp
for (int i = 0; i < maxRetries; i++)
{
    try
    {
        await subscription.ResumeAsync();
        break;
    }
    catch
    {
        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)));
    }
}
```

3. **健康检查**：定期验证订阅状态
```csharp
public async Task HealthCheck()
{
    if (!_subscription.IsActive)
    {
        await _subscription.ResumeAsync();
    }
}
```

## 🔍 监控和诊断

### 订阅状态追踪

```csharp
public class SubscriptionMetrics
{
    public int ActiveSubscriptions { get; set; }
    public int PausedSubscriptions { get; set; }
    public int ResumeAttempts { get; set; }
    public int ResumeFailures { get; set; }
    public TimeSpan AveragePauseDuration { get; set; }
}
```

### 日志记录

```csharp
Logger.LogInformation("Subscription {Id} resumed after {Duration}ms", 
    subscription.SubscriptionId, 
    pauseDuration.TotalMilliseconds);
```

## 🌟 总结

Resume机制提供了灵活的订阅生命周期管理：

- **Orleans**：完整的状态恢复，适合关键业务
- **ProtoActor**：快速恢复，适合高频操作
- **Local**：极简恢复，适合单机场景

选择合适的策略取决于：
- 消息重要性
- 网络稳定性
- 性能要求
- 资源限制

通过合理使用Resume机制，可以构建更加健壮和弹性的分布式系统。
