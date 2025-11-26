using System.Linq;
using System.Text;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker;

public partial class MakerTaskAgent
{
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
        _microSummaryFingerprints.Clear();
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
        TrackMicroSummarySignature(normalizedSummary);
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

        HydrateMicroSummaryFingerprints();
        return _microSummaryFingerprints.Contains(candidate);
    }

    private static string CanonicalizeMicroSummary(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return string.Empty;
        }

        var normalized = summary.ReplaceLineEndings(" ").Trim();
        normalized = WhitespaceRegex.Replace(normalized, " ").Trim();
        return normalized;
    }

    private void HydrateMicroSummaryFingerprints()
    {
        if (_microSummaryFingerprints.Count > 0 || CustomState.MicroSummaries.Count == 0)
        {
            return;
        }

        foreach (var existing in CustomState.MicroSummaries)
        {
            TrackMicroSummarySignature(existing);
        }
    }

    private void TrackMicroSummarySignature(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return;
        }

        var canonical = CanonicalizeMicroSummary(summary);
        if (!string.IsNullOrWhiteSpace(canonical))
        {
            _microSummaryFingerprints.Add(canonical);
        }
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
}

