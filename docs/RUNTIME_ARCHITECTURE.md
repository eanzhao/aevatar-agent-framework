# Aevatar Agent Framework - 运行时架构设计

## 🎯 运行时架构概述

Aevatar Agent Framework采用**多运行时架构**，支持在不同环境中以最适合的方式执行代理。每种运行时都有其特定的优势和适用场景，通过统一的抽象层提供一致的编程模型。

## 🏗️ 运行时架构分层

```
┌─────────────────────────────────────────────────────────┐
│                统一抽象层                                │
│  ┌───────────────────────────────────────────────────┐   │
│  │IGAgentActor    │IAgentRuntime   │RuntimeContext  │   │
│  │Lifecycle       │Capabilities    │Configuration   │   │
│  └───────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                运行时实现层                              │
│  ┌──────────────┬──────────────┬────────────────────┐   │
│  │Local Runtime │Orleans Runtime│ProtoActor Runtime │   │
│  │(进程内)       │(虚拟Actor)     │(轻量级Actor)       │   │
│  └──────────────┴──────────────┴────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                运行时特定组件                            │
│  ┌──────────────┬──────────────┬────────────────────┐   │
│  │LocalAgent    │OrleansGrain  │ProtoActor         │   │
│  │Channel       │Silo          │Mailbox            │   │
│  │Dispatcher    │Cluster       │Dispatcher         │   │
│  └──────────────┴──────────────┴────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                通信与序列化层                            │
│  ┌──────────────┬──────────────┬────────────────────┐   │
│  │Message       │Serialization │Transport          │   │
│  │Queue         │Protobuf      │Channel/TCP        │   │
│  └──────────────┴──────────────┴────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

## 🔧 统一运行时抽象

### 1. 运行时接口定义

```csharp
public interface IAgentRuntime
{
    // 运行时标识
    string RuntimeId { get; }
    string RuntimeType { get; }
    RuntimeCapabilities Capabilities { get; }

    // 生命周期管理
    Task InitializeAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task DisposeAsync();

    // 代理管理
    Task<IGAgentActor> CreateAgentAsync(string agentId, Type agentType, CancellationToken cancellationToken = default);
    Task<IGAgentActor> GetAgentAsync(string agentId, CancellationToken cancellationToken = default);
    Task RemoveAgentAsync(string agentId, CancellationToken cancellationToken = default);
    Task<bool> AgentExistsAsync(string agentId, CancellationToken cancellationToken = default);

    // 代理查询
    Task<List<AgentInfo>> GetAgentsAsync(CancellationToken cancellationToken = default);
    Task<List<AgentInfo>> GetAgentsByTypeAsync(string agentType, CancellationToken cancellationToken = default);

    // 运行时状态
    Task<RuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<RuntimeMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);
}

// 运行时能力
public class RuntimeCapabilities
{
    public bool SupportsPersistence { get; init; }
    public bool SupportsClustering { get; init; }
    public bool SupportsRemoting { get; init; }
    public bool SupportsLoadBalancing { get; init; }
    public bool SupportsFaultTolerance { get; init; }
    public bool SupportsScaling { get; init; }
    public int MaxConcurrentAgents { get; init; }
    public TimeSpan MaxAgentLifetime { get; init; }
}

// 运行时状态
public enum RuntimeStatus
{
    Initialized,
    Starting,
    Running,
    Stopping,
    Stopped,
    Faulted,
    Disposed
}

// 代理信息
public class AgentInfo
{
    public string AgentId { get; init; }
    public string AgentType { get; init; }
    public string RuntimeId { get; init; }
    public AgentStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastActivity { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

// 运行时指标
public class RuntimeMetrics
{
    public int TotalAgents { get; init; }
    public int ActiveAgents { get; init; }
    public int FaultedAgents { get; init; }
    public long TotalMessagesProcessed { get; init; }
    public long TotalErrors { get; init; }
    public TimeSpan Uptime { get; init; }
    public double CpuUsage { get; init; }
    public long MemoryUsage { get; init; }
    public Dictionary<string, long> CustomMetrics { get; init; } = new();
}
```

### 2. Actor抽象接口

```csharp
public interface IGAgentActor : IDisposable
{
    // 身份标识
    string AgentId { get; }
    string AgentType { get; }
    string RuntimeId { get; }

    // 生命周期
    Task ActivateAsync(CancellationToken cancellationToken = default);
    Task DeactivateAsync(CancellationToken cancellationToken = default);
    Task<bool> IsActiveAsync();

    // 事件处理
    Task HandleEventAsync(EventEnvelope envelope, CancellationToken cancellationToken = default);
    Task<TResponse> HandleRequestAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default);

    // 状态管理
    Task<object> GetStateAsync(CancellationToken cancellationToken = default);
    Task SetStateAsync(object state, CancellationToken cancellationToken = default);

    // 运行时信息
    Task<ActorMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);
}

