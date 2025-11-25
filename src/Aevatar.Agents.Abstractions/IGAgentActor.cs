using Google.Protobuf;

namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Agent Actor runtime abstraction interface.
/// Responsibilities: Hierarchy management, Stream subscription, Event routing, Lifecycle management.
/// </summary>
public interface IGAgentActor : IEventPublisher
{
    /// <summary>
    /// Actor Identifier (same as the associated Agent Id).
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// Get the associated Agent instance.
    /// </summary>
    IGAgent GetAgent();

    // ============ Hierarchy Inspection ============

    /// <summary>
    /// Get all child Agent IDs. Hierarchy mutations should be performed via ActorHierarchyCoordinator
    /// or IGAgentActorManager.LinkParentChildAsync to keep parent/child routers in sync.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetChildrenAsync();

    /// <summary>
    /// Get the parent Agent ID.
    /// </summary>
    Task<Guid?> GetParentAsync();

    // ============ Event Publishing and Routing ============

    /// <summary>
    /// Handle received events.
    /// </summary>
    /// <param name="envelope">Event envelope.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default);

    // ============ Lifecycle ============

    /// <summary>
    /// Activate the Actor.
    /// </summary>
    Task ActivateAsync(CancellationToken ct = default);

    /// <summary>
    /// Deactivate the Actor.
    /// </summary>
    Task DeactivateAsync(CancellationToken ct = default);
}