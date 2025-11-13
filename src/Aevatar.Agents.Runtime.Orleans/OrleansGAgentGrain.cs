using System.Linq;
using System.Reflection;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Agent包装器，用于包装非object状态类型的agent
/// </summary>
internal class AgentWrapper : GAgentBase<object>
{
    private readonly object _innerAgent;
    
    // 缓存反射 MethodInfo 以提升性能
    private readonly MethodInfo? _handleEventAsyncMethod;
    private readonly MethodInfo? _getStateMethod;
    private readonly MethodInfo? _getDescriptionAsyncMethod;
    
    public AgentWrapper(object innerAgent) : base(GetAgentId(innerAgent))
    {
        _innerAgent = innerAgent ?? throw new ArgumentNullException(nameof(innerAgent));
        
        var innerType = _innerAgent.GetType();
        
        // 预先缓存所有需要的 MethodInfo
        _handleEventAsyncMethod = innerType.GetMethod(
            nameof(HandleEventAsync), 
            new[] { typeof(EventEnvelope), typeof(CancellationToken) });
            
        _getStateMethod = innerType.GetMethod(nameof(GetState), System.Type.EmptyTypes);
        _getDescriptionAsyncMethod = innerType.GetMethod(nameof(GetDescriptionAsync), System.Type.EmptyTypes);
    }
    
    private static Guid GetAgentId(object agent)
    {
        if (agent == null)
            throw new ArgumentNullException(nameof(agent));
            
        var idProperty = agent.GetType().GetProperty("Id");
        if (idProperty != null && idProperty.CanRead)
        {
            var id = idProperty.GetValue(agent);
            if (id is Guid guidId)
            {
                return guidId;
            }
        }
        return Guid.NewGuid();
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        if (_getDescriptionAsyncMethod != null)
        {
            try
            {
                var result = _getDescriptionAsyncMethod.Invoke(_innerAgent, null);
                if (result is Task<string> task)
                {
                    return task;
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error invoking GetDescriptionAsync on wrapped agent");
            }
        }
        return Task.FromResult($"Wrapped {_innerAgent.GetType().Name}");
    }
    
    /// <summary>
    /// 覆盖 GetState 以返回内部 agent 的 state
    /// </summary>
    public override object GetState()
    {
        if (_getStateMethod != null)
        {
            try
            {
                return _getStateMethod.Invoke(_innerAgent, null) ?? new object();
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error invoking GetState on wrapped agent");
            }
        }
        return base.GetState();
    }
    
    /// <summary>
    /// 覆盖 HandleEventAsync 以转发到内部 agent
    /// 这样内部 agent 的 event handlers 才能被正确调用
    /// </summary>
    public override async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        // Early return pattern for better readability
        if (_handleEventAsyncMethod == null)
        {
            Logger?.LogWarning(
                "HandleEventAsync method not found on wrapped agent {AgentType}. Event {EventId} will not be processed.",
                _innerAgent.GetType().Name,
                envelope.Id);
            return;
        }
        
        try
        {
            // Invoke with validation
            var task = _handleEventAsyncMethod.Invoke(_innerAgent, new object[] { envelope, ct }) as Task
                ?? throw new InvalidOperationException(
                    $"HandleEventAsync on {_innerAgent.GetType().Name} must return Task");
            
            await task;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            // Unwrap and preserve original stack trace
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(ex.InnerException)
                .Throw();
        }
    }
}

/// <summary>
/// Orleans Grain 实现
/// 包装 GAgentBase 并提供事件路由
/// </summary>
public class OrleansGAgentGrain : Grain, IStandardGAgentGrain, IEventPublisher
{
    private GAgentBase<object>? _agent;
    private IGrainFactory? _grainFactory;
    private ILogger<OrleansGAgentGrain>? _logger;
    private StreamingOptions? _streamingOptions;
    
