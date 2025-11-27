namespace Aevatar.Agents.Maker;

// ============================================================
//  Default Solution Strategy - Generic Atomic Task Solving
// ============================================================

/// <summary>
/// Default solution strategy for atomic tasks.
/// Provides clear, structured prompts for reliable answers.
/// </summary>
public sealed class DefaultSolver : ISolutionStrategy
{
    /// <inheritdoc />
    public string BuildSolvePrompt(string taskDescription, IReadOnlyDictionary<string, string> context)
    {
        var contextSection = context.Count > 0
            ? $"\n[Available Information]\n{string.Join("\n", context.Select(kv => $"- {kv.Key}: {kv.Value}"))}\n"
            : "";

        return $"""
            You are an expert problem solver. Complete the following task with precision.
            
            Rules:
            1. Provide a direct, focused answer.
            2. Use the available information if relevant.
            3. If information is missing, state what's needed.
            4. Keep the response concise but complete.
            5. Do NOT repeat the task description in your answer.
            {contextSection}
            [Task]
            {taskDescription}
            
            [Your Answer]
            """;
    }

    /// <inheritdoc />
    public string ExtractSolution(string llmOutput)
    {
        if (string.IsNullOrWhiteSpace(llmOutput))
        {
            return string.Empty;
        }

        var result = llmOutput.Trim();

        // Remove common wrapper tags
        result = RemoveWrapper(result, "<Answer>", "</Answer>");
        result = RemoveWrapper(result, "<Response>", "</Response>");
        result = RemoveWrapper(result, "<Solution>", "</Solution>");

        // Remove trailing markers
        if (result.EndsWith("<END>", StringComparison.OrdinalIgnoreCase))
        {
            result = result[..^5].Trim();
        }

        return result;
    }

    private static string RemoveWrapper(string content, string startTag, string endTag)
    {
        var start = content.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        var end = content.IndexOf(endTag, StringComparison.OrdinalIgnoreCase);

        if (start >= 0 && end > start)
        {
            return content.Substring(start + startTag.Length, end - start - startTag.Length).Trim();
        }

        return content;
    }
}

