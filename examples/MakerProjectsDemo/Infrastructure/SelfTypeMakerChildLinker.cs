using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Hierarchy;
using Aevatar.Agents.Maker;
using Microsoft.Extensions.Logging;

namespace MakerProjectsDemo.Infrastructure;

public sealed class SelfTypeMakerChildLinker : IMakerChildLinker
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<SelfTypeMakerChildLinker> _logger;
    private readonly MethodInfo _factoryMethod;

    public SelfTypeMakerChildLinker(
        IGAgentActorFactory actorFactory,
        ILogger<SelfTypeMakerChildLinker> logger)
    {
        _actorFactory = actorFactory;
        _logger = logger;
        _factoryMethod = typeof(IGAgentActorFactory)
            .GetMethod(nameof(IGAgentActorFactory.CreateGAgentActorAsync)) ??
            throw new InvalidOperationException("Factory method not found.");
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

        var parentAgent = parentActor.GetAgent();
        var parentType = parentAgent.GetType();
        if (parentAgent is not MakerTaskAgent)
        {
            parentType = typeof(MakerTaskAgent);
        }

        _logger.LogInformation(
            "Provisioning {Missing} child agents of type {AgentType} for parent {ParentId}",
            missing,
            parentType.Name,
            parentActor.Id);

        for (var i = 0; i < missing; i++)
        {
            var childActor = await CreateActorAsync(parentType, ct);
            await ActorHierarchyCoordinator.LinkAsync(parentActor, childActor, _logger, ct);
        }
    }

    private Task<IGAgentActor> CreateActorAsync(Type agentType, CancellationToken ct)
    {
        var method = _factoryMethod.MakeGenericMethod(agentType);
        var task = (Task<IGAgentActor>) (method.Invoke(_actorFactory, new object?[] { Guid.NewGuid(), ct })
                                         ?? throw new InvalidOperationException("Failed to create actor."));
        return task;
    }
}

