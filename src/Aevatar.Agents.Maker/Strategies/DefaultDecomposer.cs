using System.Text.Json;

namespace Aevatar.Agents.Maker;

// ============================================================
//  Default Decomposition Strategy - Generic Task Breakdown
// ============================================================

/// <summary>
/// Default decomposition strategy using generic prompts.
/// Works well for most analytical and planning tasks.
/// </summary>
public sealed class DefaultDecomposer : IDecompositionStrategy
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <inheritdoc />
    public string BuildDecompositionPrompt(string taskDescription, IReadOnlyDictionary<string, string> context)
    {
        return BuildDecompositionPrompt(taskDescription, context, DecompositionGranularity.Balanced);
    }
    
    /// <inheritdoc />
    public string BuildDecompositionPrompt(
        string taskDescription, 
        IReadOnlyDictionary<string, string> context,
        DecompositionGranularity granularity)
    {
        var contextSection = context.Count > 0
            ? $"\n[Context]\n{string.Join("\n", context.Select(kv => $"- {kv.Key}: {kv.Value}"))}\n"
            : "";

        var jsonFormat = """{"step_id": "S1", "description": "..."}""";
        
        // Build granularity-specific instruction
        var (stepInstruction, rules) = granularity switch
        {
            DecompositionGranularity.Binary => (
                "Split the following task into EXACTLY 2 parts (binary decomposition)",
                """
                Rules:
                1. Output EXACTLY 2 subtasks - no more, no less.
                2. Each part should handle roughly half the complexity.
                3. Parts should be logically separable.
                4. Part 1's output may be needed by Part 2.
                """
            ),
            DecompositionGranularity.Single => (
                "Identify the SINGLE NEXT STEP needed to make progress on this task",
                """
                Rules:
                1. Output EXACTLY 1 step - the immediate next action.
                2. The step must be atomic and directly actionable.
                3. Do NOT plan ahead - just the very next step.
                4. If the task is already atomic, output it as-is.
                """
            ),
            _ => ( // Balanced (default)
                "Break down the following task into 3-6 logical, sequential steps",
                """
                Rules:
                1. Each step must be specific and actionable.
                2. Steps should be ordered logically (output of step N may be input to step N+1).
                3. Steps should be roughly equal in complexity.
                4. Do NOT include meta-steps like "review" or "finalize" unless truly necessary.
                """
            )
        };
        
        return $"""
            You are a task decomposition expert. {stepInstruction}.
            
            {rules}
            
            Output format: JSON array where each item is {jsonFormat}.
            Output ONLY the JSON array, no other text.
            {contextSection}
            [Task]
            {taskDescription}
            """;
    }

    /// <inheritdoc />
    /// <remarks>
    /// This is a fallback heuristic when LLM-based atomicity assessment fails.
    /// No depth limit is applied here - budget constraints handle resource limits.
    /// </remarks>
    public bool IsAtomic(string taskDescription, int currentDepth)
    {
        // Heuristic: short descriptions are likely atomic
        if (taskDescription.Length < 100)
        {
            return true;
        }

        // Heuristic: contains "single", "one", "atomic" keywords
        var lower = taskDescription.ToLowerInvariant();
        if (lower.Contains("single") || lower.Contains("one step") || lower.Contains("atomic"))
        {
            return true;
        }
        
        // Deep recursion suggests we should treat this as atomic
        // (this is a soft heuristic, not a hard limit)
        if (currentDepth >= 10)
        {
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public IReadOnlyList<(string StepId, string Description)> ParseDecomposition(string llmOutput)
    {
        var steps = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(llmOutput))
        {
            return steps;
        }

        var payload = ExtractJsonArray(llmOutput);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return steps;
        }

        try
        {
            using var document = JsonDocument.Parse(payload, JsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return steps;
            }

            var index = 1;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                string? stepId = null;
                string? description = null;

                if (element.ValueKind == JsonValueKind.Object)
                {
                    stepId = element.TryGetProperty("step_id", out var sid) ? sid.GetString() : null;
                    description = element.TryGetProperty("description", out var desc)
                        ? desc.GetString()
                        : element.TryGetProperty("task", out var task)
                            ? task.GetString()
                            : null;
                }
                else if (element.ValueKind == JsonValueKind.String)
                {
                    description = element.GetString();
                }

                stepId = string.IsNullOrWhiteSpace(stepId) ? $"S{index:D2}" : stepId.Trim();
                description = description?.Trim();

                if (!string.IsNullOrWhiteSpace(description))
                {
                    steps.Add((stepId, description!));
                    index++;
                }
            }
        }
        catch (JsonException)
        {
            // Fallback: try line-by-line parsing
            return ParseLinesAsFallback(llmOutput);
        }

        return steps;
    }

    private static string ExtractJsonArray(string content)
    {
        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        if (start >= 0 && end > start)
        {
            return content.Substring(start, end - start + 1);
        }
        return content.Trim();
    }

    private static List<(string, string)> ParseLinesAsFallback(string content)
    {
        var steps = new List<(string, string)>();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var index = 1;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('[') || trimmed.StartsWith('{'))
            {
                continue;
            }

            // Remove bullet points
            while (trimmed.Length > 0 && (trimmed[0] == '-' || trimmed[0] == '*' || trimmed[0] == '•'))
            {
                trimmed = trimmed[1..].TrimStart();
            }

            // Remove numbered prefixes like "1." or "S1:"
            if (trimmed.Length > 2 && char.IsDigit(trimmed[0]) && trimmed[1] == '.')
            {
                trimmed = trimmed[2..].TrimStart();
            }

            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                steps.Add(($"S{index:D2}", trimmed));
                index++;
            }
        }

        return steps;
    }
}

