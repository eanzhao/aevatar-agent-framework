using Aevatar.Agents.Abstractions;
using Google.Protobuf;

namespace Aevatar.Agents.Orleans;

/// <summary>
/// Orleans Grain实现的代理Actor
/// </summary>
public class OrleansGAgentGrain<TState> : Grain, IGAgentGrain where TState : class, new()
{
    private IGAgent<TState>? _businessAgent;
    private IMessageStream? _stream;
    private readonly Dictionary<Guid, IGAgentGrain> _subAgentGrains = new();
    private readonly Dictionary<Guid, List<EventEnvelope>> _eventStore = new();
    private IMessageSerializer? _serializer;
    private IGAgentFactory? _factory;

    public async Task InitializeAsync(
        IGAgent<TState> businessAgent,
        IMessageStream stream,
        IMessageSerializer serializer,
        IGAgentFactory factory,
        Dictionary<Guid, List<EventEnvelope>> eventStore)
    {
        _businessAgent = businessAgent;
        _stream = stream;
        _serializer = serializer;
        _factory = factory;
        
        // 从共享事件存储中恢复状态
        if (eventStore.TryGetValue(businessAgent.Id, out var events))
        {
            foreach (var evt in events)
            {
                await _businessAgent.ApplyEventAsync(evt);
            }
        }
        else
        {
            eventStore[businessAgent.Id] = new List<EventEnvelope>();
        }
    }

    public Task<Guid> GetIdAsync()
    {
        if (_businessAgent == null)
            throw new InvalidOperationException("Grain not initialized");
            
        return Task.FromResult(_businessAgent.Id);
    }

    public async Task AddSubAgentAsync(Type businessAgentType, Type stateType, Guid subAgentId)
    {
        if (_businessAgent == null || _factory == null)
            throw new InvalidOperationException("Grain not initialized");

        // 通过反射调用泛型方法
        var method = _factory.GetType()
            .GetMethod(nameof(IGAgentFactory.CreateAgentAsync))
            ?.MakeGenericMethod(businessAgentType, stateType);

        if (method == null)
            throw new InvalidOperationException("CreateAgentAsync method not found");

        var subAgentActorTask = method.Invoke(_factory, new object[] { subAgentId, CancellationToken.None }) as Task<IGAgentActor>;
        if (subAgentActorTask == null)
            throw new InvalidOperationException("Failed to create sub agent");

        var subAgentActor = await subAgentActorTask;

        // 通过反射调用业务代理的泛型方法
        var businessMethod = _businessAgent.GetType()
            .GetMethod(nameof(IGAgent<TState>.AddSubAgentAsync))
            ?.MakeGenericMethod(businessAgentType, stateType);

        if (businessMethod == null)
            throw new InvalidOperationException("AddSubAgentAsync method not found");

        await (Task)(businessMethod.Invoke(_businessAgent, new object[] { CancellationToken.None }) 
            ?? Task.CompletedTask);

        // 保存事件
        _eventStore[_businessAgent.Id].AddRange(_businessAgent.GetPendingEvents());

        // 订阅父流
        await subAgentActor.SubscribeToParentStreamAsync(
            new OrleansGAgentActor<TState>(_businessAgent, this, _factory!, _stream!, _serializer!));
    }

    public async Task RemoveSubAgentAsync(Guid subAgentId)
    {
        if (_businessAgent == null)
            throw new InvalidOperationException("Grain not initialized");

        if (_subAgentGrains.Remove(subAgentId))
        {
            await _businessAgent.RemoveSubAgentAsync(subAgentId);
            _eventStore[_businessAgent.Id].AddRange(_businessAgent.GetPendingEvents());
        }
    }

    public async Task ProduceEventAsync(byte[] serializedMessage, string messageTypeName)
    {
        if (_stream == null || _serializer == null)
            throw new InvalidOperationException("Grain not initialized");

        // 反序列化消息
        var messageType = Type.GetType(messageTypeName);
        if (messageType == null)
            throw new InvalidOperationException($"Message type not found: {messageTypeName}");

        var deserializeMethod = _serializer.GetType()
            .GetMethod(nameof(IMessageSerializer.Deserialize))
            ?.MakeGenericMethod(messageType);

        if (deserializeMethod == null)
            throw new InvalidOperationException("Deserialize method not found");

        var message = deserializeMethod.Invoke(_serializer, new object[] { serializedMessage }) as IMessage;
        if (message == null)
            throw new InvalidOperationException("Failed to deserialize message");

        // 产生事件
        var produceMethod = _stream.GetType()
            .GetMethod(nameof(IMessageStream.ProduceAsync))
            ?.MakeGenericMethod(messageType);

        if (produceMethod == null)
            throw new InvalidOperationException("ProduceAsync method not found");

        await (Task)(produceMethod.Invoke(_stream, new object[] { message, CancellationToken.None }) 
            ?? Task.CompletedTask);
    }

    public async Task SubscribeToParentStreamAsync(Guid parentId)
    {
        if (_businessAgent == null || _stream == null)
            throw new InvalidOperationException("Grain not initialized");

        // 注册事件处理器到流
        await _businessAgent.RegisterEventHandlersAsync(_stream);
    }
}

