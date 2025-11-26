using System.Linq;
using Google.Protobuf.WellKnownTypes;
using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker;

public partial class MakerTaskAgent
{
    protected virtual async Task RequestWorkerFanOutAsync(
        string taskDescription,
        TaskAgentState.Types.GenerationRequestType generationType,
        CancellationToken ct)
    {
        var fanOut = Math.Max(1, CustomConfig.InitialFanOut);
        CustomState.Phase = TaskAgentState.Types.Phase.WaitingForProposals;

        var eventType = generationType == TaskAgentState.Types.GenerationRequestType.Decomposition
            ? GenerateProposalEvent.Types.GenerationType.Decomposition
            : GenerateProposalEvent.Types.GenerationType.AtomicSolve;

        Logger.LogInformation("Task {TaskId} requesting {FanOut} proposals for {RequestId} ({Type})",
            CustomState.TaskId, CustomState.ActiveRequestId, fanOut, eventType);

        await StartConsensusAsync(generationType, ct);

        var maxTokens = CustomConfig.WorkerResponseTokenLimit > 0
            ? CustomConfig.WorkerResponseTokenLimit
            : 256;
        var stopSequences = CustomConfig.WorkerStopSequences;
        var stageHint = GetCurrentStageHint();

        for (var i = 0; i < fanOut; i++)
        {
            await PublishAsync(new GenerateProposalEvent
            {
                RequestId = CustomState.ActiveRequestId,
                TaskDescription = taskDescription,
                Type = eventType,
                MaxOutputTokens = maxTokens,
                StageHint = stageHint,
                StopSequences = { stopSequences },
                TaskId = CustomState.TaskId
            }, EventDirection.Down, ct);
        }
    }

    private bool HasConsensus(out string leaderHash, out int leaderVotes, out int runnerUpVotes)
    {
        leaderHash = string.Empty;
        leaderVotes = 0;
        runnerUpVotes = 0;
        if (CustomState.VoteClusters.Count == 0)
        {
            return false;
        }

        var ordered = CustomState.VoteClusters.Values
            .OrderByDescending(c => c.VoteCount)
            .ToList();

        var leader = ordered[0];
        runnerUpVotes = ordered.Count > 1 ? ordered[1].VoteCount : 0;
        leaderVotes = leader.VoteCount;

        if (leader.VoteCount - runnerUpVotes >= CustomConfig.ConsensusThresholdK)
        {
            leaderHash = leader.ClusterId;
            return true;
        }

        return false;
    }

    protected virtual async Task OnConsensusReachedAsync(string content, CancellationToken ct)
    {
        Logger.LogInformation("Task {TaskId} consensus reached for request {RequestId}", CustomState.TaskId,
            CustomState.ActiveRequestId);

        var completedRequestId = CustomState.ActiveRequestId;
        CustomState.ActiveRequestId = string.Empty;

        if (CustomState.ActiveGenerationType == TaskAgentState.Types.GenerationRequestType.AtomicSolve &&
            await HandleMicroRoundCompletionAsync(content, ct))
        {
            return;
        }

        if (CustomState.ActiveGenerationType ==
            TaskAgentState.Types.GenerationRequestType.Decomposition)
        {
            var steps = TryParsePlan(content);
            if (steps.Count == 0)
            {
                await RaiseRedFlagAsync("Decomposition plan empty or invalid.");
                return;
            }

            CustomState.PlannedSteps.Clear();
            foreach (var step in steps)
            {
                CustomState.PlannedSteps.Add(new TaskAgentState.Types.PlannedStep
                {
                    StepId = step.StepId,
                    Description = step.Description
                });
            }
            Logger.LogInformation("Task {TaskId} generated {Count} child steps:{Steps}",
                CustomState.TaskId,
                steps.Count,
                string.Join("; ", steps.Select(FormatPlanStepSummary)));

            CustomState.PendingChildIds.Clear();
            CustomState.ChildAgentIds.Clear();
            CustomState.ChildResults.Clear();

            await EnsureChildAgentsAsync(steps.Count, ct);

            CustomState.Phase = TaskAgentState.Types.Phase.ExecutingChildren;
            await LaunchChildAssignmentsAsync(steps, ct);
        }
        else
        {
            CustomState.FinalResult = content;
            CustomState.Phase = TaskAgentState.Types.Phase.Completed;

            if (CustomState.ChildResults.Count > 0)
            {
                var report = await SynthesizeFinalReportAsync(ct);
                if (!string.IsNullOrWhiteSpace(report))
                {
                    CustomState.FinalResult = report;
                }
            }

            await PublishOutcomeAsync(true, CustomState.FinalResult, ct);
        }
    }

