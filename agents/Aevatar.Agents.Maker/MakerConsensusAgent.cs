using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Messages;
using Google.Protobuf;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker;

/// <summary>
/// Dedicated agent responsible for tallying worker proposals and emitting consensus results.
/// </summary>
public class MakerConsensusAgent : AIGAgentBase<TaskConsensusState, TaskAgentConfig>
{
    private readonly SemaphoreSlim _voteLock = new(1, 1);
    private bool _providerInitialized;

    [EventHandler(AllowSelfHandling = true)]
    public Task HandleStartConsensusAsync(StartConsensusEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.TaskId) || string.IsNullOrWhiteSpace(evt.RequestId))
        {
            Logger.LogWarning("StartConsensusEvent missing identifiers. task_id={TaskId}, request_id={RequestId}",
                evt.TaskId, evt.RequestId);
            return Task.CompletedTask;
        }

        Logger.LogInformation("Consensus agent starting session for task {TaskId} request {RequestId}",
            evt.TaskId, evt.RequestId);

        CustomState.TaskId = evt.TaskId;
        CustomState.RequestId = evt.RequestId;
        CustomState.GenerationType = evt.GenerationType;
        CustomState.ConsensusThresholdK = Math.Max(1, evt.ConsensusThresholdK);
        CustomState.SemanticSimilarityThreshold = evt.SemanticSimilarityThreshold <= 0
            ? 0.95f
            : evt.SemanticSimilarityThreshold;
        CustomState.MaxCandidateWait = evt.MaxCandidateWait > 0 ? evt.MaxCandidateWait : 12;
        CustomState.MaxAttempts = evt.MaxAttempts > 0 ? evt.MaxAttempts : 3;
        CustomState.ProposalAttempts = 0;
        CustomState.TotalVotes = 0;
        CustomState.LeaderHash = string.Empty;
        CustomState.LeaderVotes = 0;
        CustomState.RunnerUpVotes = 0;
        CustomState.HasReachedConsensus = false;
        CustomState.VoteTallies.Clear();
        CustomState.CandidateContent.Clear();
        CustomState.VoteClusters.Clear();

        return Task.CompletedTask;
    }

    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleProposalReceivedAsync(ProposalReceivedEvent evt)
    {
        if (string.IsNullOrWhiteSpace(CustomState.RequestId) ||
            !string.Equals(CustomState.RequestId, evt.RequestId, StringComparison.Ordinal))
        {
            return;
        }

        await _voteLock.WaitAsync();
        try
        {
            await TallyProposalAsync(evt);
        }
        finally
        {
            _voteLock.Release();
        }
    }

    private async Task TallyProposalAsync(ProposalReceivedEvent evt)
    {
        var canonical = MakerConsensusMath.Canonicalize(evt.Content);
        if (string.IsNullOrWhiteSpace(canonical))
        {
            return;
        }

        Embedding<float>? embedding = null;
        try
        {
            embedding = await GenerateEmbeddingAsync(canonical, cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "Consensus agent failed to generate embedding for request {RequestId}. Falling back to hash only.",
                evt.RequestId);
        }

        var contentHash = MakerConsensusMath.ComputeHash(canonical);

        if (!CustomState.VoteTallies.TryGetValue(contentHash, out var tally))
        {
            CustomState.VoteTallies[contentHash] = 1;
        }
        else
        {
            CustomState.VoteTallies[contentHash] = tally + 1;
        }

        if (!CustomState.CandidateContent.ContainsKey(contentHash))
        {
            CustomState.CandidateContent[contentHash] = canonical;
        }

        CustomState.TotalVotes++;

        var assignedClusterId = contentHash;
        var maxSimilarity = 0.0;

        if (embedding != null && CustomState.VoteClusters.Count > 0)
        {
            foreach (var cluster in CustomState.VoteClusters)
            {
                if (cluster.Value.RepresentativeEmbedding.Count == 0)
                {
                    continue;
                }

                var clusterEmbedding = new Embedding<float>(cluster.Value.RepresentativeEmbedding.ToArray());
                var similarity = CosineSimilarity(embedding, clusterEmbedding);
                if (similarity > maxSimilarity)
                {
                    maxSimilarity = similarity;
                    if (similarity >= CustomState.SemanticSimilarityThreshold)
                    {
                        assignedClusterId = cluster.Key;
                    }
                }
            }
        }

        if (!CustomState.VoteClusters.TryGetValue(assignedClusterId, out var targetCluster))
        {
            targetCluster = new TaskAgentState.Types.VoteCluster
            {
                ClusterId = assignedClusterId,
                RepresentativeContent = canonical,
                VoteCount = 0
            };

            if (embedding != null)
            {
                targetCluster.RepresentativeEmbedding.AddRange(embedding.Vector.ToArray());
            }

            CustomState.VoteClusters[assignedClusterId] = targetCluster;
        }

        targetCluster.VoteCount++;
        if (!targetCluster.VariantHashes.Contains(contentHash))
        {
            targetCluster.VariantHashes.Add(contentHash);
        }

        var orderedClusters = CustomState.VoteClusters.Values
            .OrderByDescending(c => c.VoteCount)
            .ToList();

        var leader = orderedClusters[0];
        var runnerUpVotes = orderedClusters.Count > 1 ? orderedClusters[1].VoteCount : 0;

        CustomState.LeaderHash = leader.ClusterId;
        CustomState.LeaderVotes = leader.VoteCount;
        CustomState.RunnerUpVotes = runnerUpVotes;

        Logger.LogInformation(
            "Consensus agent vote update: task={TaskId}, req={RequestId}, leader={Leader}:{LeaderVotes}, runner={Runner}",
            CustomState.TaskId,
            CustomState.RequestId,
            MakerConsensusMath.BuildPreview(leader.RepresentativeContent, 32),
            leader.VoteCount,
            runnerUpVotes);

        if (CustomState.HasReachedConsensus && string.Equals(CustomState.LeaderHash, leader.ClusterId, StringComparison.Ordinal))
        {
            Logger.LogDebug("Consensus already reached on {Hash}, skipping duplicate notification.", leader.ClusterId);
            return;
        }

        if (leader.VoteCount - runnerUpVotes >= CustomState.ConsensusThresholdK)
        {
            CustomState.HasReachedConsensus = true;
            await PublishConsensusResultAsync(true, leader.RepresentativeContent, leader.ClusterId, null);
            return;
        }

        if (CustomState.TotalVotes >= CustomState.MaxCandidateWait)
        {
            CustomState.ProposalAttempts++;
            var shouldAbort = CustomState.ProposalAttempts >= CustomState.MaxAttempts;
            var failureReason = shouldAbort
                ? "max_attempts_exceeded"
                : "max_candidate_wait_reached";

            await PublishConsensusResultAsync(false, leader.RepresentativeContent, leader.ClusterId, failureReason);
        }
    }

    private async Task PublishConsensusResultAsync(
        bool success,
        string? leaderContent,
        string? leaderHash,
        string? failureReason)
    {
        var result = new ConsensusResultEvent
        {
            TaskId = CustomState.TaskId,
            RequestId = CustomState.RequestId,
            Success = success,
            WinningHash = leaderHash ?? string.Empty,
            WinningContent = leaderContent ?? string.Empty,
            LeaderVotes = CustomState.LeaderVotes,
            RunnerUpVotes = CustomState.RunnerUpVotes,
            GenerationType = CustomState.GenerationType,
            FailureReason = failureReason ?? string.Empty
        };

        Logger.LogInformation(
            "Consensus agent publishing result for task {TaskId} request {RequestId}: success={Success}, leader_votes={LeaderVotes}, runner_up={RunnerUp}",
            CustomState.TaskId,
            CustomState.RequestId,
            success,
            CustomState.LeaderVotes,
            CustomState.RunnerUpVotes);

        await PublishAsync(result, EventDirection.Up);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"MakerConsensusAgent (task={CustomState.TaskId}, req={CustomState.RequestId})");
    }

    public async Task EnsureProviderInitializedAsync(string? providerName, CancellationToken ct = default)
    {
        if (_providerInitialized || string.IsNullOrWhiteSpace(providerName))
        {
            return;
        }

        await InitializeAsync(providerName, null, ct);
        _providerInitialized = true;
    }
}
