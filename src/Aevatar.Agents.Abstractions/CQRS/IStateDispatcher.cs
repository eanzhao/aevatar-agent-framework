using System.Threading;
using System.Threading.Tasks;

namespace Aevatar.Agents.Abstractions.CQRS;

/// <summary>
/// Dispatches agent state changes to a stream for CQRS projection.
/// Implementations can use different stream backends:
/// - Orleans Streams (OrleansStateDispatcher)
/// - MassTransit/RabbitMQ (MassTransitStateDispatcher)
/// - Kafka, etc.
/// </summary>
public interface IStateDispatcher
{
    /// <summary>
    /// Publishes a StateWrapper containing agent state to a stream.
    /// </summary>
    /// <param name="wrapper">The StateWrapper containing the agent's state.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    Task PublishAsync(StateWrapper wrapper, CancellationToken ct = default);
}
