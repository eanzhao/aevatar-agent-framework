using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Aevatar.Agents.Orleans;

/// <summary>
/// Orleans Grain 实现
/// 包装 GAgentBase 并提供事件路由
/// </summary>
public class OrleansGAgentGrain : Grain, IStandardGAgentGrain, IEventPublisher
{
    private GAgentBase<object>? _agent;
    private IGrainFactory? _grainFactory;
    
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
        
        // 初始化Orleans Streaming
        try
        {
            _streamProvider = this.GetStreamProvider("StreamProvider");
            var agentId = await GetIdAsync();
            
            // 创建自己的Stream
            var streamId = StreamId.Create(AevatarAgentsOrleansConstants.StreamNamespace, agentId.ToString());
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
        if (_streamProvider != null)
        {
            var streamId = StreamId.Create(AevatarAgentsOrleansConstants.StreamNamespace, childId.ToString());
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
        if (_streamProvider != null)
        {
            var streamId = StreamId.Create(AevatarAgentsOrleansConstants.StreamNamespace, parentId.ToString());
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
                            return envelope.Payload.TypeUrl.Contains(eventType.Name) ||
                                   envelope.Payload.TypeUrl.Contains(eventType.FullName);
                        };
                        break;
                    }
                    baseType = baseType.BaseType;
                }
                
                _parentStreamSubscription = await messageStream.SubscribeAsync<EventEnvelope>(
                    async envelope =>
                    {
                        // 处理从父节点stream接收到的事件
                        await _agent.HandleEventAsync(envelope);
                    },
                    filter);
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
                var logger =
                    ServiceProvider?.GetService(typeof(ILogger<OrleansGAgentGrain>)) as ILogger<OrleansGAgentGrain>;
                logger?.LogError(ex, "Error handling event {EventId} in agent {AgentId}",
                    envelope.Id, this.GetPrimaryKeyString());
            }
        }

        // 继续路由事件
        await ContinuePropagationAsync(envelope);
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
                var agentType = System.Type.GetType(agentTypeName);
                var stateType = System.Type.GetType(stateTypeName);
                
                if (agentType != null && stateType != null)
                {
                    // 创建Agent实例
                    var agentId = await GetIdAsync();
                    var agent = Activator.CreateInstance(agentType, agentId) as GAgentBase<object>;
                    
                    if (agent != null)
                    {
                        SetAgent(agent);
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