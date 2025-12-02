using Aevatar.Agents;

namespace Aevatar.Agents.Abstractions.CQRS;

/// <summary>
/// State projector interface for CQRS pattern.
/// Responsible for receiving state changes and projecting them to read models.
/// </summary>
public interface IStateProjector
{
    /// <summary>
    /// Project state to the read model.
    /// </summary>
    /// <param name="wrapper">State wrapper containing the state data</param>
    /// <param name="ct">Cancellation token</param>
    Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default);
}

