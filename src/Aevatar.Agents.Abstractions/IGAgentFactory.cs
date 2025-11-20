namespace Aevatar.Agents.Abstractions;

public interface IGAgentFactory
{
    IGAgent CreateGAgent(Guid id, Type agentType, CancellationToken ct = default);
    
    TAgent CreateGAgent<TAgent>(Guid id, CancellationToken ct = default)
        where TAgent : IGAgent;

    TAgent CreateGAgent<TAgent>(CancellationToken ct = default)
        where TAgent : IGAgent;
}