    // Orleans Streaming
    private IStreamProvider? _streamProvider;
    private IAsyncStream<byte[]>? _myStream;
    private StreamSubscriptionHandle<byte[]>? _streamSubscription;
    private readonly Dictionary<Guid, IAsyncStream<byte[]>> _childStreams = new();
    private IAsyncStream<byte[]>? _parentStream;
    private IMessageStreamSubscription? _parentStreamSubscription;  // 父节点stream订阅句柄

    // 层级关系
    private Guid? _parentId;
    private readonly HashSet<Guid> _childrenIds = new();

    public Task<Guid> GetIdAsync()
    {
        // 从字符串键解析Guid
        var keyString = this.GetPrimaryKeyString();
        if (Guid.TryParse(keyString, out var guid))
        {
            return Task.FromResult(guid);
        }
        return Task.FromResult(Guid.Empty);
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _grainFactory = GrainFactory;
        _logger = ServiceProvider?.GetService<ILogger<OrleansGAgentGrain>>();
        
        // Load streaming options from configuration or use defaults
        var optionsSnapshot = ServiceProvider?.GetService<IOptionsSnapshot<StreamingOptions>>();
        _streamingOptions = optionsSnapshot?.Value ?? new StreamingOptions();
        
        // 初始化Orleans Streaming
        try
        {
            _streamProvider = this.GetStreamProvider(_streamingOptions.StreamProviderName);
            var agentId = await GetIdAsync();
            
            // 创建自己的Stream - 使用配置的 namespace
            var streamId = StreamId.Create(_streamingOptions.DefaultStreamNamespace, agentId.ToString());
            _myStream = _streamProvider.GetStream<byte[]>(streamId);
            
            // 订阅自己的Stream来接收事件
            _streamSubscription = await _myStream.SubscribeAsync(OnStreamEventReceived);
        }
        catch (Exception ex)
        {
            // 如果Stream Provider未配置，记录警告但继续工作
            Console.WriteLine($"Warning: Stream provider not configured, falling back to direct calls: {ex.Message}");
        }
        
        await base.OnActivateAsync(cancellationToken);
    }
    