// Actor指标
public class ActorMetrics
{
    public string AgentId { get; init; }
    public DateTime? ActivationTime { get; init; }
    public DateTime? LastActivityTime { get; init; }
    public long MessagesProcessed { get; init; }
    public long Errors { get; init; }
    public TimeSpan? TotalActivationTime { get; init; }
    public Dictionary<string, long> CustomMetrics { get; init; } = new();
}
```

## 🏠 Local Runtime 实现

### 1. Local运行时特点

- **进程内执行**: 直接方法调用，无网络开销
- **轻量级**: 最小内存占用，快速启动
- **开发友好**: 易于调试和测试
- **单进程限制**: 不支持分布式部署
- **适用场景**: 开发测试、简单应用、单进程部署

### 2. Local运行时实现

```csharp
public class LocalAgentRuntime : IAgentRuntime
{
    private readonly ConcurrentDictionary<string, LocalAgentActor> _agents;
    private readonly IServiceProvider _serviceProvider;
    private readonly LocalRuntimeConfiguration _configuration;
    private readonly ILogger<LocalAgentRuntime> _logger;
    private readonly Channel<EventEnvelope> _eventChannel;
    private readonly CancellationTokenSource _shutdownCts;

    public string RuntimeId { get; }
    public string RuntimeType => "Local";
    public RuntimeCapabilities Capabilities { get; }

    public LocalAgentRuntime(IServiceProvider serviceProvider, IOptions<LocalRuntimeConfiguration> configuration, ILogger<LocalAgentRuntime> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration.Value;
        _logger = logger;
        _agents = new ConcurrentDictionary<string, LocalAgentActor>();
        _shutdownCts = new CancellationTokenSource();

        RuntimeId = $"local-{Environment.MachineName}-{Guid.NewGuid():N}";

        // 配置能力
        Capabilities = new RuntimeCapabilities
        {
            SupportsPersistence = false,
            SupportsClustering = false,
            SupportsRemoting = false,
            SupportsLoadBalancing = false,
            SupportsFaultTolerance = false,
            SupportsScaling = false,
            MaxConcurrentAgents = _configuration.MaxConcurrentAgents,
            MaxAgentLifetime = Timeout.InfiniteTimeSpan
        };

        // 创建事件通道
        _eventChannel = Channel.CreateUnbounded<EventEnvelope>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    }

    public async Task InitializeAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing LocalAgentRuntime with ID {RuntimeId}", RuntimeId);

        // 启动事件处理循环
        _ = Task.Run(() => ProcessEventsAsync(_shutdownCts.Token), _shutdownCts.Token);

        await Task.CompletedTask;
    }

    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting event processing loop");

        await foreach (var envelope in _eventChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await RouteEventAsync(envelope, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing event {EventId}", envelope.Id);
            }
        }

        _logger.LogInformation("Event processing loop stopped");
    }

    private async Task RouteEventAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(envelope.TargetAgentId))
        {
            // 广播事件到所有相关代理
            var tasks = _agents.Values
                .Where(agent => agent.IsActiveAsync().Result)
                .Select(agent => agent.HandleEventAsync(envelope, cancellationToken));

            await Task.WhenAll(tasks);
        }
        else
        {
            // 定向事件到特定代理
            if (_agents.TryGetValue(envelope.TargetAgentId, out var targetAgent))
            {
                await targetAgent.HandleEventAsync(envelope, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Target agent {AgentId} not found for event {EventId}",
                    envelope.TargetAgentId, envelope.Id);
            }
        }
    }

    public async Task<IGAgentActor> CreateAgentAsync(string agentId, Type agentType, CancellationToken cancellationToken = default)
    {
        if (_agents.ContainsKey(agentId))
        {
            throw new InvalidOperationException($"Agent with ID {agentId} already exists");
        }

        _logger.LogInformation("Creating agent {AgentId} of type {AgentType}", agentId, agentType.Name);

        // 创建代理实例
        var agent = ActivatorUtilities.CreateInstance(_serviceProvider, agentType) as IGAgent;
        if (agent == null)
        {
            throw new InvalidOperationException($"Failed to create agent instance of type {agentType.Name}");
        }

        // 创建Actor包装
        var actor = new LocalAgentActor(agentId, agent, _serviceProvider, _eventChannel.Writer, _logger);

        // 注册代理
        if (!_agents.TryAdd(agentId, actor))
        {
            throw new InvalidOperationException($"Failed to register agent {agentId}");
        }

        // 激活代理
        await actor.ActivateAsync(cancellationToken);

        _logger.LogInformation("Agent {AgentId} created and activated successfully", agentId);
        return actor;
    }

    public async Task<IGAgentActor> GetAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (_agents.TryGetValue(agentId, out var actor))
        {
            return actor;
        }

        _logger.LogWarning("Agent {AgentId} not found", agentId);
        return null;
    }

    public async Task RemoveAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (_agents.TryRemove(agentId, out var actor))
        {
            await actor.DeactivateAsync(cancellationToken);
            actor.Dispose();

            _logger.LogInformation("Agent {AgentId} removed successfully", agentId);
        }
        else
        {
            _logger.LogWarning("Agent {AgentId} not found for removal", agentId);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping LocalAgentRuntime");

        // 停止事件处理
        _shutdownCts.Cancel();

        // 停用所有代理
        var stopTasks = _agents.Values.Select(agent => agent.DeactivateAsync(cancellationToken));
        await Task.WhenAll(stopTasks);

        _logger.LogInformation("LocalAgentRuntime stopped");
    }

    public async Task DisposeAsync()
    {
        await StopAsync();
        _shutdownCts?.Dispose();
        _eventChannel?.Writer.TryComplete();
    }
}
```

### 3. Local Actor实现

```csharp
public class LocalAgentActor : IGAgentActor
{
    private readonly string _agentId;
    private readonly IGAgent _agent;
    private readonly IServiceProvider _serviceProvider;
    private readonly ChannelWriter<EventEnvelope> _eventChannel;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _activationSemaphore;
    private bool _isActive;
    private DateTime? _activationTime;
    private DateTime? _lastActivityTime;
    private long _messagesProcessed;
    private long _errors;

