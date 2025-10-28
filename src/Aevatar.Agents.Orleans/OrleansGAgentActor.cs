using Aevatar.Agents.Abstractions;
using Google.Protobuf;

namespace Aevatar.Agents.Orleans;

/// <summary>
/// Orleans实现的代理Actor包装器
/// </summary>
public class OrleansGAgentActor<TState> : IGAgentActor where TState : class, new()
{
    private readonly IGAgent<TState> _businessAgent;
    private readonly IGAgentGrain _grain;
    private readonly IGAgentFactory _factory;
    private readonly IMessageStream _stream;
    private readonly IMessageSerializer _serializer;

    public OrleansGAgentActor(
        IGAgent<TState> businessAgent,
        IGAgentGrain grain,
        IGAgentFactory factory,
        IMessageStream stream,
        IMessageSerializer serializer)
    {
        _businessAgent = businessAgent;
        _grain = grain;
        _factory = factory;
        _stream = stream;
        _serializer = serializer;
    }

    public Guid Id => _businessAgent.Id;

    public async Task AddSubAgentAsync<TSubAgent, TSubState>(CancellationToken ct = default)
        where TSubAgent : IGAgent<TSubState>
        where TSubState : class, new()
    {
        var subAgentId = Guid.NewGuid();
        await _grain.AddSubAgentAsync(typeof(TSubAgent), typeof(TSubState), subAgentId);
    }

    public async Task RemoveSubAgentAsync(Guid subAgentId, CancellationToken ct = default)
    {
        await _grain.RemoveSubAgentAsync(subAgentId);
    }

    public async Task ProduceEventAsync(IMessage message, CancellationToken ct = default)
    {
        // 序列化消息
        var serialized = _serializer.Serialize(message);
        var messageTypeName = message.GetType().AssemblyQualifiedName 
            ?? throw new InvalidOperationException("Failed to get message type name");

        await _grain.ProduceEventAsync(serialized, messageTypeName);
    }

    public async Task SubscribeToParentStreamAsync(IGAgentActor parent, CancellationToken ct = default)
    {
        await _grain.SubscribeToParentStreamAsync(parent.Id);
    }

    /// <summary>
    /// 获取内部Grain引用
    /// </summary>
    public IGAgentGrain GetGrain() => _grain;
}

