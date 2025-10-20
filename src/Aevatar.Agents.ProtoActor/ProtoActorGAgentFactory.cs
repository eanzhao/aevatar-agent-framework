using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Proto;

namespace Aevatar.Agents.ProtoActor;

/// <summary>
/// Proto.Actor实现的代理工厂
/// </summary>
public class ProtoActorGAgentFactory : IGAgentFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Guid, List<EventEnvelope>> _eventStore;
    private readonly IRootContext _rootContext;
    
    public ProtoActorGAgentFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _eventStore = new Dictionary<Guid, List<EventEnvelope>>();
        _rootContext = new ActorSystem().Root;
    }

    public async Task<IGAgentActor> CreateAgentAsync<TBusiness, TState>(Guid id, CancellationToken ct = default) 
        where TBusiness : IGAgent<TState> 
        where TState : class, new()
    {
        // 获取必要的服务
        var serializer = _serviceProvider.GetRequiredService<IMessageSerializer>();
        var businessAgent = _serviceProvider.GetRequiredService<TBusiness>();
        var stream = new ProtoActorMessageStream(serializer, id, _rootContext);
        
        // 创建Actor Props
        var props = Props.FromProducer(() => 
            new AgentActor<TState>(businessAgent, serializer, stream, _eventStore));
        
        // 启动Actor
        var pid = _rootContext.SpawnNamed(props, $"agent-{id}");
        
        // 创建代理Actor包装器
        var actorAgent = new ProtoActorGAgentActor<TState>(
            businessAgent, 
            _rootContext, 
            pid, 
            this,
            stream);
        
        // 如果事件存储中有事件，应用它们
        if (_eventStore.ContainsKey(id))
        {
            foreach (var evt in _eventStore[id])
            {
                await businessAgent.ApplyEventAsync(evt, ct);
            }
        }
        
        return actorAgent;
    }
}
