# Advanced Agent Examples - 高级 Agent 示例

## GAgentBase 的三种形式

### 1. 基础版本 - GAgentBase<TState>

适用于：不需要约束事件类型，也不需要配置的简单 Agent

```csharp
public class SimpleAgentState
{
    public string Name { get; set; } = string.Empty;
    public int Counter { get; set; }
}

public class SimpleAgent : GAgentBase<SimpleAgentState>
{
    public SimpleAgent(Guid id, ILogger<SimpleAgent>? logger = null)
        : base(id, logger)
    {
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Simple Agent");
    }
    
    // 可以处理任何 IMessage 类型的事件
    [EventHandler]
    public Task HandleConfigEventAsync(GeneralConfigEvent evt)
    {
        _state.Name = evt.ConfigKey;
        return Task.CompletedTask;
    }
    
    [EventHandler]
    public Task HandleLLMEventAsync(LLMEvent evt)
    {
        _state.Counter++;
        return Task.CompletedTask;
    }
}
```

### 2. 事件约束版本 - GAgentBase<TState, TEvent>

适用于：需要约束 Agent 只能处理特定基类的事件

```csharp
// 定义事件基类（必须是 IMessage）
public class MyEventBase : IMessage
{
    public string EventId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    
    // IMessage 实现...
}

public class UserCreatedEvent : MyEventBase
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
}

public class UserUpdatedEvent : MyEventBase
{
    public string UserId { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
}

public class UserAgentState
{
    public Dictionary<string, string> Users { get; set; } = new();
}

public class UserAgent : GAgentBase<UserAgentState, MyEventBase>
{
    public UserAgent(Guid id, ILogger<UserAgent>? logger = null)
        : base(id, logger)
    {
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("User Management Agent");
    }
    
    // 只能处理 MyEventBase 或其子类的事件
    [EventHandler(Priority = 1)]
    public async Task HandleUserCreatedAsync(UserCreatedEvent evt)
    {
        _state.Users[evt.UserId] = evt.UserName;
        
        // 发布事件时也受 TEvent 约束
        await PublishAsync(new UserUpdatedEvent
        {
            UserId = evt.UserId,
            NewName = evt.UserName
        }, EventDirection.Down);
    }
    
    [EventHandler(Priority = 2)]
    public Task HandleUserUpdatedAsync(UserUpdatedEvent evt)
    {
        if (_state.Users.ContainsKey(evt.UserId))
        {
            _state.Users[evt.UserId] = evt.NewName;
        }
        return Task.CompletedTask;
    }
    
    // 编译错误：不能处理 MyEventBase 之外的事件
    // [EventHandler]
    // public Task HandleOtherEventAsync(GeneralConfigEvent evt) { }  // ❌ 编译错误
}
```

**优势**：
- ✅ 类型安全 - 编译时检查事件类型
- ✅ 清晰的职责 - Agent 只处理特定领域的事件
- ✅ 防止误用 - 不能处理无关的事件类型

### 3. 配置支持版本 - GAgentBase<TState, TEvent, TConfiguration>

适用于：需要动态配置 Agent 行为的场景

```csharp
// 定义配置类型（必须是 IMessage）
public class UserAgentConfiguration : IMessage
{
    public int MaxUsers { get; set; } = 1000;
    public bool EnableAutoCleanup { get; set; } = true;
    public int CleanupThresholdDays { get; set; } = 30;
    
    // IMessage 实现...
}

public class ConfigurableUserAgent 
    : GAgentBase<UserAgentState, MyEventBase, UserAgentConfiguration>
{
    private UserAgentConfiguration _config = new();
    
    public ConfigurableUserAgent(Guid id, ILogger<ConfigurableUserAgent>? logger = null)
        : base(id, logger)
    {
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Configurable User Agent (Max: {_config.MaxUsers})");
    }
    
    // 配置处理器（自动被发现）
    protected override async Task OnConfigureAsync(
        UserAgentConfiguration configuration, 
        CancellationToken ct = default)
    {
        _config = configuration;
        
        _logger.LogInformation(
            "Agent {Id} configured: MaxUsers={MaxUsers}, AutoCleanup={AutoCleanup}",
            Id, _config.MaxUsers, _config.EnableAutoCleanup);
        
        await Task.CompletedTask;
    }
    
    [EventHandler]
    public async Task HandleUserCreatedAsync(UserCreatedEvent evt)
    {
        // 检查配置限制
        if (_state.Users.Count >= _config.MaxUsers)
        {
            _logger.LogWarning("Max users limit reached: {Max}", _config.MaxUsers);
            return;
        }
        
        _state.Users[evt.UserId] = evt.UserName;
    }
}

// 使用配置
var actor = await factory.CreateAgentAsync<ConfigurableUserAgent, UserAgentState>(Guid.NewGuid());
var agent = (ConfigurableUserAgent)actor.GetAgent();

// 动态配置
await agent.ConfigureAsync(new UserAgentConfiguration
{
    MaxUsers = 500,
    EnableAutoCleanup = true,
    CleanupThresholdDays = 60
});
```