    public LocalAgentActor(string agentId, IGAgent agent, IServiceProvider serviceProvider,
        ChannelWriter<EventEnvelope> eventChannel, ILogger logger)
    {
        _agentId = agentId;
        _agent = agent;
        _serviceProvider = serviceProvider;
        _eventChannel = eventChannel;
        _logger = logger;

        _activationSemaphore = new SemaphoreSlim(1, 1);
    }

    public string AgentId => _agentId;
    public string AgentType => _agent.GetType().Name;
    public string RuntimeId => $"local-{Environment.MachineName}";

    public async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        await _activationSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_isActive)
            {
                _logger.LogWarning("Agent {AgentId} is already active", _agentId);
                return;
            }

            _logger.LogInformation("Activating agent {AgentId}", _agentId);

            // 激活代理
            if (_agent is IActivatable activatable)
            {
                await activatable.ActivateAsync(cancellationToken);
            }

            _isActive = true;
            _activationTime = DateTime.UtcNow;
            _lastActivityTime = _activationTime;

            _logger.LogInformation("Agent {AgentId} activated successfully", _agentId);
        }
        finally
        {
            _activationSemaphore.Release();
        }
    }

    public async Task DeactivateAsync(CancellationToken cancellationToken = default)
    {
        await _activationSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (!_isActive)
            {
                return;
            }

            _logger.LogInformation("Deactivating agent {AgentId}", _agentId);

            // 停用代理
            if (_agent is IActivatable activatable)
            {
                await activatable.DeactivateAsync(cancellationToken);
            }

            _isActive = false;
            _activationTime = null;

            _logger.LogInformation("Agent {AgentId} deactivated successfully", _agentId);
        }
        finally
        {
            _activationSemaphore.Release();
        }
    }

    public async Task<bool> IsActiveAsync()
    {
        await _activationSemaphore.WaitAsync();
        try
        {
            return _isActive;
        }
        finally
        {
            _activationSemaphore.Release();
        }
    }

    public async Task HandleEventAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (!await IsActiveAsync())
        {
            _logger.LogWarning("Agent {AgentId} is not active, cannot handle event {EventId}",
                _agentId, envelope.Id);
            return;
        }

        using var activity = StartActivity($"Handle {envelope.EventType}");

        try
        {
            _logger.LogDebug("Agent {AgentId} handling event {EventId} of type {EventType}",
                _agentId, envelope.Id, envelope.EventType);

            // 更新活动时间
            Interlocked.Exchange(ref _lastActivityTime, DateTime.UtcNow);

            // 处理事件
            if (_agent is IEventHandler eventHandler)
            {
                await eventHandler.HandleAsync(envelope, cancellationToken);
            }
            else
            {
                // 使用反射调用事件处理方法
                await InvokeEventHandlerAsync(envelope, cancellationToken);
            }

            Interlocked.Increment(ref _messagesProcessed);

            _logger.LogDebug("Agent {AgentId} handled event {EventId} successfully",
                _agentId, envelope.Id);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errors);

            _logger.LogError(ex, "Agent {AgentId} failed to handle event {EventId}",
                _agentId, envelope.Id);

            throw;
        }
    }

    private async Task InvokeEventHandlerAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        // 查找事件处理方法
        var eventType = envelope.Event.GetType();
        var method = _agent.GetType().GetMethod("HandleAsync", new[] { eventType });

        if (method != null)
        {
            var result = method.Invoke(_agent, new object[] { envelope.Event });

            if (result is Task task)
            {
                await task;
            }
        }
        else
        {
            _logger.LogWarning("No handler found for event type {EventType} on agent {AgentId}",
                eventType.Name, _agentId);
        }
    }

    public async Task<TResponse> HandleRequestAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsActiveAsync())
        {
            throw new InvalidOperationException($"Agent {_agentId} is not active");
        }

        // 查找请求处理方法
        var method = _agent.GetType().GetMethod("HandleRequestAsync", new[] { typeof(TRequest) });
        if (method == null)
        {
            throw new NotSupportedException($"Agent {_agentId} does not support handling requests of type {typeof(TRequest).Name}");
        }

        var result = method.Invoke(_agent, new object[] { request });

        if (result is Task<TResponse> task)
        {
            return await task;
        }

        return (TResponse)result;
    }

    public async Task<object> GetStateAsync(CancellationToken cancellationToken = default)
    {
        if (_agent is IStateGAgent<object> stateAgent)
        {
            return stateAgent.State;
        }

        // 使用反射获取状态属性
        var stateProperty = _agent.GetType().GetProperty("State");
        if (stateProperty != null)
        {
            return stateProperty.GetValue(_agent);
        }

        return null;
    }

    public async Task SetStateAsync(object state, CancellationToken cancellationToken = default)
    {
        if (_agent is IStateGAgent<object> stateAgent)
        {
            // 需要类型安全的设置方法
            return;
        }

        // 使用反射设置状态属性
        var stateProperty = _agent.GetType().GetProperty("State");
        if (stateProperty != null && stateProperty.CanWrite)
        {
            stateProperty.SetValue(_agent, state);
        }
    }

    public async Task<ActorMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        await _activationSemaphore.WaitAsync(cancellationToken);
        try
        {
            return new ActorMetrics
            {
                AgentId = _agentId,
                ActivationTime = _activationTime,
                LastActivityTime = _lastActivityTime,
                MessagesProcessed = _messagesProcessed,
                Errors = _errors,
                TotalActivationTime = _activationTime.HasValue ? DateTime.UtcNow - _activationTime.Value : null
            };
        }
        finally
        {
            _activationSemaphore.Release();
        }
    }

    public void Dispose()
    {
        _activationSemaphore?.Dispose();
    }
}
```

## 🌐 Orleans Runtime 实现

### 1. Orleans运行时特点

- **虚拟Actor**: 自动生命周期管理，透明激活/停用
- **分布式**: 支持多节点集群部署
- **持久化**: 可选的状态持久化
- **容错**: 内置故障检测和恢复
- **适用场景**: 生产环境、分布式系统、高可用要求

### 2. Orleans Grain实现

```csharp
// Orleans Grain接口
public interface IGAgentGrain : IGrainWithStringKey
{
    Task HandleEventAsync(EventEnvelope envelope);
    Task<TResponse> HandleRequestAsync<TRequest, TResponse>(TRequest request);
    Task<object> GetStateAsync();
    Task SetStateAsync(object state);
    Task<ActorMetrics> GetMetricsAsync();
    Task ActivateAgentAsync();
    Task DeactivateAgentAsync();
}

