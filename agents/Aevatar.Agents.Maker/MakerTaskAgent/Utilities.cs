using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker;

public partial class MakerTaskAgent
{
    private List<PlanStep> TryParsePlan(string content)
    {
        var steps = new List<PlanStep>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return steps;
        }

        var payload = content.Trim();

        var start = payload.IndexOf('[');
        var end = payload.LastIndexOf(']');

        if (start >= 0 && end > start)
        {
            payload = payload.Substring(start, end - start + 1);
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

                var stepId = string.IsNullOrWhiteSpace(stepIdRaw) ? $"S{index:D2}" : stepIdRaw!.Trim();
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

    private record PlanStep(string StepId, string Description);
}

