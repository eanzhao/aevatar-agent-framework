using Aevatar.Agents.Maker.Messages;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Maker.Agents;

// ============================================================
//  MAKER Progress Reporter
//  Handles progress reporting and red flag management
//  Part of MakerCoordinatorGAgent (partial class)
// ============================================================

public partial class MakerCoordinatorGAgent
{
    // ============================================================
    //  Progress Reporting
    // ============================================================

    /// <summary>
    /// Report progress to callback and publish event.
    /// </summary>
    private void ReportProgress(MakerProgress progress)
    {
        _progressCallback?.Invoke(progress);

        _ = PublishAsync(new MakerProgressEvent
        {
            ExecutionId = CustomState.ExecutionId,
            TaskId = progress.TaskId,
            Phase = progress.Phase.ToString().ToLowerInvariant(),
            Message = progress.Message,
            Depth = progress.Depth,
            Timestamp = Timestamp.FromDateTimeOffset(progress.Timestamp),
            VotingRound = progress.Voting?.Round ?? 0,
            VotingTotal = progress.Voting?.TotalVotes ?? 0,
            VotingLeader = progress.Voting?.LeaderVotes ?? 0,
            VotingRunnerUp = progress.Voting?.RunnerUpVotes ?? 0,
            VotingNeeded = progress.Voting?.VotesNeeded ?? 0,
            ProposalId = progress.Proposal?.ProposalId ?? string.Empty,
            ProposalContent = progress.Proposal?.Content ?? string.Empty,
            ProposalSuccess = progress.Proposal?.Success ?? false
        });
    }

    // ============================================================
    //  Red Flag Management
    // ============================================================

    /// <summary>
    /// Add a red flag event for tracking issues during execution.
    /// </summary>
    private void AddRedFlag(string taskId, string reason)
    {
        _redFlags.Add(new RedFlagEvent
        {
            TaskId = taskId,
            Reason = reason,
            Recovered = false
        });
        CustomState.RedFlagReasons.Add($"{taskId}: {reason}");
    }
}