// Orleans Grain实现
[Reentrant]
[StorageProvider(ProviderName = "Default")]
public class GAgentGrain : Grain, IGAgentGrain
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GAgentGrain> _logger;
    private IGAgent _agent;
    private string _agentType;

    // 状态持久化
    [PersistentState("agentState", "Default")]
    private IPersistentState<AgentGrainState> _state;

    public GAgentGrain(IServiceProvider serviceProvider, ILogger<GAgentGrain> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var agentId = this.GetPrimaryKeyString();
        _logger.LogInformation("Activating Orleans grain for agent {AgentId}", agentId);

        try
        {
            // 恢复或创建代理
            if (string.IsNullOrEmpty(_state.State?.AgentType))
            {
                // 新代理，需要从配置或请求中获取类型
                _logger.LogWarning("Agent type not set for {AgentId}, deferring activation", agentId);
                return;
            }

            await InitializeAgentAsync(agentId, _state.State.AgentType, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating grain for agent {AgentId}", agentId);
            throw;
        }

        await base.OnActivateAsync(cancellationToken);
    }

    private async Task InitializeAgentAsync(string agentId, string agentType, CancellationToken cancellationToken)
    {
        _agentType = agentType;

        // 创建代理实例
        var agentTypeInfo = Type.GetType(agentType);
        if (agentTypeInfo == null)
        {
            throw new InvalidOperationException($"Agent type {agentType} not found");
        }

        _agent = ActivatorUtilities.CreateInstance(_serviceProvider, agentTypeInfo) as IGAgent;
        if (_agent == null)
        {
            throw new InvalidOperationException($"Failed to create agent instance of type {agentType}");
        }

        // 恢复状态
        if (_state.State?.AgentState != null)
        {
            await SetStateAsync(_state.State.AgentState);
        }

        // 激活代理
        if (_agent is IActivatable activatable)
        {
            await activatable.ActivateAsync(cancellationToken);
        }

        _logger.LogInformation("Agent {AgentId} initialized and activated in Orleans grain", agentId);
    }

    public async Task HandleEventAsync(EventEnvelope envelope)
    {
        if (_agent == null)
        {
            throw new InvalidOperationException($"Agent not initialized for grain {this.GetPrimaryKeyString()}");
        }

        try
        {
            _logger.LogDebug("Orleans grain handling event {EventId} of type {EventType}",
                envelope.Id, envelope.EventType);

            // 处理事件
            if (_agent is IEventHandler eventHandler)
            {
                await eventHandler.HandleAsync(envelope);
            }

            // 更新状态
            if (_agent is IStateGAgent<object> stateAgent)
            {
                _state.State = new AgentGrainState
                {
                    AgentType = _agentType,
                    AgentState = stateAgent.State,
                    LastModified = DateTime.UtcNow
                };

                await _state.WriteStateAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling event {EventId} in Orleans grain", envelope.Id);
            throw;
        }
    }

    public async Task<TResponse> HandleRequestAsync<TRequest, TResponse>(TRequest request)
    {
        if (_agent == null)
        {
            throw new InvalidOperationException($"Agent not initialized for grain {this.GetPrimaryKeyString()}");
        }

        var method = _agent.GetType().GetMethod("HandleRequestAsync", new[] { typeof(TRequest) });
        if (method == null)
        {
            throw new NotSupportedException($"Agent does not support handling requests of type {typeof(TRequest).Name}");
        }

        var result = method.Invoke(_agent, new object[] { request });

        if (result is Task<TResponse> task)
        {
            return await task;
        }

        return (TResponse)result;
    }

    public async Task ActivateAgentAsync()
    {
        if (_agent != null)
        {
            return; // 已激活
        }

        var agentId = this.GetPrimaryKeyString();
        _logger.LogInformation("Activating agent {AgentId} in Orleans grain", agentId);

        try
        {
            // 从配置或元数据获取代理类型
            var agentType = _state.State?.AgentType ?? await GetAgentTypeFromConfigurationAsync(agentId);
            if (string.IsNullOrEmpty(agentType))
            {
                throw new InvalidOperationException($"Agent type not specified for {agentId}");
            }

            await InitializeAgentAsync(agentId, agentType, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating agent {AgentId}", agentId);
            throw;
        }
    }

    private async Task<string> GetAgentTypeFromConfigurationAsync(string agentId)
    {
        // 从配置服务或注册表获取代理类型
        var configuration = _serviceProvider.GetService<IConfiguration>();
        return configuration?[$"Agents:{agentId}:Type"];
    }
}

// Grain状态
[Serializable]
public class AgentGrainState
{
    public string AgentType { get; set; }
    public object AgentState { get; set; }
    public DateTime LastModified { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
```

### 3. Orleans运行时管理器

```csharp
public class OrleansAgentRuntime : IAgentRuntime
{
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<OrleansAgentRuntime> _logger;
    private readonly OrleansRuntimeConfiguration _configuration;

    public string RuntimeId { get; }
    public string RuntimeType => "Orleans";
    public RuntimeCapabilities Capabilities { get; }

    public OrleansAgentRuntime(IClusterClient clusterClient, IOptions<OrleansRuntimeConfiguration> configuration, ILogger<OrleansAgentRuntime> logger)
    {
        _clusterClient = clusterClient;
        _logger = logger;
        _configuration = configuration.Value;

        RuntimeId = $"orleans-{_clusterClient.ClusterId}-{Guid.NewGuid():N}";

        // 配置能力
        Capabilities = new RuntimeCapabilities
        {
            SupportsPersistence = true,
            SupportsClustering = true,
            SupportsRemoting = true,
            SupportsLoadBalancing = true,
            SupportsFaultTolerance = true,
            SupportsScaling = true,
            MaxConcurrentAgents = _configuration.MaxConcurrentAgents,
            MaxAgentLifetime = Timeout.InfiniteTimeSpan
        };
    }

    public async Task InitializeAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing OrleansAgentRuntime with ID {RuntimeId}", RuntimeId);

        // 确保集群客户端已连接
        await _clusterClient.Connect(async error =>
        {
            _logger.LogError(error, "Orleans cluster connection failed");
            return true; // 重试连接
        });

        _logger.LogInformation("Connected to Orleans cluster {ClusterId}", _clusterClient.ClusterId);
    }

    public async Task<IGAgentActor> CreateAgentAsync(string agentId, Type agentType, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating Orleans grain for agent {AgentId} of type {AgentType}", agentId, agentType.Name);

        try
        {
            // 获取grain引用
            var grain = _clusterClient.GetGrain<IGAgentGrain>(agentId);

            // 配置grain（设置代理类型）
            // 这里需要通过grain方法设置代理类型，因为Orleans需要知道类型来创建实例
            await grain.ActivateAgentAsync(); // 假设代理类型通过配置确定

            // 创建Orleans Actor包装器
            var actor = new OrleansAgentActor(agentId, grain, _logger);

            _logger.LogInformation("Orleans grain for agent {AgentId} created successfully", agentId);
            return actor;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Orleans grain for agent {AgentId}", agentId);
            throw;
        }
    }

    public async Task<IGAgentActor> GetAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var grain = _clusterClient.GetGrain<IGAgentGrain>(agentId);
            var actor = new OrleansAgentActor(agentId, grain, _logger);

            return actor;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Orleans grain for agent {AgentId}", agentId);
            return null;
        }
    }

    public async Task RemoveAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var grain = _clusterClient.GetGrain<IGAgentGrain>(agentId);
            await grain.DeactivateAgentAsync();

            _logger.LogInformation("Orleans grain for agent {AgentId} deactivated", agentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deactivate Orleans grain for agent {AgentId}", agentId);
        }
    }

    public async Task<List<AgentInfo>> GetAgentsAsync(CancellationToken cancellationToken = default)
    {
        // 从Orleans管理接口获取代理列表
        var managementGrain = _clusterClient.GetGrain<IManagementGrain>(0);
        var statistics = await managementGrain.GetSimpleGrainStatistics();

        var agentInfos = statistics.Select(stat => new AgentInfo
        {
            AgentId = stat.GrainType,
            AgentType = stat.GrainType,
            RuntimeId = RuntimeId,
            Status = AgentStatus.Active, // Orleans grains are active if they exist
            CreatedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            Metadata = new Dictionary<string, object>
            {
                ["activation_count"] = stat.ActivationCount,
                ["grain_type"] = stat.GrainType
            }
        }).ToList();

        return agentInfos;
    }

    public async Task<RuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var managementGrain = _clusterClient.GetGrain<IManagementGrain>(0);
            var hosts = await managementGrain.GetHosts();

            return hosts.Any() ? RuntimeStatus.Running : RuntimeStatus.Faulted;
        }
        catch
        {
            return RuntimeStatus.Faulted;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping OrleansAgentRuntime");

        try
        {
            await _clusterClient.Close();
            _logger.LogInformation("Disconnected from Orleans cluster");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping Orleans runtime");
        }
    }

    public async Task DisposeAsync()
    {
        await StopAsync();
        _clusterClient?.Dispose();
    }
}
```

## ⚡ ProtoActor Runtime 实现

### 1. ProtoActor运行时特点

- **高性能**: 优化的消息传递和调度
- **轻量级**: 最小内存占用
- **邮箱模型**: 异步消息处理
- **分布式**: 支持远程Actor
- **适用场景**: 高吞吐量、低延迟、资源受限环境

### 2. ProtoActor Actor实现

```csharp
// ProtoActor Actor定义
public class GAgentActor : IActor
{
    private readonly IGAgent _agent;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly string _agentId;

