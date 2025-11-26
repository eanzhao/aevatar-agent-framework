using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

using Microsoft.Extensions.AI;

namespace Aevatar.Agents.Maker;

/// <summary>
/// MAKER task agent orchestrates decomposition, voting, and recursion flows.
/// </summary>
public class MakerTaskAgent : AIGAgentBase<TaskAgentState, TaskAgentConfig>
{
    private const int VotePreviewLength = 180;
    private readonly ConcurrentDictionary<string, byte> _selfHandledRequests = new();
    private readonly SemaphoreSlim _selfWorkerInitLock = new(1, 1);
    private bool _selfWorkerInitialized;
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private const int MicroStageRetryLimit = 2;
    private readonly JsonDocumentOptions _jsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly SemaphoreSlim _voteLock = new(1, 1);
    private readonly IMakerChildLinker? _childLinker;
    private bool _childLinkerWarningLogged;
    private readonly Dictionary<string, string> _currentContextSnapshot = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, int> _microStageRetryCounters = new();

    public MakerTaskAgent(IMakerChildLinker? childLinker)
    {
        _childLinker = childLinker;
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        InitializeStateDefaults();
        InitializeConfigDefaults();

        // Initialize embedding generator if configured
        if (LLMProviderFactory != null)
        {
            try
            {
                // Try getting active provider config first
                var providerName = CustomConfig.ProviderName;
                if (!string.IsNullOrWhiteSpace(providerName)) 
                {
                    // Ensure we can get the config
                    var providerConfig = LLMProviderFactory.GetProviderConfig(providerName);
                    if (providerConfig?.Embeddings?.Enabled == true)
                    {
                        await InitializeEmbeddingGeneratorAsync(providerConfig, ct);
                    }
                    else 
                    {
                        // Try default provider as fallback for embeddings
                        var defaultProviderConfig = LLMProviderFactory.GetDefaultProviderConfig();
                        if (defaultProviderConfig?.Embeddings?.Enabled == true)
                        {
                             await InitializeEmbeddingGeneratorAsync(defaultProviderConfig, ct);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Failed to initialize embedding generator: {Message}", ex.Message);
            }
        }

        SystemPrompt = CustomConfig.SystemPromptTemplate;
        CustomState.LastUpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
    }

    private void InitializeStateDefaults()
    {
        if (string.IsNullOrWhiteSpace(CustomState.TaskId))
        {
            CustomState.TaskId = Id.ToString();
        }

        if (CustomState.CurrentDepth < 0)
        {
            CustomState.CurrentDepth = 0;
        }

        if (!System.Enum.IsDefined(typeof(TaskAgentState.Types.Phase), CustomState.Phase))
        {
            CustomState.Phase = TaskAgentState.Types.Phase.Created;
        }

        if (!System.Enum.IsDefined(typeof(TaskAgentState.Types.GenerationRequestType), CustomState.ActiveGenerationType))
        {
            CustomState.ActiveGenerationType =
                TaskAgentState.Types.GenerationRequestType.None;
        }
    }

    private void InitializeConfigDefaults()
    {
        if (CustomConfig.ConsensusThresholdK <= 0)
        {
            CustomConfig.ConsensusThresholdK = 2;
        }

        if (CustomConfig.MaxDepth <= 0)
        {
            CustomConfig.MaxDepth = 4;
        }

        if (CustomConfig.MaxAttempts <= 0)
        {
            CustomConfig.MaxAttempts = 3;
        }

        if (CustomConfig.InitialFanOut <= 0)
        {
            CustomConfig.InitialFanOut = 3;
        }

        if (CustomConfig.MaxCandidateWait <= 0)
        {
            CustomConfig.MaxCandidateWait = 12;
        }

        if (string.IsNullOrWhiteSpace(CustomConfig.ProviderName))
        {
            CustomConfig.ProviderName = "deepseek";
        }

        if (string.IsNullOrWhiteSpace(CustomConfig.SystemPromptTemplate))
        {
            CustomConfig.SystemPromptTemplate = """
You are a MAKER supervisor. Your job is to orchestrate recursive decomposition and ensure zero-error execution.
- Always reason about confidence.
- Prefer decomposition until tasks are clearly atomic.
- Track context variables and prepare child assignments.
""";
        }

        if (CustomConfig.WorkerResponseTokenLimit <= 0)
        {
            CustomConfig.WorkerResponseTokenLimit = 160;
        }

        if (CustomConfig.WorkerStopSequences.Count == 0)
        {
            CustomConfig.WorkerStopSequences.Add("<END>");
        }

        if (CustomConfig.ChildPoolSize <= 0)
        {
            CustomConfig.ChildPoolSize = 2;
        }
        
        if (CustomConfig.SemanticSimilarityThreshold <= 0)
        {
            CustomConfig.SemanticSimilarityThreshold = 0.95f;
        }
    }

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
        try 
        {
            if (string.IsNullOrWhiteSpace(CustomState.ActiveRequestId) ||
                !string.Equals(CustomState.ActiveRequestId, evt.RequestId, StringComparison.Ordinal))
            {
                Logger.LogDebug("Ignoring proposal for request {RequestId} (active: {Active})", evt.RequestId,
                    CustomState.ActiveRequestId);
                return;
            }

            var canonical = Canonicalize(evt.Content);
            Embedding<float>? embedding = null;

            // Generate embedding outside of lock to avoid blocking
            try
            {
                // Try to generate embedding for semantic clustering
                // We use a fire-and-forget manner or wait? Better wait to ensure consistency.
                // If generator is not available, this returns null quickly.
                embedding = await GenerateEmbeddingAsync(canonical, cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Failed to generate embedding for proposal: {Message}. Falling back to exact match.", ex.Message);
            }

            await _voteLock.WaitAsync();
            try
            {
                // Re-check state after lock
                if (!string.Equals(CustomState.ActiveRequestId, evt.RequestId, StringComparison.Ordinal))
                {
                     return;
                }
                
                var contentHash = ComputeHash(canonical);

                // 1. Update legacy/exact match tallies (for debugging or fallback)
                var tally = CustomState.VoteTallies.TryGetValue(contentHash, out var current)
                    ? current + 1
                    : 1;
                CustomState.VoteTallies[contentHash] = tally;

                if (!CustomState.CandidateContent.ContainsKey(contentHash))
                {
                    CustomState.CandidateContent[contentHash] = canonical;
                }

                // 2. Perform Semantic Clustering
                string assignedClusterId = contentHash; // Default to exact hash
                double maxSimilarity = 0.0;

                if (embedding != null && CustomState.VoteClusters.Count > 0)
                {
                    // Find best matching cluster
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

                // 3. Update Cluster State
                if (!CustomState.VoteClusters.TryGetValue(assignedClusterId, out var targetCluster))
                {
                    // New cluster created
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
                    BuildPreview(canonical));

                if (HasConsensus(out var leaderHash, out var leaderVotes, out var runnerUpVotes))
                {
                    CustomState.PreferredPlanHash = leaderHash;
                    // Use the representative content of the winning cluster
                    // Defensive check: ensure cluster exists
                    if (CustomState.VoteClusters.TryGetValue(leaderHash, out var winningCluster)) 
                    {
                        var leaderContent = winningCluster.RepresentativeContent;

                        Logger.LogInformation(
                            "Task {TaskId} consensus lead {Leader}-{Runner} after {Total} votes. Leader preview={Preview}",
                            CustomState.TaskId,
                            leaderVotes,
                            runnerUpVotes,
                            totalVotes,
                            BuildPreview(leaderContent));

                        await OnConsensusReachedAsync(leaderContent, CancellationToken.None);
                    }
                    else 
                    {
                        Logger.LogError("Task {TaskId} consensus reached on hash {Hash} but cluster is missing!", CustomState.TaskId, leaderHash);
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
                        var description = BuildTaskDescription(CustomState.OriginalGoal, CustomState.ActiveGenerationType);
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
                // Default: aggregate child results as final result
                CustomState.FinalResult = BuildAggregateResult();
                CustomState.Phase = TaskAgentState.Types.Phase.Completed;
                
                // If synthesis is possible (and enabled by override), try it
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
            Logger.LogDebug("Task {TaskId} tried to publish outcome but channel is closed (Agent deactivating).", CustomState.TaskId);
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
    public async Task HandleSelfGenerateProposalAsync(GenerateProposalEvent evt)
    {
        if (!CustomConfig.EnableSelfWorker)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(CustomState.ActiveRequestId) ||
            !string.Equals(CustomState.ActiveRequestId, evt.RequestId, StringComparison.Ordinal))
        {
            return;
        }

        if (!_selfHandledRequests.TryAdd(evt.RequestId, 0))
        {
            return;
        }

        var prompt = BuildSelfWorkerPrompt(evt);
        var response = await RunSelfWorkerAsync(prompt, evt, CancellationToken.None);
        if (string.IsNullOrWhiteSpace(response))
        {
            Logger.LogWarning("Self worker produced empty proposal for task {TaskId}", CustomState.TaskId);
            return;
        }

        await PublishAsync(new ProposalReceivedEvent
        {
            RequestId = evt.RequestId,
            Content = response,
            ReasoningTrace = $"role=self:{evt.Type};task={CustomState.TaskId}"
        }, EventDirection.Up);
    }

    private TaskAgentState.Types.GenerationRequestType DetermineGenerationType(AssignTaskEvent evt)
    {
        if (evt.ContextVariables.TryGetValue("force_atomic", out var forceAtomic) &&
            bool.TryParse(forceAtomic, out var shouldForce) &&
            shouldForce)
        {
            return TaskAgentState.Types.GenerationRequestType.AtomicSolve;
        }

        if (evt.CurrentDepth >= CustomConfig.MaxDepth)
        {
            return TaskAgentState.Types.GenerationRequestType.AtomicSolve;
        }

        return TaskAgentState.Types.GenerationRequestType.Decomposition;
    }

    private async Task InitializeMicroPlanAsync(AssignTaskEvent evt, CancellationToken ct)
    {
        CustomState.MicroObjectives.Clear();
        CustomState.MicroSummaries.Clear();
        CustomState.MicroCursor = 0;
        CustomState.MicroModeActive = CustomConfig.EnforceMicroSteps;
        CustomState.MicroCurrentObjective = string.Empty;

        if (!CustomConfig.EnforceMicroSteps)
        {
            return;
        }

        var objectives = await BuildMicroObjectivesAsync(evt, ct);
        if (objectives.Count == 0)
        {
            CustomState.MicroModeActive = false;
            return;
        }

        foreach (var objective in objectives)
        {
            var normalized = NormalizeObjective(objective);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                CustomState.MicroObjectives.Add(normalized);
            }
        }

        if (CustomState.MicroObjectives.Count == 0)
        {
            CustomState.MicroModeActive = false;
            return;
        }

        CustomState.MicroCurrentObjective = GetStageLabel(0);
    }

    protected virtual Task<IReadOnlyList<string>> BuildMicroObjectivesAsync(AssignTaskEvent evt, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    protected virtual string BuildTaskDescription(
        string originalDescription,
        TaskAgentState.Types.GenerationRequestType generationType)
    {
        if (generationType == TaskAgentState.Types.GenerationRequestType.AtomicSolve && IsMicroModeActive())
        {
            return BuildMicroTaskDescription(originalDescription);
        }

        return originalDescription;
    }

    private bool IsMicroModeActive()
    {
        return CustomConfig.EnforceMicroSteps &&
               CustomState.MicroModeActive &&
               CustomState.MicroObjectives.Count > 0 &&
               CustomState.MicroCursor < CustomState.MicroObjectives.Count;
    }

    private string BuildMicroTaskDescription(string fallbackDescription)
    {
        if (!IsMicroModeActive())
        {
            return fallbackDescription;
        }

        var index = Math.Clamp(CustomState.MicroCursor, 0, CustomState.MicroObjectives.Count - 1);
        var stageLabel = GetStageLabel(index);
        var totalStages = CustomState.MicroObjectives.Count;

        var builder = new StringBuilder();
        builder.AppendLine($"[Stage] ({index + 1}/{totalStages}) {stageLabel}");
        builder.AppendLine("Focus strictly on this stage. Use confirmed facts below to propose new reasoning or actions; if nothing new exists, explain why.");
        builder.AppendLine("When information is missing, list the gaps and specify the required inputs.");
        builder.AppendLine($"Output format: New insight (specific to \"{stageLabel}\"): ...; Evidence: ...; if no insight, describe the blocker.");
        builder.AppendLine("Differentiation rule: do not copy earlier summaries. Provide at least one new insight, parameter, or action item.");
        builder.AppendLine();
        AppendMicroBackground(builder);

        if (CustomState.MicroSummaries.Count > 0)
        {
            builder.AppendLine("Confirmed stages (last 3):");
            var startIndex = Math.Max(0, CustomState.MicroSummaries.Count - 3);
            for (var i = startIndex; i < CustomState.MicroSummaries.Count; i++)
            {
                var title = GetStageLabel(i);
                builder.AppendLine($"- ({i + 1}) {title}: {CustomState.MicroSummaries[i]}");
            }
        }
        else
        {
            builder.AppendLine("No confirmed stages yet. Cover the essential fundamentals.");
        }

        builder.AppendLine("Forbidden: verbatim repetition of confirmed content or jumping to future stages. Summarize before referencing and extend the reasoning.");
        builder.AppendLine("Output rule: keep under 4 lines, lead with the conclusion, follow with evidence, and append <END> at the end.");

        return builder.ToString();
    }

    private string GetCurrentStageHint()
    {
        return IsMicroModeActive()
            ? (CustomState.MicroCurrentObjective ?? string.Empty)
            : string.Empty;
    }

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
                StopSequences = { stopSequences }
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

        // Clear active request ID to prevent processing late/orphaned proposals
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
            
            // Try to synthesize a better report if we have aggregated child results
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
        // Default implementation just aggregates strings. 
        // Derived classes (like BaziMakerTaskAgent) should override this to call LLM.
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

    private async Task<bool> HandleMicroRoundCompletionAsync(string content, CancellationToken ct)
    {
        if (!IsMicroModeActive())
        {
            return false;
        }

        var stageIndex = CustomState.MicroCursor;
        var stageLabel = GetStageLabel(stageIndex);
        var normalizedSummary = NormalizeMicroSummary(content);

        if (IsDuplicateMicroSummary(normalizedSummary))
        {
            var retryTriggered = await RetryCurrentMicroStageAsync(stageIndex, stageLabel,
                "Detected duplicate micro summary", ct);
            if (retryTriggered)
            {
                return true;
            }

            Logger.LogWarning("Task {TaskId} stage {StageLabel} exceeded retry limit; accepting summary despite duplication.",
                CustomState.TaskId, stageLabel);
        }

        CustomState.MicroSummaries.Add(normalizedSummary);
        _microStageRetryCounters.Remove(stageIndex);
        CustomState.MicroCursor++;
        CustomState.LastUpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        if (CustomState.MicroCursor >= CustomState.MicroObjectives.Count)
        {
            CustomState.MicroModeActive = false;
            CustomState.FinalResult = BuildMicroAggregateResult();
            CustomState.Phase = TaskAgentState.Types.Phase.Completed;
            await PublishOutcomeAsync(true, CustomState.FinalResult, ct);
            return true;
        }

        CustomState.MicroCurrentObjective = GetStageLabel(CustomState.MicroCursor);
        CustomState.VoteTallies.Clear();
        CustomState.CandidateContent.Clear();
        CustomState.ProposalAttempts = 0;
        CustomState.ActiveRequestId = Guid.NewGuid().ToString("N");
        CustomState.Phase = TaskAgentState.Types.Phase.AssessingComplexity;

        var description = BuildTaskDescription(CustomState.OriginalGoal,
            TaskAgentState.Types.GenerationRequestType.AtomicSolve);
        await RequestWorkerFanOutAsync(description,
            TaskAgentState.Types.GenerationRequestType.AtomicSolve, ct);
        return true;
    }

    private string BuildMicroAggregateResult()
    {
        if (CustomState.MicroSummaries.Count == 0)
        {
            return CustomState.FinalResult ?? string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Micro-analysis summary for task {CustomState.TaskId}:");
        for (var i = 0; i < CustomState.MicroSummaries.Count; i++)
        {
            var stageTitle = GetStageLabel(i);
            builder.AppendLine($"[{i + 1}] {stageTitle}");
            builder.AppendLine(CustomState.MicroSummaries[i]);
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private void SnapshotContextVariables(IEnumerable<KeyValuePair<string, string>> contextVariables)
    {
        _currentContextSnapshot.Clear();

        if (contextVariables == null)
        {
            return;
        }

        foreach (var pair in contextVariables)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            _currentContextSnapshot[pair.Key] = pair.Value ?? string.Empty;
        }
    }

    private void AppendMicroBackground(StringBuilder builder)
    {
        var goalExcerpt = BuildOriginalGoalExcerpt();
        if (!string.IsNullOrWhiteSpace(goalExcerpt))
        {
            builder.AppendLine("[Task Background]");
            builder.AppendLine(goalExcerpt);
        }

        if (_currentContextSnapshot.Count > 0)
        {
            builder.AppendLine("[Context Variables]");
            foreach (var pair in _currentContextSnapshot)
            {
                builder.AppendLine($"- {pair.Key}: {pair.Value}");
            }
        }
    }

    private Dictionary<string, string> BuildChildAssignmentContext(PlanStep step)
    {
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["parentTaskId"] = CustomState.TaskId,
            ["stepId"] = step.StepId
        };

        foreach (var pair in _currentContextSnapshot)
        {
            context[pair.Key] = pair.Value;
        }

        var goalExcerpt = BuildOriginalGoalExcerpt();
        if (!string.IsNullOrWhiteSpace(goalExcerpt))
        {
            context["original_goal_excerpt"] = goalExcerpt;
        }

        var microDigest = BuildMicroHistoryDigest();
        if (!string.IsNullOrWhiteSpace(microDigest))
        {
            context["micro_history"] = microDigest;
        }

        var completedDigest = BuildCompletedChildDigest();
        if (!string.IsNullOrWhiteSpace(completedDigest))
        {
            context["completed_children"] = completedDigest;
        }

        return context;
    }

    private string BuildOriginalGoalExcerpt(int maxChars = 600)
    {
        if (string.IsNullOrWhiteSpace(CustomState.OriginalGoal))
        {
            return string.Empty;
        }

        var normalized = WhitespaceRegex.Replace(CustomState.OriginalGoal, " ").Trim();
        if (normalized.Length <= maxChars)
        {
            return normalized;
        }

        return normalized[..maxChars] + "...";
    }

    private string BuildMicroHistoryDigest(int maxEntries = 4)
    {
        if (CustomState.MicroSummaries.Count == 0)
        {
            return string.Empty;
        }

        var upper = CustomState.MicroSummaries.Count;
        var start = Math.Max(0, upper - maxEntries);
        var segments = new List<string>(upper - start);

        for (var i = start; i < upper; i++)
        {
            var title = GetStageLabel(i);
            segments.Add($"{title}: {CustomState.MicroSummaries[i]}");
        }

        var digest = string.Join(" | ", segments);
        if (start > 0)
        {
            digest = "... | " + digest;
        }

        return digest;
    }

    private string GetStageLabel(int objectiveIndex)
    {
        if (objectiveIndex < 0)
        {
            return $"Stage {objectiveIndex + 1}";
        }

        if (objectiveIndex >= CustomState.MicroObjectives.Count)
        {
            return $"Stage {objectiveIndex + 1}";
        }

        var raw = CustomState.MicroObjectives[objectiveIndex];
        var normalized = NormalizeObjective(raw);

        return string.IsNullOrWhiteSpace(normalized)
            ? $"Stage {objectiveIndex + 1}"
            : normalized;
    }

    private static string NormalizeObjective(string objective)
    {
        if (string.IsNullOrWhiteSpace(objective))
        {
            return string.Empty;
        }

        var trimmed = objective.Trim();

        if (trimmed.EndsWith("<END>", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^5];
        }

        trimmed = trimmed.Trim();

        while (trimmed.Length > 0 && IsBulletCharacter(trimmed[0]))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        return trimmed;
    }

    private string BuildCompletedChildDigest(int maxEntries = 4)
    {
        if (CustomState.ChildResults.Count == 0)
        {
            return string.Empty;
        }

        var entries = CustomState.ChildResults
            .OrderBy(pair => pair.Key)
            .Take(maxEntries)
            .Select(pair => $"{pair.Key}: {BuildPreview(pair.Value)}");

        var digest = string.Join(" | ", entries);
        if (CustomState.ChildResults.Count > maxEntries)
        {
            digest += " | ...";
        }

        return digest;
    }

    private string NormalizeMicroSummary(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var normalized = content.Trim();
        if (normalized.EndsWith("<END>", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^5];
        }

        normalized = normalized.ReplaceLineEndings(" ").Trim();
        normalized = WhitespaceRegex.Replace(normalized, " ").Trim();
        return normalized;
    }

    private static bool IsBulletCharacter(char value)
    {
        return value == '-'
               || value == '*'
               || value == '\u2022'
               || value == '\u00B7';
    }

    private bool IsDuplicateMicroSummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return false;
        }

        var candidate = CanonicalizeMicroSummary(summary);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        foreach (var existing in CustomState.MicroSummaries)
        {
            var canonical = CanonicalizeMicroSummary(existing);
            if (!string.IsNullOrWhiteSpace(canonical) &&
                string.Equals(candidate, canonical, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string CanonicalizeMicroSummary(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return string.Empty;
        }

        var normalized = summary.ReplaceLineEndings(" ").Trim();
        normalized = Regex.Replace(normalized, @"\s+", " ");
        return normalized;
    }

    private async Task<bool> RetryCurrentMicroStageAsync(
        int stageIndex,
        string stageLabel,
        string reason,
        CancellationToken ct)
    {
        var attempts = _microStageRetryCounters.TryGetValue(stageIndex, out var current)
            ? current + 1
            : 1;
        _microStageRetryCounters[stageIndex] = attempts;

        if (attempts > MicroStageRetryLimit)
        {
            return false;
        }

        Logger.LogWarning(
            "Task {TaskId} micro stage {StageLabel} retry {Attempt}/{Limit}: {Reason}",
            CustomState.TaskId,
            stageLabel,
            attempts,
            MicroStageRetryLimit,
            reason);

        CustomState.VoteTallies.Clear();
        CustomState.CandidateContent.Clear();
        CustomState.ProposalAttempts = 0;
        CustomState.ActiveRequestId = Guid.NewGuid().ToString("N");
        CustomState.Phase = TaskAgentState.Types.Phase.AssessingComplexity;

        var description = BuildTaskDescription(CustomState.OriginalGoal,
            TaskAgentState.Types.GenerationRequestType.AtomicSolve);

        await RequestWorkerFanOutAsync(description,
            TaskAgentState.Types.GenerationRequestType.AtomicSolve, ct);

        return true;
    }

    private static string BuildGoalWithInheritedContext(AssignTaskEvent evt)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(evt.GoalDescription))
        {
            builder.AppendLine(evt.GoalDescription.Trim());
        }

        if (evt.ContextVariables.TryGetValue("original_goal_excerpt", out var excerpt) &&
            !string.IsNullOrWhiteSpace(excerpt))
        {
            builder.AppendLine();
            builder.AppendLine("[Inherited Goal]");
            builder.AppendLine(excerpt);
        }

        if (evt.ContextVariables.TryGetValue("micro_history", out var microHistory) &&
            !string.IsNullOrWhiteSpace(microHistory))
        {
            builder.AppendLine();
            builder.AppendLine("[Upstream Micro Summary]");
            builder.AppendLine(microHistory);
        }

        if (evt.ContextVariables.TryGetValue("completed_children", out var completedChildren) &&
            !string.IsNullOrWhiteSpace(completedChildren))
        {
            builder.AppendLine();
            builder.AppendLine("[Completed Sibling Insights]");
            builder.AppendLine(completedChildren);
        }

        var text = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(text)
            ? evt.GoalDescription
            : text;
    }

    private string BuildAggregateResult()
    {
        if (CustomState.ChildResults.Count == 0)
        {
            return CustomState.FinalResult ?? string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Task {CustomState.TaskId} completed. Aggregated child results:");

        foreach (var kvp in CustomState.ChildResults.OrderBy(kvp => kvp.Key))
        {
            builder.AppendLine($"- {kvp.Key}: {kvp.Value}");
        }

        return builder.ToString().Trim();
    }

    private List<PlanStep> TryParsePlan(string content)
    {
        var steps = new List<PlanStep>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return steps;
        }

        var payload = content.Trim();
        
        // Robust JSON extraction: Find the outer-most square brackets
        var start = payload.IndexOf('[');
        var end = payload.LastIndexOf(']');
        
        if (start >= 0 && end > start)
        {
            payload = payload.Substring(start, end - start + 1);
        }
        else 
        {
            // Fallback: if no brackets found, maybe it's just content? 
            // But we expect an array. If no brackets, it's likely invalid or just text.
            // We'll try to parse it as is, in case it's a valid JSON without brackets (unlikely for array)
            // or rely on the parser to throw.
        }

        try
        {
            using var document = JsonDocument.Parse(payload, _jsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return steps;
            }

            var index = 1;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                string? stepIdRaw = null;
                string? descriptionRaw = null;

                if (element.ValueKind == JsonValueKind.Object)
                {
                    stepIdRaw = element.TryGetProperty("step_id", out var stepIdProp)
                        ? stepIdProp.GetString()
                        : null;
                    descriptionRaw = element.TryGetProperty("description", out var descProp)
                        ? descProp.GetString()
                        : element.GetPropertyOrDefault("task");
                }
                else if (element.ValueKind == JsonValueKind.String)
                {
                    descriptionRaw = element.GetString();
                }

                var stepId = string.IsNullOrWhiteSpace(stepIdRaw) ? $"S{index:D2}" : stepIdRaw.Trim();
                var description = descriptionRaw?.Trim();

                if (!string.IsNullOrWhiteSpace(description))
                {
                    steps.Add(new PlanStep(stepId, description!));
                    index++;
                }
            }
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(ex, "Failed to parse decomposition plan for task {TaskId}. Payload preview: {Payload}", CustomState.TaskId, payload.Substring(0, Math.Min(payload.Length, 100)));
        }

        return steps;
    }

    private static string Canonicalize(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var trimmed = content.Trim();
        // Do NOT collapse whitespace for canonicalization anymore as it ruins formatting
        // return WhitespaceRegex.Replace(trimmed, " "); 
        return trimmed;
    }

    private static string ComputeHash(string canonicalContent)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(canonicalContent);
        var hashBytes = sha.ComputeHash(bytes);
        return Convert.ToHexString(hashBytes);
    }

    private static string BuildPreview(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "[empty]";
        }

        var normalized = content.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= VotePreviewLength
            ? normalized
            : normalized[..VotePreviewLength] + "...";
    }

    private static string GetHashPrefix(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return "----";
        }

        var length = Math.Min(8, hash.Length);
        return hash[..length];
    }

    private static string FormatPlanStepSummary(PlanStep step)
    {
        var description = step.Description.ReplaceLineEndings(" ").Trim();
        if (description.Length > 120)
        {
            description = description[..120] + "...";
        }

        return $"{step.StepId}:{description}";
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

    private string BuildSelfWorkerPrompt(GenerateProposalEvent evt)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CustomState.ActiveGenerationType ==
                           TaskAgentState.Types.GenerationRequestType.Decomposition
            ? "You are the fallback MAKER decomposition unit. Produce a candidate plan quickly when external votes are missing."
            : "You are the fallback MAKER atomic solver. Produce a minimal yet reliable answer when external votes are missing.");
        builder.AppendLine("Follow these rules:");
        builder.AppendLine("1. Keep responses concise - no extra commentary.");
        builder.AppendLine("2. For decomposition tasks, output a JSON array; for atomic tasks, use no more than three lines.");
        builder.AppendLine("3. Append the <END> marker and stop immediately.");
        builder.AppendLine();
        builder.AppendLine("[Current Task]");
        builder.AppendLine(evt.TaskDescription);
        return builder.ToString();
    }

    private async Task<string> RunSelfWorkerAsync(
        string prompt,
        GenerateProposalEvent evt,
        CancellationToken ct)
    {
        await EnsureSelfWorkerInitializedAsync(ct);

        var request = new ChatRequest
        {
            Message = prompt,
            RequestId = $"{evt.RequestId}:self",
            Temperature = 0.2,
            // Increase max tokens for self worker to allow valid json plans
            MaxTokens = evt.MaxOutputTokens > 0 ? Math.Max(evt.MaxOutputTokens, 1000) : 1000
        };

        foreach (var seq in evt.StopSequences)
        {
            request.StopSequences.Add(seq);
        }

        if (!string.IsNullOrWhiteSpace(evt.StageHint))
        {
            request.StageHint = evt.StageHint;
        }

        var builder = new StringBuilder();
        await foreach (var chunk in ChatStreamAsync(request, ct))
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                continue;
            }

            //Logger.LogInformation("SelfWorker chunk: {Chunk}", chunk.Trim());
            builder.Append(chunk);
        }

        return builder.ToString().Trim();
    }

    private async Task EnsureSelfWorkerInitializedAsync(CancellationToken ct)
    {
        if (_selfWorkerInitialized)
        {
            return;
        }

        await _selfWorkerInitLock.WaitAsync(ct);
        try
        {
            if (_selfWorkerInitialized)
            {
                return;
            }

            await InitializeAsync(CustomConfig.ProviderName, config =>
            {
                if (string.IsNullOrWhiteSpace(config.Model))
                {
                    config.Model = "deepseek-chat";
                }
                config.MaxOutputTokens = Math.Max(config.MaxOutputTokens, 800);
                config.Temperature = Math.Min(config.Temperature, 0.3f);
            }, ct);

            _selfWorkerInitialized = true;
        }
        finally
        {
            _selfWorkerInitLock.Release();
        }
    }

    public override Task<string> GetDescriptionAsync()
    {
        var phase = CustomState.Phase.ToString();
        return Task.FromResult($"MAKER Task ({phase}) - depth {CustomState.CurrentDepth}");
    }

    private record PlanStep(string StepId, string Description);
}

internal static class JsonElementExtensions
{
    public static string? GetPropertyOrDefault(this JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return element.TryGetProperty(propertyName, out var prop)
            ? prop.GetString()
            : null;
    }
}


