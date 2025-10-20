namespace Aevatar.Agents.Abstractions;

public interface IGAgentFactory
{
    Task<IGAgentActor> CreateAgentAsync<TBusiness, TState>(Guid id, CancellationToken ct = default) 
        where TBusiness : IGAgent<TState> 
        where TState : class, new();
}