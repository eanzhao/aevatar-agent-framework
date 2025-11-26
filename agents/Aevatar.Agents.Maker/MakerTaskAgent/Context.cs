using System.Linq;
using System.Text;

namespace Aevatar.Agents.Maker;

public partial class MakerTaskAgent
{
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
}

