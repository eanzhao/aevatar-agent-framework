namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Message stream provider interface.
/// Responsible for providing message streams for agents.
/// </summary>
public interface IMessageStreamProvider
{
    /// <summary>
    /// Gets the message stream for the specified agent.
    /// </summary>
    /// <param name="agentId">The agent identifier.</param>
    /// <returns>The message stream instance.</returns>
    IMessageStream GetStream(Guid agentId);
}