**优势**：
- ✅ 动态配置 - 运行时修改 Agent 行为
- ✅ 类型安全 - 配置也是强类型
- ✅ 可序列化 - 配置可以通过 Protobuf 传输

## 事件处理器高级用法

### 1. 优先级控制

```csharp
public class PriorityAgent : GAgentBase<MyState>
{
    // 高优先级处理器（先执行）
    [EventHandler(Priority = 1)]
    public Task ValidateEventAsync(MyEvent evt)
    {
        // 验证逻辑
        return Task.CompletedTask;
    }
    
    // 普通优先级处理器
    [EventHandler(Priority = 10)]
    public Task ProcessEventAsync(MyEvent evt)
    {
        // 处理逻辑
        return Task.CompletedTask;
    }
    
    // 低优先级处理器（最后执行）
    [EventHandler(Priority = 100)]
    public Task LogEventAsync(MyEvent evt)
    {
        // 日志逻辑
        return Task.CompletedTask;
    }
}
```

### 2. AllEventHandler（事件转发）

```csharp
public class ForwarderAgent : GAgentBase<MyState>
{
    // 转发所有事件给子 Agent
    [AllEventHandler(AllowSelfHandling = false)]
    protected async Task ForwardAllEventsAsync(EventEnvelope envelope)
    {
        _logger.LogDebug("Forwarding event {EventId} to children", envelope.Id);
        
        // 事件会自动路由到子 Agent
        // 这里可以添加额外的转发逻辑，如过滤、转换等
        
        // 例如：只转发特定类型的事件
        if (envelope.Payload != null)
        {
            // 可以检查 Payload 类型
            var descriptor = envelope.Payload.TypeUrl;
            if (descriptor.Contains("UserEvent"))
            {
                // 只转发用户事件
            }
        }
    }
}
```

### 3. AllowSelfHandling

```csharp
public class RecursiveAgent : GAgentBase<MyState>
{
    // 不处理自己发出的事件（默认）
    [EventHandler(AllowSelfHandling = false)]
    public Task HandleEventAsync(MyEvent evt)
    {
        // 只处理其他 Agent 发出的事件
        return Task.CompletedTask;
    }
    
    // 处理自己发出的事件（用于递归处理）
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleRecursiveEventAsync(RecursiveEvent evt)
    {
        // 可以处理自己发出的事件
        evt.Depth++;
        
        if (evt.Depth < evt.MaxDepth)
        {
            // 继续递归
            await PublishAsync(evt, EventDirection.Down);
        }
    }
}
```

## 层级关系高级用法

### 父子 Agent 协作

```csharp
// 父 Agent - 协调者
public class CoordinatorAgent : GAgentBase<CoordinatorState>
{
    public async Task CreateWorkersAsync(IGAgentActorFactory factory, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var workerActor = await factory.CreateAgentAsync<WorkerAgent, WorkerState>(
                Guid.NewGuid());
            
            // 建立层级关系
            await workerActor.SetParentAsync(Id);
            
            // 发送配置给 Worker
            await PublishAsync(new WorkerConfiguration
            {
                WorkerId = i,
                TaskType = "Processing"
            }, EventDirection.Down);
        }
    }
    
    [EventHandler]
    public Task HandleTaskCompletedAsync(TaskCompletedEvent evt)
    {
        _logger.LogInformation("Worker {WorkerId} completed task", evt.WorkerId);
        _state.CompletedTasks++;
        return Task.CompletedTask;
    }
}

// 子 Agent - 工作者
public class WorkerAgent : GAgentBase<WorkerState>
{
    [EventHandler]
    public async Task HandleConfigAsync(WorkerConfiguration config)
    {
        _state.WorkerId = config.WorkerId;
        _state.TaskType = config.TaskType;
        
        // 开始工作
        await DoWorkAsync();
        
        // 报告完成（向上传播）
        await PublishAsync(new TaskCompletedEvent
        {
            WorkerId = _state.WorkerId
        }, EventDirection.Up);
    }
}
```

## 事件传播高级用法

### 兄弟节点广播（UpThenDown）

```csharp
public class BroadcastingAgent : GAgentBase<MyState>
{
    [EventHandler]
    public async Task HandleUserActionAsync(UserActionEvent evt)
    {
        // 使用 UpThenDown 方向，实现兄弟节点广播
        // 1. 事件先发送到父节点
        // 2. 父节点收到后，转发给所有子节点（包括兄弟节点）
        await PublishAsync(new UserActionNotification
        {
            Action = evt.Action,
            UserId = evt.UserId
        }, EventDirection.UpThenDown);
    }
}
```

### HopCount 控制示例