    private readonly Behavior<object> _behavior;
    private PID _self;

    public GAgentActor(IGAgent agent, IServiceProvider serviceProvider, ILogger logger)
    {
        _agent = agent;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _agentId = agent.Id;

        // 定义行为
        _behavior = new Behavior<object>()
            .When<EventEnvelope>(HandleEventAsync)
            .When<RequestMessage>(HandleRequestAsync)
            .When<ActivationMessage>(HandleActivationAsync)
            .When<DeactivationMessage>(HandleDeactivationAsync);
    }

    public async Task ReceiveAsync(IContext context)
    {
        _self = context.Self;
        await _behavior.ReceiveAsync(context);
    }

    private async Task HandleEventAsync(IContext context, EventEnvelope envelope)
    {
        try
        {
            _logger.LogDebug("ProtoActor {AgentId} handling event {EventId}", _agentId, envelope.Id);

            if (_agent is IEventHandler eventHandler)
            {
                await eventHandler.HandleAsync(envelope);
            }

            // 回复确认
            context.Respond(new EventHandledResponse { Success = true, EventId = envelope.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling event {EventId} in ProtoActor {AgentId}", envelope.Id, _agentId);
            context.Respond(new EventHandledResponse { Success = false, Error = ex.Message });
        }
    }

    private async Task HandleRequestAsync(IContext context, RequestMessage request)
    {
        try
        {
            _logger.LogDebug("ProtoActor {AgentId} handling request {RequestId}", _agentId, request.RequestId);

            // 使用反射调用请求处理方法
            var method = _agent.GetType().GetMethod("HandleRequestAsync", new[] { request.RequestType });
            if (method != null)
            {
                var result = method.Invoke(_agent, new object[] { request.RequestData });

                if (result is Task task)
                {
                    await task;
                    var taskType = task.GetType();
                    if (taskType.IsGenericType && taskType.GetGenericTypeDefinition() == typeof(Task<>))
                    {
                        var resultProperty = taskType.GetProperty("Result");
                        var taskResult = resultProperty?.GetValue(task);
                        context.Respond(new ResponseMessage { RequestId = request.RequestId, ResponseData = taskResult });
                    }
                    else
                    {
                        context.Respond(new ResponseMessage { RequestId = request.RequestId });
                    }
                }
                else
                {
                    context.Respond(new ResponseMessage { RequestId = request.RequestId, ResponseData = result });
                }
            }
            else
            {
                context.Respond(new ResponseMessage
                {
                    RequestId = request.RequestId,
                    Error = $"No handler found for request type {request.RequestType.Name}"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling request {RequestId} in ProtoActor {AgentId}", request.RequestId, _agentId);
            context.Respond(new ResponseMessage { RequestId = request.RequestId, Error = ex.Message });
        }
    }

    private async Task HandleActivationAsync(IContext context, ActivationMessage message)
    {
        try
        {
            _logger.LogInformation("Activating ProtoActor {AgentId}", _agentId);

            if (_agent is IActivatable activatable)
            {
                await activatable.ActivateAsync();
            }

            context.Respond(new ActivationResponse { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating ProtoActor {AgentId}", _agentId);
            context.Respond(new ActivationResponse { Success = false, Error = ex.Message });
        }
    }

    private async Task HandleDeactivationAsync(IContext context, DeactivationMessage message)
    {
        try
        {
            _logger.LogInformation("Deactivating ProtoActor {AgentId}", _agentId);

            if (_agent is IActivatable activatable)
            {
                await activatable.DeactivateAsync();
            }

            context.Respond(new DeactivationResponse { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating ProtoActor {AgentId}", _agentId);
            context.Respond(new DeactivationResponse { Success = false, Error = ex.Message });
        }
    }
}

// 消息类型
public class RequestMessage
{
    public string RequestId { get; init; }
    public Type RequestType { get; init; }
    public object RequestData { get; init; }
}

public class ResponseMessage
{
    public string RequestId { get; init; }
    public object ResponseData { get; init; }
    public string Error { get; init; }
    public bool IsSuccess => string.IsNullOrEmpty(Error);
}
```

### 3. ProtoActor运行时管理器

```csharp
public class ProtoActorRuntime : IAgentRuntime
{
    private readonly ActorSystem _actorSystem;
    private readonly RootContext _rootContext;
    private readonly ILogger<ProtoActorRuntime> _logger;
    private readonly ProtoActorRuntimeConfiguration _configuration;

    private readonly ConcurrentDictionary<string, PID> _agentPids;
    private readonly ConcurrentDictionary<string, string> _agentTypes;

    public string RuntimeId { get; }
    public string RuntimeType => "ProtoActor";
    public RuntimeCapabilities Capabilities { get; }

    public ProtoActorRuntime(ActorSystem actorSystem, IOptions<ProtoActorRuntimeConfiguration> configuration, ILogger<ProtoActorRuntime> logger)
    {
        _actorSystem = actorSystem;
        _rootContext = new RootContext(_actorSystem);
        _logger = logger;
        _configuration = configuration.Value;

        RuntimeId = $"protoactor-{Environment.MachineName}-{Guid.NewGuid():N}";

        // 配置能力
        Capabilities = new RuntimeCapabilities
        {
            SupportsPersistence = false,
            SupportsClustering = true,
            SupportsRemoting = true,
            SupportsLoadBalancing = true,
            SupportsFaultTolerance = true,
            SupportsScaling = true,
            MaxConcurrentAgents = _configuration.MaxConcurrentAgents,
            MaxAgentLifetime = Timeout.InfiniteTimeSpan
        };

        _agentPids = new ConcurrentDictionary<string, PID>();
        _agentTypes = new ConcurrentDictionary<string, string>();
    }

    public async Task InitializeAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing ProtoActorRuntime with ID {RuntimeId}", RuntimeId);

        // 启动Actor系统
        await _actorSystem.StartAsync();

        _logger.LogInformation("ProtoActor system started successfully");
    }

    public async Task<IGAgentActor> CreateAgentAsync(string agentId, Type agentType, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating ProtoActor for agent {AgentId} of type {AgentType}", agentId, agentType.Name);

        try
        {
            // 创建代理实例
            var agent = ActivatorUtilities.CreateInstance(_serviceProvider, agentType) as IGAgent;
            if (agent == null)
            {
                throw new InvalidOperationException($"Failed to create agent instance of type {agentType.Name}");
            }

            // 创建Actor Props
            var props = Props.FromProducer(() => new GAgentActor(agent, _serviceProvider, _logger));

            // 启动Actor
            var pid = _rootContext.Spawn(props);

            // 注册代理
            _agentPids[agentId] = pid;
            _agentTypes[agentId] = agentType.FullName;

            // 激活Actor
            var activationResponse = await _rootContext.RequestAsync<ActivationResponse>(pid, new ActivationMessage());
            if (!activationResponse.Success)
            {
                throw new InvalidOperationException($"Failed to activate ProtoActor: {activationResponse.Error}");
            }

            // 创建Actor包装器
            var actor = new ProtoActorAgentActor(agentId, pid, _rootContext, _logger);

            _logger.LogInformation("ProtoActor for agent {AgentId} created and activated successfully", agentId);
            return actor;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ProtoActor for agent {AgentId}", agentId);
            throw;
        }
    }

    public async Task<IGAgentActor> GetAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (_agentPids.TryGetValue(agentId, out var pid))
        {
            var actor = new ProtoActorAgentActor(agentId, pid, _rootContext, _logger);
            return actor;
        }

        _logger.LogWarning("ProtoActor for agent {AgentId} not found", agentId);
        return null;
    }

    public async Task RemoveAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (_agentPids.TryRemove(agentId, out var pid))
        {
            try
            {
                // 停用Actor
                var deactivationResponse = await _rootContext.RequestAsync<DeactivationResponse>(pid, new DeactivationMessage());
                if (!deactivationResponse.Success)
                {
                    _logger.LogWarning("Failed to deactivate ProtoActor for agent {AgentId}: {Error}",
                        agentId, deactivationResponse.Error);
                }

                // 停止Actor
                _rootContext.Stop(pid);

                // 清理注册
                _agentTypes.TryRemove(agentId, out _);

                _logger.LogInformation("ProtoActor for agent {AgentId} removed successfully", agentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing ProtoActor for agent {AgentId}", agentId);
            }
        }
        else
        {
            _logger.LogWarning("ProtoActor for agent {AgentId} not found for removal", agentId);
        }
    }

    public async Task<RuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 检查Actor系统状态
            return _actorSystem.Status == SystemStatus.Running ? RuntimeStatus.Running : RuntimeStatus.Faulted;
        }
        catch
        {
            return RuntimeStatus.Faulted;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping ProtoActorRuntime");

        try
        {
            // 停止所有Actor
            var stopTasks = _agentPids.Values.Select(pid => Task.Run(() => _rootContext.Stop(pid)));
            await Task.WhenAll(stopTasks);

            // 停止Actor系统
            await _actorSystem.ShutdownAsync();

            _logger.LogInformation("ProtoActor system stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping ProtoActor runtime");
        }
    }

    public async Task DisposeAsync()
    {
        await StopAsync();
        _actorSystem?.Dispose();
    }
}
```

## 📊 运行时对比与选择

### 1. 运行时特性对比

| 特性 | Local Runtime | Orleans Runtime | ProtoActor Runtime |
|------|---------------|-----------------|-------------------|
| **部署复杂度** | 简单 | 复杂 | 中等 |
| **性能** | 极高(进程内) | 高 | 极高 |
| **可扩展性** | 无 | 优秀 | 良好 |
| **容错能力** | 无 | 优秀 | 良好 |
| **状态持久化** | 无 | 支持 | 需实现 |
| **集群支持** | 无 | 原生支持 | 支持 |
| **内存占用** | 最低 | 中等 | 低 |
| **调试便利性** | 最佳 | 中等 | 中等 |
| **学习曲线** | 平缓 | 陡峭 | 中等 |

### 2. 选择建议

#### 选择Local Runtime的场景：
- **开发测试环境**
- **单进程应用**
- **原型开发**
- **教学演示**
- **资源极其受限的环境**

#### 选择Orleans Runtime的场景：
- **生产环境**
- **分布式系统**
- **高可用要求**
- **需要状态持久化**
- **团队有分布式系统经验**

#### 选择ProtoActor Runtime的场景：
- **高性能要求**
- **高吞吐量系统**
- **微服务架构**
- **需要跨语言支持**
- **对延迟敏感的应用**

## 🔧 运行时配置

### 1. 运行时配置基类

```csharp
public abstract class RuntimeConfiguration
{
    public string RuntimeId { get; set; }
    public int MaxConcurrentAgents { get; set; } = 1000;
    public TimeSpan AgentTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public bool EnableMetrics { get; set; } = true;
    public bool EnableTracing { get; set; } = true;
    public Dictionary<string, object> CustomSettings { get; set; } = new();
}

// Local运行时配置
public class LocalRuntimeConfiguration : RuntimeConfiguration
{
    public int EventProcessingConcurrency { get; set; } = Environment.ProcessorCount;
    public int EventChannelCapacity { get; set; } = 10000;
    public bool EnableEventBatching { get; set; } = true;
    public TimeSpan AgentIdleTimeout { get; set; } = TimeSpan.FromMinutes(10);
}

// Orleans运行时配置
public class OrleansRuntimeConfiguration : RuntimeConfiguration
{
    public string ClusterId { get; set; } = "aevatar-cluster";
    public string ServiceId { get; set; } = "aevatar-service";
    public bool EnableClustering { get; set; } = true;
    public string StorageProvider { get; set; } = "Default";
    public TimeSpan GrainCollectionAge { get; set; } = TimeSpan.FromHours(2);
    public int SiloPort { get; set; } = 11111;
    public int GatewayPort { get; set; } = 30000;
}

// ProtoActor运行时配置
public class ProtoActorRuntimeConfiguration : RuntimeConfiguration
{
    public int DispatcherThroughput { get; set; } = 300;
    public TimeSpan MailboxIdleTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public bool EnableRemote { get; set; } = false;
    public int RemotePort { get; set; } = 8080;
    public string RemoteHost { get; set; } = "localhost";
}
```

### 2. 运行时选择配置

```csharp
public class RuntimeSelectionOptions
{
    public string DefaultRuntime { get; set; } = "Local";
    public Dictionary<string, RuntimeConfiguration> RuntimeConfigurations { get; set; } = new();
    public Dictionary<string, string> AgentRuntimeMappings { get; set; } = new();
    public RuntimeSelectionStrategy SelectionStrategy { get; set; } = RuntimeSelectionStrategy.Static;
}

public enum RuntimeSelectionStrategy
{
    Static,         // 静态配置
    Dynamic,        // 基于负载动态选择
    RoundRobin,     // 轮询
    LeastLoaded,    // 最少负载
    AffinityBased   // 亲和性基于
}
```

---

*本文档详细描述了多运行时架构的设计，包括统一抽象、各运行时实现细节、性能对比和配置选项，为选择合适的运行时提供全面指导。*