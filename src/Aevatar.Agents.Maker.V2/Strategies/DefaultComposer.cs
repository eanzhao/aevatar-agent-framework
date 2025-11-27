using System.Text;

namespace Aevatar.Agents.Maker.V2;

// ============================================================
//  Default Composition Strategy - Generic Result Aggregation
// ============================================================

/// <summary>
/// Default composition strategy.
/// Uses LLM synthesis for complex aggregation.
/// </summary>
public sealed class DefaultComposer : ICompositionStrategy
{
    /// <summary>
    /// Maximum combined length of subtask results before forcing LLM synthesis.
    /// </summary>
    public int SimpleAggregationThreshold { get; init; } = 2000;

    /// <inheritdoc />
    public string? Compose(
        string originalTask,
        IReadOnlyDictionary<string, string> subtaskResults,
        IReadOnlyDictionary<string, string> context)
    {
        // If results are small enough, just concatenate
        var totalLength = subtaskResults.Values.Sum(v => v.Length);
        if (totalLength <= SimpleAggregationThreshold && subtaskResults.Count <= 3)
        {
            return SimpleAggregate(subtaskResults);
        }

        // Return null to trigger LLM synthesis
        return null;
    }

    /// <inheritdoc />
    public string BuildSynthesisPrompt(
        string originalTask,
        IReadOnlyDictionary<string, string> subtaskResults,
        IReadOnlyDictionary<string, string> context)
    {
        var resultsSection = new StringBuilder();
        foreach (var (stepId, result) in subtaskResults.OrderBy(kv => kv.Key))
        {
            resultsSection.AppendLine($"--- {stepId} ---");
            resultsSection.AppendLine(result);
            resultsSection.AppendLine();
        }

        return $"""
            You are synthesizing results from multiple subtasks into a coherent final output.
            
            [Original Task]
            {originalTask}
            
            [Subtask Results]
            {resultsSection}
            
            [Instructions]
            1. Combine the results into a single, coherent response.
            2. Remove redundancy and resolve any contradictions.
            3. Ensure the output directly addresses the original task.
            4. Maintain logical flow between sections.
            5. Use appropriate formatting (markdown if helpful).
            
            [Synthesized Output]
            """;
    }

    private static string SimpleAggregate(IReadOnlyDictionary<string, string> results)
    {
        var builder = new StringBuilder();
        foreach (var (stepId, result) in results.OrderBy(kv => kv.Key))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }
            builder.AppendLine($"## {stepId}");
            builder.AppendLine(result.Trim());
        }
        return builder.ToString();
    }
}

