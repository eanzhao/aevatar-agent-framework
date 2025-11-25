using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Hierarchy;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker;

/// <summary>
/// Default implementation that provisions MakerTaskAgent children using the configured runtime factory.
/// </summary>
public sealed class MakerChildLinker : IMakerChildLinker
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<MakerChildLinker> _logger;

    public MakerChildLinker(
        IGAgentActorFactory actorFactory,
        ILogger<MakerChildLinker> logger)
    {
        _actorFactory = actorFactory;
        _logger = logger;
    }

    public async Task EnsureChildPoolAsync(
        IGAgentActor parentActor,
        int desiredChildCount,
        CancellationToken ct = default)
    {
        desiredChildCount = Math.Max(1, desiredChildCount);

        var existingChildren = await parentActor.GetChildrenAsync();
        var missing = desiredChildCount - existingChildren.Count;
        if (missing <= 0)
        {
            return;
        }

        _logger.LogInformation(
            "Provisioning {Missing} MakerTaskAgent children for parent {ParentId}",
            missing,
            parentActor.Id);

        for (var i = 0; i < missing; i++)
        {
            var childActor = await _actorFactory.CreateGAgentActorAsync<MakerTaskAgent>(Guid.NewGuid(), ct);
            await ActorHierarchyCoordinator.LinkAsync(parentActor, childActor, _logger, ct);
        }
    }
}