```csharp
public class HopControlAgent : GAgentBase<MyState>
{
    public async Task BroadcastToDepthAsync(MyEvent evt, int depth)
    {
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Version = 1,
            Payload = Any.Pack(evt),
            PublisherId = Id.ToString(),
            Direction = EventDirection.Down,
            MaxHopCount = depth,  // 限制传播深度
            CurrentHopCount = 0
        };
        
        // 只传播到指定深度的子节点
        // Depth=1: 直接子节点
        // Depth=2: 子节点 + 孙节点
        // Depth=3: 子节点 + 孙节点 + 曾孙节点
    }
    
    public async Task BroadcastFromDepthAsync(MyEvent evt, int minDepth)
    {
        var envelope = new EventEnvelope
        {
            // ...
            MinHopCount = minDepth,  // 只在指定深度后才处理
            // 例如 MinHop=2，则只有孙节点及以下才会处理此事件
        };
    }
}
```

## 最佳实践

### 1. 状态设计

```csharp
// ✅ 好的状态设计 - 简单、可序列化
public class GoodState
{
    public string Name { get; set; } = string.Empty;
    public List<string> Items { get; set; } = new();
    public Dictionary<string, int> Counters { get; set; } = new();
}

// ❌ 避免 - 过于复杂
public class BadState
{
    public Func<string, Task> Callback { get; set; }  // ❌ 不可序列化
    public DbContext Database { get; set; }  // ❌ 不应该在状态中
}
```

### 2. 事件设计

```csharp
// ✅ 好的事件设计 - 包含足够的上下文信息
public class UserCreatedEvent : IMessage
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

// ❌ 避免 - 信息不足
public class BadEvent : IMessage
{
    public string Data { get; set; } = string.Empty;  // 太模糊
}
```

### 3. 事件处理器命名

```csharp
// ✅ 清晰的命名
[EventHandler]
public Task HandleUserCreatedAsync(UserCreatedEvent evt) { }

[EventHandler]
public Task HandleOrderPlacedAsync(OrderPlacedEvent evt) { }

// ❌ 避免 - 不清晰
[EventHandler]
public Task Handle1Async(Event1 evt) { }

[EventHandler]
public Task ProcessAsync(SomeEvent evt) { }
```

### 4. 层级关系设计

```csharp
// ✅ 清晰的层级关系
// System Agent
//   ├── Module A Agent
//   │   ├── Worker A1
//   │   └── Worker A2
//   └── Module B Agent
//       ├── Worker B1
//       └── Worker B2

// 每一层有明确的职责
// - System: 协调和监控
// - Module: 功能模块管理
// - Worker: 具体任务执行
```

## 性能优化技巧

### 1. 事件处理器缓存

事件处理器会自动缓存，不需要手动优化：

```csharp
// 框架自动缓存反射结果
public class MyAgent : GAgentBase<MyState>
{
    // 第一次调用时会反射并缓存
    // 后续调用直接使用缓存的 MethodInfo
}
```

### 2. 批量事件处理

```csharp
public class BatchProcessingAgent : GAgentBase<MyState>
{
    private readonly List<MyEvent> _eventBatch = new();
    private readonly SemaphoreSlim _batchLock = new(1, 1);
    
    [EventHandler]
    public async Task HandleEventAsync(MyEvent evt)
    {
        await _batchLock.WaitAsync();
        try
        {
            _eventBatch.Add(evt);
            
            // 达到批次大小时处理
            if (_eventBatch.Count >= 100)
            {
                await ProcessBatchAsync();
                _eventBatch.Clear();
            }
        }
        finally
        {
            _batchLock.Release();
        }
    }
    
    private async Task ProcessBatchAsync()
    {
        // 批量处理
    }
}
```

### 3. 异步最佳实践

```csharp
public class AsyncAgent : GAgentBase<MyState>
{
    // ✅ 正确的异步模式
    [EventHandler]
    public async Task HandleEventAsync(MyEvent evt)
    {
        var result = await CallExternalServiceAsync(evt);
        _state.LastResult = result;
    }
    
    // ❌ 避免 - 同步阻塞
    [EventHandler]
    public Task HandleEventBadAsync(MyEvent evt)
    {
        var result = CallExternalServiceAsync(evt).Result;  // ❌ 阻塞
        return Task.CompletedTask;
    }
}
```

## 错误处理

### 1. 事件处理器中的异常

```csharp
public class RobustAgent : GAgentBase<MyState>
{
    [EventHandler]
    public async Task HandleEventAsync(MyEvent evt)
    {
        try
        {
            // 业务逻辑
            await ProcessEventAsync(evt);
        }
        catch (BusinessException ex)
        {
            // 业务异常 - 记录并发布错误事件
            _logger.LogWarning(ex, "Business error processing event");
            
            await PublishAsync(new ErrorEvent
            {
                ErrorMessage = ex.Message,
                EventId = evt.EventId
            }, EventDirection.Up);
        }
        // 框架会自动捕获其他异常并记录日志
    }
}
```

### 2. 发布事件时的异常

```csharp
public class SafePublishingAgent : GAgentBase<MyState>
{
    public async Task PublishSafelyAsync(MyEvent evt)
    {
        try
        {
            await PublishAsync(evt, EventDirection.Down);
        }
        catch (InvalidOperationException ex)
        {
            // EventPublisher 未设置
            _logger.LogError(ex, "Cannot publish event - EventPublisher not initialized");
        }
    }
}
```

---

*掌握这些高级用法，让你的 Agent 更加强大和灵活。* 🚀

