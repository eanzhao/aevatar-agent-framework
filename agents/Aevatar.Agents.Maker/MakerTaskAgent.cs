using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

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
    private readonly JsonDocumentOptions _jsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly SemaphoreSlim _voteLock = new(1, 1);

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        InitializeStateDefaults();
        InitializeConfigDefaults();

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
    }

    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleAssignTaskAsync(AssignTaskEvent evt)
    {
        Logger.LogInformation("Task {TaskId} received goal: {Goal}", evt.TaskId, evt.GoalDescription);

        if (!string.IsNullOrWhiteSpace(evt.TaskId))
        {
            CustomState.TaskId = evt.TaskId;
        }

        CustomState.OriginalGoal = evt.GoalDescription;
        CustomState.CurrentDepth = evt.CurrentDepth;
        CustomState.ParentId = evt.ContextVariables.TryGetValue("parentTaskId", out var parentId)
            ? parentId
            : CustomState.ParentId;
        CustomState.Phase = TaskAgentState.Types.Phase.AssessingComplexity;
        CustomState.VoteTallies.Clear();
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
        await InitializeMicroPlanAsync(evt, CancellationToken.None);

        var taskDescription = BuildTaskDescription(evt.GoalDescription, CustomState.ActiveGenerationType);
        await RequestWorkerFanOutAsync(taskDescription, CustomState.ActiveGenerationType, CancellationToken.None);
    }

    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleProposalReceivedAsync(ProposalReceivedEvent evt)
    {
        if (string.IsNullOrWhiteSpace(CustomState.ActiveRequestId) ||
            !string.Equals(CustomState.ActiveRequestId, evt.RequestId, StringComparison.Ordinal))
        {
            Logger.LogDebug("Ignoring proposal for request {RequestId} (active: {Active})", evt.RequestId,
                CustomState.ActiveRequestId);
            return;
        }

        await _voteLock.WaitAsync();
        try
        {
            var canonical = Canonicalize(evt.Content);
            var contentHash = ComputeHash(canonical);

            var tally = CustomState.VoteTallies.TryGetValue(contentHash, out var current)
                ? current + 1
                : 1;
            CustomState.VoteTallies[contentHash] = tally;

            if (!CustomState.CandidateContent.ContainsKey(contentHash))
            {
                CustomState.CandidateContent[contentHash] = canonical;
            }

            CustomState.LastUpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

            var totalVotes = CustomState.VoteTallies.Values.Sum();

            Logger.LogInformation(
                "Task {TaskId} vote update (req {RequestId}, type {Type}): candidate {Candidate} -> {Votes} votes / total {Total}. Preview={Preview}",
                CustomState.TaskId,
                evt.RequestId,
                CustomState.ActiveGenerationType,
                GetHashPrefix(contentHash),
                tally,
                totalVotes,
                BuildPreview(canonical));

            if (HasConsensus(out var leaderHash, out var leaderVotes, out var runnerUpVotes))
            {
                CustomState.PreferredPlanHash = leaderHash;
                var leaderContent = CustomState.CandidateContent[leaderHash];

                Logger.LogInformation(
                    "Task {TaskId} consensus lead {Leader}-{Runner} after {Total} votes. Leader preview={Preview}",
                    CustomState.TaskId,
                    leaderVotes,
                    runnerUpVotes,
                    totalVotes,
                    BuildPreview(leaderContent));

                await OnConsensusReachedAsync(leaderContent, CancellationToken.None);
                return;
            }

            if (totalVotes >= CustomConfig.MaxCandidateWait)
            {
                Logger.LogWarning(
                    "Consensus not reached after {Votes} votes (Attempt {Attempt}/{Max}). Standings: {Standings}",
                    totalVotes,
                    CustomState.ProposalAttempts + 1,
                    CustomConfig.MaxAttempts,
                    string.Join(", ",
                        CustomState.VoteTallies
                            .OrderByDescending(kv => kv.Value)
                            .Take(4)
                            .Select(kv => $"{GetHashPrefix(kv.Key)}:{kv.Value}")));

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

    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleChildOutcomeAsync(TaskOutcomeEvent evt)
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
            await PublishOutcomeAsync(true, CustomState.FinalResult, CancellationToken.None);
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
            if (!string.IsNullOrWhiteSpace(objective))
            {
                CustomState.MicroObjectives.Add(objective);
            }
        }

        if (CustomState.MicroObjectives.Count == 0)
        {
            CustomState.MicroModeActive = false;
            return;
        }

        CustomState.MicroCurrentObjective = CustomState.MicroObjectives[0];
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
        var stage = CustomState.MicroObjectives[index];

        var builder = new StringBuilder();
        builder.AppendLine(stage);

        if (CustomState.MicroSummaries.Count > 0)
        {
            builder.AppendLine("【已确认信息】");
            for (var i = 0; i < CustomState.MicroSummaries.Count; i++)
            {
                builder.AppendLine($"- ({i + 1}) {CustomState.MicroSummaries[i]}");
            }
        }

        builder.AppendLine("【要求】请只回答当前阶段的问题，禁止引用未来阶段内容。");
        builder.AppendLine("【输出规则】保持不超过 80 个汉字或 3 行，末尾追加标记 <END> 并立即停止。");

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
        if (CustomState.VoteTallies.Count == 0)
        {
            return false;
        }

        var ordered = CustomState.VoteTallies
            .OrderByDescending(kv => kv.Value)
            .ToList();

        var leader = ordered[0];
        runnerUpVotes = ordered.Count > 1 ? ordered[1].Value : 0;
        leaderVotes = leader.Value;

        if (leader.Value - runnerUpVotes >= CustomConfig.ConsensusThresholdK)
        {
            leaderHash = leader.Key;
            return true;
        }

        return false;
    }

    protected virtual async Task OnConsensusReachedAsync(string content, CancellationToken ct)
    {
        Logger.LogInformation("Task {TaskId} consensus reached for request {RequestId}", CustomState.TaskId,
            CustomState.ActiveRequestId);

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

            CustomState.Phase = TaskAgentState.Types.Phase.ExecutingChildren;
            await LaunchChildAssignmentsAsync(steps, ct);
        }
        else
        {
            CustomState.FinalResult = content;
            CustomState.Phase = TaskAgentState.Types.Phase.Completed;
            await PublishOutcomeAsync(true, content, ct);
        }
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

            await PublishAsync(new AssignTaskEvent
            {
                TaskId = childId,
                GoalDescription = step.Description,
                CurrentDepth = nextDepth,
                ContextVariables =
                {
                    { "parentTaskId", CustomState.TaskId },
                    { "stepId", step.StepId }
                }
            }, EventDirection.Down, ct);
        }
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

        CustomState.MicroSummaries.Add(content);
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

        CustomState.MicroCurrentObjective = CustomState.MicroObjectives[CustomState.MicroCursor];
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
            var stageTitle = i < CustomState.MicroObjectives.Count
                ? CustomState.MicroObjectives[i]
                : $"阶段 {i + 1}";
            builder.AppendLine($"[{i + 1}] {stageTitle}");
            builder.AppendLine(CustomState.MicroSummaries[i]);
            builder.AppendLine();
        }

        return builder.ToString().Trim();
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

        try
        {
            using var document = JsonDocument.Parse(content, _jsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return steps;
            }

            var index = 1;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var stepIdRaw = element.TryGetProperty("step_id", out var stepIdProp)
                    ? stepIdProp.GetString()
                    : null;
                var descriptionRaw = element.TryGetProperty("description", out var descProp)
                    ? descProp.GetString()
                    : element.GetPropertyOrDefault("task") ??
                      (element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString());

                var stepId = string.IsNullOrWhiteSpace(stepIdRaw) ? $"S{index:D2}" : stepIdRaw.Trim();
                var description = descriptionRaw?.Trim();

                if (!string.IsNullOrWhiteSpace(stepId) && !string.IsNullOrWhiteSpace(description))
                {
                    steps.Add(new PlanStep(stepId, description!));
                    index++;
                }
            }
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(ex, "Failed to parse decomposition plan for task {TaskId}", CustomState.TaskId);
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
        return WhitespaceRegex.Replace(trimmed, " ");
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
            ? "你是备用的 MAKER 自身分解单元，在缺少外部投票时，需要迅速给出一个候选方案。"
            : "你是备用的 MAKER 自身求解单元，需要给出一个极简且可靠的候选方案。");
        builder.AppendLine("遵守以下规则：");
        builder.AppendLine("1. 回答必须简洁，避免多余解释。");
        builder.AppendLine("2. 如果是分解任务，请输出 JSON 数组；若是原子任务，请输出 3 行以内的内容。");
        builder.AppendLine("3. 最后追加标记 <END> 并立即停止。");
        builder.AppendLine();
        builder.AppendLine("【当前任务】");
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
            MaxTokens = evt.MaxOutputTokens > 0 ? evt.MaxOutputTokens : 200
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

            Logger.LogInformation("✨ SelfWorker chunk: {Chunk}", chunk.Trim());
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