    private async Task OnStreamEventReceived(byte[] envelopeBytes, StreamSequenceToken? token)
    {
        try
        {
            await HandleEventAsync(envelopeBytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling stream event: {ex.Message}");
        }
    }

    /// <summary>
    /// 设置关联的 Agent（由 Factory 调用）
    /// </summary>
    public void SetAgent(GAgentBase<object> agent)
    {
        _agent = agent;
        _agent.SetEventPublisher(this);
    }

    // ============ 层级关系管理 ============

    public async Task AddChildAsync(Guid childId)
    {
        _childrenIds.Add(childId);
        
        // 如果使用Streaming，获取子节点的Stream
        if (_streamProvider != null && _streamingOptions != null)
        {
            var streamId = StreamId.Create(_streamingOptions.DefaultStreamNamespace, childId.ToString());
            var childStream = _streamProvider.GetStream<byte[]>(streamId);
            _childStreams[childId] = childStream;
        }
        
        await Task.CompletedTask;
    }

    public Task RemoveChildAsync(Guid childId)
    {
        _childrenIds.Remove(childId);
        _childStreams.Remove(childId);
        return Task.CompletedTask;
    }

    public async Task SetParentAsync(Guid parentId)
    {
        // 如果已有父节点，先清除
        if (_parentId != null)
        {
            await ClearParentAsync();
        }
        
        _parentId = parentId;
        
        // 如果使用Streaming，订阅父节点的Stream
        if (_streamProvider != null && _streamingOptions != null)
        {
            var streamId = StreamId.Create(_streamingOptions.DefaultStreamNamespace, parentId.ToString());
            _parentStream = _streamProvider.GetStream<byte[]>(streamId);
            
            // 创建Orleans MessageStream包装器并订阅
            var messageStream = new OrleansMessageStream(parentId, _parentStream);
            
            // Agent订阅父节点的stream，接收组内广播的事件
            if (_agent != null)
            {
                // 创建类型过滤器（如果Agent有特定的事件类型约束）
                Func<EventEnvelope, bool>? filter = null;
                
                // 检查Agent是否继承自GAgentBase<TState, TEvent>，获取TEvent类型
                var agentType = _agent.GetType();
                var baseType = agentType.BaseType;
                while (baseType != null)
                {
                    if (baseType.IsGenericType && 
                        baseType.GetGenericTypeDefinition() == typeof(GAgentBase<,>))
                    {
                        var eventType = baseType.GetGenericArguments()[1];
                        // 创建类型过滤器
                        filter = envelope =>
                        {
                            if (envelope.Payload == null) return false;
                            // 检查TypeUrl是否包含事件类型名
                            var eventTypeName = eventType.Name ?? string.Empty;
                            var eventTypeFullName = eventType.FullName ?? string.Empty;
                            return envelope.Payload.TypeUrl.Contains(eventTypeName) ||
                                   envelope.Payload.TypeUrl.Contains(eventTypeFullName);
                        };
                        break;
                    }
                    baseType = baseType.BaseType;
                }
                
                // 获取自己的ID
                var myId = await GetIdAsync();
                
                // 创建组合过滤器：类型过滤 + 过滤掉自己发布的事件
                Func<EventEnvelope, bool>? combinedFilter = envelope =>
                {
                    // 过滤掉自己发布的事件，避免循环
                    if (envelope.PublisherId == myId.ToString())
                    {
                        return false;
                    }
                    
                    // 应用类型过滤
                    if (filter != null)
                    {
                        return filter(envelope);
                    }
                    
                    return true;
                };
                
                _parentStreamSubscription = await messageStream.SubscribeAsync<EventEnvelope>(
                    async envelope =>
                    {
                        // 从父stream接收到的事件，直接调用Agent的HandleEventAsync
                        // AgentWrapper 会自动转发到内部 agent，触发其 event handlers
                        await _agent.HandleEventAsync(envelope, CancellationToken.None);
                    },
                    combinedFilter);
            }
        }
    }

    public async Task ClearParentAsync()
    {
        _parentId = null;
        _parentStream = null;
        
        // 取消订阅父节点的stream
        if (_parentStreamSubscription != null)
        {
            await _parentStreamSubscription.UnsubscribeAsync();
            _parentStreamSubscription = null;
        }
    }

    public Task<IReadOnlyList<Guid>> GetChildrenAsync()
    {
        return Task.FromResult<IReadOnlyList<Guid>>(_childrenIds.ToList());
    }

    public Task<Guid?> GetParentAsync()
    {
        return Task.FromResult(_parentId);
    }

    // ============ 事件发布（IEventPublisher 实现） ============

    async Task<string> IEventPublisher.PublishEventAsync<TEvent>(
        TEvent evt,
        EventDirection direction,
        CancellationToken ct)
    {
        var eventId = Guid.NewGuid().ToString();

        var envelope = new EventEnvelope
        {
            Id = eventId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Version = 1,
            Payload = Any.Pack(evt),
            CorrelationId = Guid.NewGuid().ToString(),
            PublisherId = this.GetPrimaryKeyString(),
            Direction = direction,
            ShouldStopPropagation = false,
            MaxHopCount = -1,
            CurrentHopCount = 0,
            MinHopCount = -1,
            Message = $"Published by {this.GetGrainId()}"
        };

        envelope.Publishers.Add(this.GetPrimaryKeyString());

        await RouteEventAsync(envelope, ct);

        return eventId;
    }

    // ============ 事件路由 ============

    private async Task RouteEventAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (envelope.ShouldStopPropagation)
            return;

        if (envelope.MaxHopCount > 0 && envelope.CurrentHopCount >= envelope.MaxHopCount)
            return;

        switch (envelope.Direction)
        {
            case EventDirection.Up:
                await SendToParentAsync(envelope);
                break;

            case EventDirection.Down:
                await SendToChildrenAsync(envelope);
                break;

            case EventDirection.Both:
                await SendToParentAsync(envelope);
                await SendToChildrenAsync(envelope);
                break;
        }
    }

