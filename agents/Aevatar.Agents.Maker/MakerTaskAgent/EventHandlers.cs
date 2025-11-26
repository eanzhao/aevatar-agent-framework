using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.AI;
using Google.Protobuf.WellKnownTypes;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker;

public partial class MakerTaskAgent
{
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleAssignTaskAsync(AssignTaskEvent evt)
    {
        try
        {
            Logger.LogInformation("Task {TaskId} received goal: {Goal}", evt.TaskId, evt.GoalDescription);

            if (!string.IsNullOrWhiteSpace(evt.TaskId))
            {
                CustomState.TaskId = evt.TaskId;
            }

            var goalWithContext = BuildGoalWithInheritedContext(evt);
            CustomState.OriginalGoal = goalWithContext;
            CustomState.CurrentDepth = evt.CurrentDepth;
            CustomState.ParentId = evt.ContextVariables.TryGetValue("parentTaskId", out var parentId)
                ? parentId
                : CustomState.ParentId;
            CustomState.Phase = TaskAgentState.Types.Phase.AssessingComplexity;
            CustomState.VoteTallies.Clear();
            CustomState.VoteClusters.Clear();
            CustomState.CandidateContent.Clear();
            CustomState.PlannedSteps.Clear();
            CustomState.PendingChildIds.Clear();
            CustomState.PreferredPlanHash = string.Empty;
            CustomState.ActiveRequestId = Guid.NewGuid().ToString("N");
            CustomState.ProposalAttempts = 0;
            CustomState.FinalResult = string.Empty;
            CustomState.FailureReason = string.Empty;
            CustomState.ActiveGenerationType = DetermineGenerationType(evt);
            CustomState.LastUpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
            _selfHandledRequests.Clear();
            _microStageRetryCounters.Clear();
            SnapshotContextVariables(evt.ContextVariables);
            await InitializeMicroPlanAsync(evt, CancellationToken.None);

            var taskDescription = BuildTaskDescription(evt.GoalDescription, CustomState.ActiveGenerationType);
            await RequestWorkerFanOutAsync(taskDescription, CustomState.ActiveGenerationType, CancellationToken.None);
        }
        catch (System.Threading.Channels.ChannelClosedException)
        {
            Logger.LogDebug("Task {TaskId} tried to assign task but channel is closed.", CustomState.TaskId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling assign task in HandleAssignTaskAsync");
            throw;
        }
    }

    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleProposalReceivedAsync(ProposalReceivedEvent evt)
    {
        if (CustomConfig.UseConsensusAgent)
        {
            await PublishAsync(evt, EventDirection.Down);
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(CustomState.ActiveRequestId) ||
                !string.Equals(CustomState.ActiveRequestId, evt.RequestId, StringComparison.Ordinal))
            {
                Logger.LogDebug("Ignoring proposal for request {RequestId} (active: {Active})", evt.RequestId,
                    CustomState.ActiveRequestId);
                return;
            }

            var canonical = MakerConsensusMath.Canonicalize(evt.Content);
            Embedding<float>? embedding = null;

            try
            {
                embedding = await GenerateEmbeddingAsync(canonical, cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Failed to generate embedding for proposal: {Message}. Falling back to exact match.",
                    ex.Message);
            }

            await _voteLock.WaitAsync();
            try
            {
                if (!string.Equals(CustomState.ActiveRequestId, evt.RequestId, StringComparison.Ordinal))
                {
                    return;
                }

                var contentHash = MakerConsensusMath.ComputeHash(canonical);
                var tally = CustomState.VoteTallies.TryGetValue(contentHash, out var current)
                    ? current + 1
                    : 1;
                CustomState.VoteTallies[contentHash] = tally;

                if (!CustomState.CandidateContent.ContainsKey(contentHash))
                {
                    CustomState.CandidateContent[contentHash] = canonical;
                }

                string assignedClusterId = contentHash;
                double maxSimilarity = 0.0;

                if (embedding != null && CustomState.VoteClusters.Count > 0)
                {
                    foreach (var cluster in CustomState.VoteClusters.Values)
                    {
                        if (cluster.RepresentativeEmbedding.Count == 0) continue;

                        var clusterEmbedding = new Embedding<float>(cluster.RepresentativeEmbedding.ToArray());
                        var similarity = CosineSimilarity(embedding, clusterEmbedding);

                        if (similarity > maxSimilarity)
                        {
                            maxSimilarity = similarity;
                            if (similarity >= CustomConfig.SemanticSimilarityThreshold)
                            {
                                assignedClusterId = cluster.ClusterId;
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

                CustomState.LastUpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

                var totalVotes = CustomState.VoteClusters.Values.Sum(c => c.VoteCount);

                Logger.LogInformation(
                    "Task {TaskId} vote update: req={RequestId}, hash={Hash}, cluster={ClusterId} (sim={Sim:F3}), cluster_votes={CVotes}, total={Total}. Preview={Preview}",
                    CustomState.TaskId,
                    evt.RequestId,
                    GetHashPrefix(contentHash),
                    GetHashPrefix(assignedClusterId),
                    maxSimilarity,
                    targetCluster.VoteCount,
                    totalVotes,
                    MakerConsensusMath.BuildPreview(canonical, VotePreviewLength));

                if (HasConsensus(out var leaderHash, out var leaderVotes, out var runnerUpVotes))
                {
                    CustomState.PreferredPlanHash = leaderHash;
                    if (CustomState.VoteClusters.TryGetValue(leaderHash, out var winningCluster))
                    {
                        var leaderContent = winningCluster.RepresentativeContent;

                        Logger.LogInformation(
                            "Task {TaskId} consensus lead {Leader}-{Runner} after {Total} votes. Leader preview={Preview}",
                            CustomState.TaskId,
                            leaderVotes,
                            runnerUpVotes,
                            totalVotes,
                            MakerConsensusMath.BuildPreview(leaderContent, VotePreviewLength));

                        await OnConsensusReachedAsync(leaderContent, CancellationToken.None);
                    }
                    else
                    {
                        Logger.LogError("Task {TaskId} consensus reached on hash {Hash} but cluster is missing!",
                            CustomState.TaskId, leaderHash);
                    }

                    return;
                }

                if (totalVotes >= CustomConfig.MaxCandidateWait)
                {
                    Logger.LogWarning(
                        "Consensus not reached after {Votes} votes. Top clusters: {Standings}",
                        totalVotes,
                        string.Join(", ",
                            CustomState.VoteClusters.Values
                                .OrderByDescending(v => v.VoteCount)
                                .Take(3)
                                .Select(v => $"{GetHashPrefix(v.ClusterId)}:{v.VoteCount}")));

                    CustomState.ProposalAttempts++;
                    if (CustomState.ProposalAttempts >= CustomConfig.MaxAttempts)
                    {
                        if (CustomState.ActiveGenerationType ==
                            TaskAgentState.Types.GenerationRequestType.Decomposition)
                        {
                            await RaiseRedFlagAsync("Reached maximum voting attempts without consensus.");
                        }
                        else
                        {
                            await RestartDecompositionCycleAsync(CancellationToken.None);
                        }
                    }
                    else
                    {
                        var description =
                            BuildTaskDescription(CustomState.OriginalGoal, CustomState.ActiveGenerationType);
                        await RequestWorkerFanOutAsync(description, CustomState.ActiveGenerationType,
                            CancellationToken.None);
                    }
                }
            }
            finally
            {
                _voteLock.Release();
            }
        }
        catch (System.Threading.Channels.ChannelClosedException)
        {
            Logger.LogDebug("Task {TaskId} tried to handle proposal but channel is closed.", CustomState.TaskId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling event in HandleProposalReceivedAsync");
            throw;
        }
    }

    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleConsensusResultAsync(ConsensusResultEvent evt)
    {
        if (!CustomConfig.UseConsensusAgent)
        {
            return;
        }

        if (!string.Equals(evt.TaskId, CustomState.TaskId, StringComparison.Ordinal) ||
            !string.Equals(evt.RequestId, CustomState.ActiveRequestId, StringComparison.Ordinal))
        {
            Logger.LogDebug("Ignoring consensus result for request {RequestId}", evt.RequestId);
            return;
        }

        if (!evt.Success)
        {
            await HandleConsensusFailureAsync(evt, CancellationToken.None);
            return;
        }

        CustomState.PreferredPlanHash = evt.WinningHash;
        await OnConsensusReachedAsync(evt.WinningContent, CancellationToken.None);
    }

    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleChildOutcomeAsync(TaskOutcomeEvent evt)
    {
        try
        {
            if (!CustomState.ChildAgentIds.Contains(evt.TaskId))
            {
                return;
            }

            CustomState.ChildResults[evt.TaskId] = evt.ResultData ?? string.Empty;
            CustomState.PendingChildIds.Remove(evt.TaskId);
            CustomState.LastUpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

            Logger.LogInformation("Task {TaskId} child {ChildId} completed. Remaining: {Remaining}", CustomState.TaskId,
                evt.TaskId, CustomState.PendingChildIds.Count);

            if (!evt.Success)
            {
                CustomState.Phase = TaskAgentState.Types.Phase.Failed;
                CustomState.FailureReason = evt.FailureReason ?? "Child task failed.";
                await PublishOutcomeAsync(false, evt.ResultData ?? string.Empty, CancellationToken.None);
                return;
            }

            if (CustomState.PendingChildIds.Count == 0)
            {
                CustomState.FinalResult = BuildAggregateResult();
                CustomState.Phase = TaskAgentState.Types.Phase.Completed;

                try
                {
                    var synthesized = await SynthesizeFinalReportAsync(CancellationToken.None);
                    if (!string.IsNullOrWhiteSpace(synthesized))
                    {
                        CustomState.FinalResult = synthesized;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to synthesize final report. Falling back to aggregation.");
                }

                await PublishOutcomeAsync(true, CustomState.FinalResult, CancellationToken.None);
            }
        }
        catch (System.Threading.Channels.ChannelClosedException)
        {
            Logger.LogDebug("Task {TaskId} tried to publish outcome but channel is closed (Agent deactivating).",
                CustomState.TaskId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling child outcome in HandleChildOutcomeAsync");
            throw;
        }
    }

    [EventHandler(AllowSelfHandling = true)]
    public Task HandleRedFlagRaisedAsync(RedFlagRaisedEvent evt)
    {
        Logger.LogWarning("Task {TaskId} observed upstream red flag: {Reason}", evt.TaskId, evt.Reason);
        return Task.CompletedTask;
    }

    [EventHandler(AllowSelfHandling = true)]
    public virtual Task HandleSelfGenerateProposalAsync(GenerateProposalEvent evt)
    {
        if (!CustomConfig.EnableSelfWorker)
        {
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(CustomState.ActiveRequestId) ||
            !string.Equals(CustomState.ActiveRequestId, evt.RequestId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        if (!_selfHandledRequests.TryAdd(evt.RequestId, 0))
        {
            return Task.CompletedTask;
        }

        // Run in background so we don't block the event processing loop
        _ = Task.Run(async () =>
        {
            try
            {
                var prompt = BuildSelfWorkerPrompt(evt);
                var response = await RunSelfWorkerAsync(prompt, evt, CancellationToken.None);
                if (!string.IsNullOrWhiteSpace(response))
                {
                    await PublishAsync(new ProposalReceivedEvent
                    {
                        RequestId = evt.RequestId,
                        Content = response,
                        ReasoningTrace = $"role=self:{evt.Type};task={CustomState.TaskId}"
                    }, EventDirection.Up);
                }
                else
                {
                    Logger.LogWarning("Self worker produced empty proposal for task {TaskId}", CustomState.TaskId);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in self-worker execution");
            }
        });

        return Task.CompletedTask;
    }
}

