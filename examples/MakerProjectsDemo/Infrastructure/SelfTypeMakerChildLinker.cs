using System.Linq;
using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Hierarchy;
using Aevatar.Agents.Maker;
using Microsoft.Extensions.Logging;

namespace MakerProjectsDemo.Infrastructure;

public sealed class SelfTypeMakerChildLinker : IMakerChildLinker
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly IGAgentActorManager _actorManager;
    private readonly ILogger<SelfTypeMakerChildLinker> _logger;
    private readonly MethodInfo _factoryMethod;

    public SelfTypeMakerChildLinker(
        IGAgentActorFactory actorFactory,
        IGAgentActorManager actorManager,
        ILogger<SelfTypeMakerChildLinker> logger)
    {
        _actorFactory = actorFactory;
        _actorManager = actorManager;
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
        foreach (var childId in existingChildren)
        {
            var childActor = await _actorManager.GetActorAsync(childId);
            if (childActor == null)
            {
                continue;
            }

            await EnableExternalConsensusAsync(childActor, ct);
        }

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
            await EnableExternalConsensusAsync(childActor, ct);
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

    private async Task EnableExternalConsensusAsync(IGAgentActor candidate, CancellationToken ct)
    {
        if (candidate.GetAgent() is not MakerTaskAgent taskAgent)
        {
            return;
        }

        taskAgent.EnableExternalConsensus();
        await EnsureConsensusAgentAsync(candidate, ct);
    }

    private async Task EnsureConsensusAgentAsync(IGAgentActor taskActor, CancellationToken ct)
    {
        if (taskActor.GetAgent() is not MakerTaskAgent taskAgent || !taskAgent.IsExternalConsensusEnabled)
        {
            return;
        }

        var children = await taskActor.GetChildrenAsync();
        foreach (var childId in children)
        {
            var childActor = await _actorManager.GetActorAsync(childId);
            if (childActor?.GetAgent() is MakerConsensusAgent existing)
            {
                await existing.EnsureProviderInitializedAsync(taskAgent.ProviderName, ct);
                return;
            }
        }

        var consensusActor = await _actorFactory.CreateGAgentActorAsync<MakerConsensusAgent>(Guid.NewGuid(), ct);
        await ActorHierarchyCoordinator.LinkAsync(taskActor, consensusActor, _logger, ct);
        if (consensusActor.GetAgent() is MakerConsensusAgent consensusAgent)
        {
            await consensusAgent.EnsureProviderInitializedAsync(taskAgent.ProviderName, ct);
        }
        _logger.LogInformation("Linked MakerConsensusAgent {ConsensusId} to task {TaskId}",
            consensusActor.Id, taskActor.Id);
    }
}