    private async Task SendToParentAsync(EventEnvelope envelope)
    {
        if (_parentId == null)
        {
            // 没有父节点，停止向上传播
            return;
        }

        // 向上发布：发送到父节点的stream，会自动广播给所有订阅者（包括其他子节点）
        if (_parentStream != null)
        {
            try
            {
                // 发送到父节点的stream，实现组内广播
                await _parentStream.OnNextAsync(envelope.ToByteArray());
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send via stream to parent, falling back to direct call: {ex.Message}");
            }
        }
        
        // Fallback到直接调用（如果stream不可用）
        var parentGrain = _grainFactory!.GetGrain<IGAgentGrain>(_parentId.Value.ToString());
        await parentGrain.HandleEventAsync(envelope.ToByteArray());
    }

    private async Task SendToChildrenAsync(EventEnvelope envelope)
    {
        foreach (var childId in _childrenIds)
        {
            var childEnvelope = envelope.Clone();
            childEnvelope.CurrentHopCount++;
            childEnvelope.Publishers.Add(this.GetGrainId().ToString());

            // 优先使用Stream
            if (_childStreams.TryGetValue(childId, out var childStream))
            {
                try
                {
                    await childStream.OnNextAsync(childEnvelope.ToByteArray());
                    continue;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to send via stream to child {childId}, falling back to direct call: {ex.Message}");
                }
            }
            
            // Fallback到直接调用
            var childGrain = _grainFactory!.GetGrain<IGAgentGrain>(childId.ToString());
            await childGrain.HandleEventAsync(childEnvelope.ToByteArray());
        }
    }

    // ============ 事件处理 ============

