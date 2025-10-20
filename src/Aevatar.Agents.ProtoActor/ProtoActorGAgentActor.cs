using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Proto;

namespace Aevatar.Agents.ProtoActor;

/// <summary>
/// Proto.Actor实现的代理Actor
/// </summary>
public class ProtoActorGAgentActor<TState> : IGAgentActor where TState : class, new()
{
    private readonly IGAgent<TState> _businessAgent;
    private readonly IRootContext _rootContext;
    private readonly PID _actorPid;
    private readonly IGAgentFactory _factory;
    private readonly IMessageStream _stream;
    private readonly Dictionary<Guid, PID> _subAgentPids = new();

    public ProtoActorGAgentActor(
        IGAgent<TState> businessAgent, 
        IRootContext rootContext, 
        PID actorPid, 
        IGAgentFactory factory,
        IMessageStream stream)
    {
        _businessAgent = businessAgent;
        _rootContext = rootContext;
        _actorPid = actorPid;
        _factory = factory;
        _stream = stream;
    }

    public Guid Id => _businessAgent.Id;

    public async Task AddSubAgentAsync<TSubAgent, TSubState>(CancellationToken ct = default) 
        where TSubAgent : IGAgent<TSubState> 
        where TSubState : class, new()
    {
        // 创建子代理Actor
        var subAgentActor = await _factory.CreateAgentAsync<TSubAgent, TSubState>(Guid.NewGuid(), ct);
        
        // 在业务代理中添加子代理
        await _businessAgent.AddSubAgentAsync<TSubAgent, TSubState>(ct);
        
        // 如果是ProtoActorGAgentActor，保存PID引用
        if (subAgentActor is ProtoActorGAgentActor<TSubState> protoSubAgent)
        {
            _subAgentPids[protoSubAgent.Id] = protoSubAgent._actorPid;
        }
        
        // 订阅父流
        await subAgentActor.SubscribeToParentStreamAsync(this, ct);
    }

    public async Task RemoveSubAgentAsync(Guid subAgentId, CancellationToken ct = default)
    {
        // 从业务代理中移除子代理
        await _businessAgent.RemoveSubAgentAsync(subAgentId, ct);
        
        // 如果存在PID引用，停止Actor并移除引用
        if (_subAgentPids.TryGetValue(subAgentId, out var pid))
        {
            await _rootContext.StopAsync(pid);
            _subAgentPids.Remove(subAgentId);
        }
    }

    public async Task ProduceEventAsync(IMessage message, CancellationToken ct = default)
    {
        // 使用消息流产生事件
        await _stream.ProduceAsync(message, ct);
    }

    public async Task SubscribeToParentStreamAsync(IGAgentActor parent, CancellationToken ct = default)
    {
        // 注册事件处理器到流
        await _businessAgent.RegisterEventHandlersAsync(_stream, ct);
    }
    
    /// <summary>
    /// 获取内部Actor的PID
    /// </summary>
    public PID GetActorPid() => _actorPid;
}
