using System;
using System.Threading;
using System.Threading.Tasks;

namespace Aevatar.Agents.Maker;

public partial class MakerTaskAgent
{
    private async Task StartConsensusAsync(
        TaskAgentState.Types.GenerationRequestType generationType,
        CancellationToken ct)
    {
        if (!CustomConfig.UseConsensusAgent ||
            string.IsNullOrWhiteSpace(CustomState.ActiveRequestId))
        {
            return;
        }

        var startEvent = new StartConsensusEvent
        {
            TaskId = CustomState.TaskId,
            RequestId = CustomState.ActiveRequestId,
            GenerationType = generationType,
            ConsensusThresholdK = CustomConfig.ConsensusThresholdK > 0
                ? CustomConfig.ConsensusThresholdK
                : 2,
            SemanticSimilarityThreshold = CustomConfig.SemanticSimilarityThreshold > 0
                ? CustomConfig.SemanticSimilarityThreshold
                : 0.95f,
            MaxCandidateWait = CustomConfig.MaxCandidateWait > 0
                ? CustomConfig.MaxCandidateWait
                : 12,
            MaxAttempts = CustomConfig.MaxAttempts > 0
                ? CustomConfig.MaxAttempts
                : 3
        };

        await PublishAsync(startEvent, EventDirection.Down, ct);
    }

    private async Task HandleConsensusFailureAsync(ConsensusResultEvent evt, CancellationToken ct)
    {
        var attemptsExceeded = string.Equals(
            evt.FailureReason,
            "max_attempts_exceeded",
            StringComparison.OrdinalIgnoreCase);

        if (!attemptsExceeded)
        {
            CustomState.ProposalAttempts++;
            attemptsExceeded = CustomState.ProposalAttempts >= CustomConfig.MaxAttempts;
        }
        else
        {
            CustomState.ProposalAttempts = CustomConfig.MaxAttempts;
        }

        if (attemptsExceeded)
        {
            if (CustomState.ActiveGenerationType ==
                TaskAgentState.Types.GenerationRequestType.Decomposition)
            {
                await RaiseRedFlagAsync("Reached maximum voting attempts without consensus.");
            }
            else
            {
                await RestartDecompositionCycleAsync(ct);
            }
            return;
        }

        var description = BuildTaskDescription(CustomState.OriginalGoal, CustomState.ActiveGenerationType);
        await RequestWorkerFanOutAsync(description, CustomState.ActiveGenerationType, ct);
    }
}

