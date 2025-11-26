using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Hierarchy;
using Aevatar.Agents.Maker;
using Microsoft.Extensions.Logging;

namespace MakerPaperSummaryDemo;

/// <summary>
/// Custom Child Linker to ensure we create PaperSummaryTaskAgent children instead of generic MakerTaskAgent.
/// </summary>
public sealed class PaperSummaryChildLinker : IMakerChildLinker
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<PaperSummaryChildLinker> _logger;

    public PaperSummaryChildLinker(
        IGAgentActorFactory actorFactory,
        ILogger<PaperSummaryChildLinker> logger)
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
            "Provisioning {Missing} PaperSummaryTaskAgent children for parent {ParentId}",
            missing,
            parentActor.Id);

        for (var i = 0; i < missing; i++)
        {
            // Here is the key difference: we create PaperSummaryTaskAgent
            var childActor = await _actorFactory.CreateGAgentActorAsync<PaperSummaryTaskAgent>(Guid.NewGuid(), ct);
            await ActorHierarchyCoordinator.LinkAsync(parentActor, childActor, _logger, ct);
        }
    }
}

