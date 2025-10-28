using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Streams;

namespace Aevatar.Agents.Orleans;

/// <summary>
/// Orleans实现的代理工厂
/// </summary>
public class OrleansGAgentFactory : IGAgentFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Guid, List<EventEnvelope>> _eventStore;
    private readonly IGrainFactory _grainFactory;
    private readonly IStreamProvider? _streamProvider;

    /// <summary>
    /// 构造函数（不使用Orleans Streams）
    /// </summary>
    public OrleansGAgentFactory(
        IServiceProvider serviceProvider,
        IGrainFactory grainFactory)
    {
        _serviceProvider = serviceProvider;
        _grainFactory = grainFactory;
        _eventStore = new Dictionary<Guid, List<EventEnvelope>>();
    }

    /// <summary>
    /// 构造函数（使用Orleans Streams）
    /// </summary>
    public OrleansGAgentFactory(
        IServiceProvider serviceProvider,
        IGrainFactory grainFactory,
        IStreamProvider streamProvider)
    {
        _serviceProvider = serviceProvider;
        _grainFactory = grainFactory;
        _streamProvider = streamProvider;
        _eventStore = new Dictionary<Guid, List<EventEnvelope>>();
    }

    public async Task<IGAgentActor> CreateAgentAsync<TBusiness, TState>(Guid id, CancellationToken ct = default)
        where TBusiness : IGAgent<TState>
        where TState : class, new()
    {
        // 获取必要的服务
        var serializer = _serviceProvider.GetRequiredService<IMessageSerializer>();
        var businessAgent = _serviceProvider.GetRequiredService<TBusiness>();

        // 创建消息流
        IMessageStream stream;
        if (_streamProvider != null)
        {
            var orleansStream = _streamProvider.GetStream<byte[]>(
                StreamId.Create("AgentStream", id));
            stream = new OrleansMessageStream(serializer, id, orleansStream);
        }
        else
        {
            stream = new OrleansMessageStream(serializer, id);
        }

        // 获取Grain
        var grain = _grainFactory.GetGrain<IGAgentGrain>(id);

        // 初始化Grain（通过反射调用泛型方法）
        var grainType = typeof(OrleansGAgentGrain<>).MakeGenericType(typeof(TState));
        var grainInstance = _grainFactory.GetGrain(grainType, id);

        var initMethod = grainType.GetMethod("InitializeAsync");
        if (initMethod != null)
        {
            await (Task)(initMethod.Invoke(grainInstance, new object[]
            {
                businessAgent,
                stream,
                serializer,
                this,
                _eventStore
            }) ?? Task.CompletedTask);
        }

        // 创建代理Actor包装器
        var actorAgent = new OrleansGAgentActor<TState>(
            businessAgent,
            grain,
            this,
            stream,
            serializer);

        // 如果事件存储中有事件，应用它们
        if (_eventStore.ContainsKey(id))
        {
            foreach (var evt in _eventStore[id])
            {
                await businessAgent.ApplyEventAsync(evt, ct);
            }
        }
        else
        {
            _eventStore[id] = new List<EventEnvelope>();
        }

        return actorAgent;
    }
}