    public async Task HandleEventAsync(byte[] envelopeBytes)
    {
        if (_agent == null)
        {
            throw new InvalidOperationException("Agent not set");
        }

        // 反序列化 EventEnvelope
        var envelope = EventEnvelope.Parser.ParseFrom(envelopeBytes);

        // 检查 MinHopCount
        var shouldProcess = envelope.MinHopCount <= 0 || envelope.CurrentHopCount >= envelope.MinHopCount;

        if (shouldProcess)
        {
            try
            {
                // 让 Agent 处理事件（使用反射调用）
                var handleMethod = _agent.GetType().GetMethod("HandleEventAsync");
                if (handleMethod != null)
                {
                    if (handleMethod.Invoke(_agent, [envelope, CancellationToken.None]) is Task task)
                    {
                        await task;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling event {EventId} in agent {AgentId}",
                    envelope.Id, this.GetPrimaryKeyString());
            }
        }

        // 继续路由事件
        await ContinuePropagationAsync(envelope);
    }
    
    /// <summary>
    /// 发布事件到 Orleans Stream
    /// </summary>
    public async Task PublishEventAsync(byte[] envelopeBytes)
    {
        // Publish to own stream - all subscribers will receive it
        if (_myStream != null)
        {
            try
            {
                // 直接发送原始字节，避免不必要的序列化往返
                await _myStream.OnNextAsync(envelopeBytes);
                
                // 仅在需要记录日志时才反序列化
                if (_logger != null && _logger.IsEnabled(LogLevel.Debug))
                {
                    var envelope = EventEnvelope.Parser.ParseFrom(envelopeBytes);
                    _logger.LogDebug(
                        "Published event {EventId} to Orleans Stream from agent {AgentId}",
                        envelope.Id, this.GetPrimaryKeyString());
                }
            }
            catch (Exception ex)
            {
                // 仅在发生错误时才反序列化以获取事件 ID
                string? eventId = null;
                try
                {
                    var envelope = EventEnvelope.Parser.ParseFrom(envelopeBytes);
                    eventId = envelope.Id;
                }
                catch { /* 忽略解析错误 */ }
                
                _logger?.LogError(ex, 
                    "Failed to publish event {EventId} to Orleans Stream from agent {AgentId}",
                    eventId ?? "unknown", this.GetPrimaryKeyString());
                throw;
            }
        }
        else
        {
            _logger?.LogWarning(
                "Stream provider not configured, cannot publish event from agent {AgentId}. Ensure Orleans Streaming is configured.",
                this.GetPrimaryKeyString());
        }
    }

    /// <summary>
    /// 继续传播事件
    /// </summary>
    private async Task ContinuePropagationAsync(EventEnvelope envelope)
    {
        if (envelope.ShouldStopPropagation)
            return;

        if (envelope.MaxHopCount > 0 && envelope.CurrentHopCount >= envelope.MaxHopCount)
            return;

        // 根据方向继续传播（只向下传播）
        if (envelope.Direction is EventDirection.Down or EventDirection.Both)
        {
            await SendToChildrenAsync(envelope);
        }
    }

    // ============ 生命周期 ============

    public async Task ActivateAsync(string? agentTypeName = null, string? stateTypeName = null)
    {
        // 如果提供了类型信息，创建Agent实例
        if (!string.IsNullOrEmpty(agentTypeName) && !string.IsNullOrEmpty(stateTypeName) && _agent == null)
        {
            try
            {
                // 尝试通过Type.GetType获取类型，如果失败则搜索所有加载的程序集
                var agentType = System.Type.GetType(agentTypeName);
                if (agentType == null)
                {
                    // 搜索所有程序集
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        agentType = assembly.GetTypes().FirstOrDefault(t => t.Name == agentTypeName || t.FullName == agentTypeName);
                        if (agentType != null) break;
                    }
                }
                
                var stateType = System.Type.GetType(stateTypeName);
                if (stateType == null)
                {
                    // 搜索所有程序集
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        stateType = assembly.GetTypes().FirstOrDefault(t => t.Name == stateTypeName || t.FullName == stateTypeName);
                        if (stateType != null) break;
                    }
                }
                
                if (agentType != null && stateType != null)
                {
                    // 创建Agent实例
                    var agentId = await GetIdAsync();
                    
                    // 尝试不同的构造函数
                    object? agent = null;
                    
                    // 尝试带ID参数的构造函数
                    try
                    {
                        agent = Activator.CreateInstance(agentType, agentId);
                    }
                    catch
                    {
                        // 尝试无参构造函数
                        try
                        {
                            agent = Activator.CreateInstance(agentType);
                            // 设置ID属性
                            var idProperty = agentType.GetProperty("Id");
                            if (idProperty != null && idProperty.CanWrite)
                            {
                                idProperty.SetValue(agent, agentId);
                            }
                        }
                        catch
                        {
                            // 两种构造方式都失败
                        }
                    }
                    
                    if (agent is GAgentBase<object> agentBase)
                    {
                        SetAgent(agentBase);
                    }
                    else if (agent != null)
                    {
                        // 尝试通过反射获取agent的实际类型并创建包装
                        var agentInterfaces = agent.GetType().GetInterfaces();
                        if (agentInterfaces.Any(i => i.Name.StartsWith("IStateGAgent")))
                        {
                            // 这是一个有状态的agent，但状态类型不是object
                            // 需要创建一个适配器
                            var wrapper = new AgentWrapper(agent);
                            SetAgent(wrapper);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 记录错误但不抛出，允许Grain继续工作
                Console.WriteLine($"Failed to create agent: {ex.Message}");
            }
        }
        
        // 激活Agent
        if (_agent != null)
        {
            await _agent.OnActivateAsync();
        }
    }

    public async Task DeactivateAsync()
    {
        if (_agent != null)
        {
            await _agent.OnDeactivateAsync();
        }
    }
    
    public Task<TState?> GetStateAsync<TState>() where TState : Google.Protobuf.IMessage, new()
    {
        if (_agent == null)
        {
            return Task.FromResult<TState?>(default);
        }
        
        // Get state from agent
        var state = _agent.GetState();
        
        // Return typed state if it matches the requested type
        if (state is TState typedState)
        {
            return Task.FromResult<TState?>(typedState);
        }
        
        return Task.FromResult<TState?>(default);
    }
    
    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        // 清理Stream订阅
        if (_streamSubscription != null)
        {
            try
            {
                await _streamSubscription.UnsubscribeAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error unsubscribing from stream: {ex.Message}");
            }
        }
        
        await base.OnDeactivateAsync(reason, cancellationToken);
    }
}