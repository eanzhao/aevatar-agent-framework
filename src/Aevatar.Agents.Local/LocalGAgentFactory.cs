using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Local;

public class LocalGAgentFactory : IGAgentFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Guid, List<EventEnvelope>> _eventStore;

    public LocalGAgentFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _eventStore = new Dictionary<Guid, List<EventEnvelope>>();
    }

    public async Task<IGAgentActor> CreateAgentAsync<TBusiness, TState>(Guid id, CancellationToken ct = default) 
        where TBusiness : IGAgent<TState> 
        where TState : class, new()
    {
        var serializer = _serviceProvider.GetRequiredService<IMessageSerializer>();
        var stream = new LocalMessageStream(serializer, id);
        var businessAgent = _serviceProvider.GetRequiredService<TBusiness>();
        var actor = new LocalGAgentActor<TState>(stream, businessAgent, this, _eventStore);
        
        if (_eventStore.ContainsKey(id))
        {
            foreach (var evt in _eventStore[id])
            {
                await businessAgent.ApplyEventAsync(evt, ct);
            }
        }
        
        return actor;
    }
}