    protected virtual async Task<string> SynthesizeFinalReportAsync(CancellationToken ct)
    {
        return BuildAggregateResult();
    }

    private async Task LaunchChildAssignmentsAsync(IEnumerable<PlanStep> steps, CancellationToken ct)
    {
        var nextDepth = CustomState.CurrentDepth + 1;

        foreach (var step in steps)
        {
            var childId = $"{CustomState.TaskId}:{step.StepId}";
            if (!CustomState.ChildAgentIds.Contains(childId))
            {
                CustomState.ChildAgentIds.Add(childId);
            }

            if (!CustomState.PendingChildIds.Contains(childId))
            {
                CustomState.PendingChildIds.Add(childId);
            }

            Logger.LogInformation("Task {TaskId} assigning child {ChildId}: {Description}",
                CustomState.TaskId, childId, step.Description);

            var childContext = BuildChildAssignmentContext(step);
            await PublishAsync(new AssignTaskEvent
            {
                TaskId = childId,
                GoalDescription = step.Description,
                CurrentDepth = nextDepth,
                ContextVariables = { childContext }
            }, EventDirection.Down, ct);
        }
    }

    private bool ShouldAutoLinkChildren()
    {
        return CustomConfig.AutoLinkChildren || CustomConfig.ChildPoolSize > 0;
    }

    private async Task EnsureChildAgentsAsync(int requiredSteps, CancellationToken ct)
    {
        if (!ShouldAutoLinkChildren())
        {
            return;
        }

        if (_childLinker == null)
        {
            if (!_childLinkerWarningLogged)
            {
                Logger.LogWarning("Auto child linking requested but IMakerChildLinker is not registered.");
                _childLinkerWarningLogged = true;
            }
            return;
        }

        if (EventPublisher is not IGAgentActor parentActor)
        {
            if (!_childLinkerWarningLogged)
            {
                Logger.LogWarning("Auto child linking requested but hosting actor reference is unavailable.");
                _childLinkerWarningLogged = true;
            }
            return;
        }

        var desiredCount = CustomConfig.ChildPoolSize > 0
            ? CustomConfig.ChildPoolSize
            : requiredSteps;

        desiredCount = Math.Max(1, desiredCount);

        await _childLinker.EnsureChildPoolAsync(parentActor, desiredCount, ct);
    }

    private async Task RaiseRedFlagAsync(string reason)
    {
        CustomState.RedFlagCount++;
        CustomState.FailureReason = reason;
        CustomState.Phase = TaskAgentState.Types.Phase.Failed;

        Logger.LogWarning("Task {TaskId} red-flagged: {Reason}", CustomState.TaskId, reason);

        await PublishAsync(new RedFlagRaisedEvent
        {
            TaskId = CustomState.TaskId,
            Reason = reason,
            Attempt = CustomState.ProposalAttempts
        }, EventDirection.Up);

        await PublishOutcomeAsync(false, CustomState.CandidateContent.TryGetValue(CustomState.PreferredPlanHash, out var value)
            ? value
            : string.Empty, CancellationToken.None);
    }

    private async Task PublishOutcomeAsync(bool success, string result, CancellationToken ct)
    {
        await PublishAsync(new TaskOutcomeEvent
        {
            TaskId = CustomState.TaskId,
            Success = success,
            ResultData = result,
            FailureReason = success ? string.Empty : CustomState.FailureReason
        }, EventDirection.Up, ct);
    }

    private async Task RestartDecompositionCycleAsync(CancellationToken ct)
    {
        Logger.LogInformation("Task {TaskId} restarting decomposition cycle due to stalled consensus.", CustomState.TaskId);

        CustomState.ActiveGenerationType = TaskAgentState.Types.GenerationRequestType.Decomposition;
        CustomState.ActiveRequestId = Guid.NewGuid().ToString("N");
        CustomState.Phase = TaskAgentState.Types.Phase.AssessingComplexity;
        CustomState.ProposalAttempts = 0;
        CustomState.VoteTallies.Clear();
        CustomState.VoteClusters.Clear();
        CustomState.CandidateContent.Clear();
        CustomState.PreferredPlanHash = string.Empty;
        _selfHandledRequests.Clear();

        var description = BuildTaskDescription(CustomState.OriginalGoal,
            TaskAgentState.Types.GenerationRequestType.Decomposition);
        await RequestWorkerFanOutAsync(description,
            TaskAgentState.Types.GenerationRequestType.Decomposition, ct);
    }
}

