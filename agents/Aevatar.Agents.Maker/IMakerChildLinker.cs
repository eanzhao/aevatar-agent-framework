using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Maker;

/// <summary>
/// Abstraction for provisioning and linking MakerTaskAgent children.
/// </summary>
public interface IMakerChildLinker
{
    /// <summary>
    /// Ensure that at least <paramref name="desiredChildCount"/> child actors are linked to the parent actor.
    /// Implementations may create additional MakerTaskAgent actors and link them via <see cref="ActorHierarchyCoordinator"/>.
    /// </summary>
    Task EnsureChildPoolAsync(
        IGAgentActor parentActor,
        int desiredChildCount,
        CancellationToken ct = default);
}

