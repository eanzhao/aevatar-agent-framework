using System.Text.Json;
using Aevatar.Agents.CreativeReasoning.Messages;

namespace Aevatar.Agents.CreativeReasoning.Strategies;

/// <summary>
/// Default implementation of thought decomposition strategy.
/// Breaks solutions into typed atomic thought units.
/// </summary>
public class DefaultThoughtDecompositionStrategy : IThoughtDecompositionStrategy
{
    public string BuildDecompositionPrompt(string solution, string problemContext)
    {
        return $$"""
            # Task: Decompose Solution into Atomic Thoughts

            ## Problem Context
            {{problemContext}}

            ## Solution to Decompose
            {{solution}}

            ## Instructions
            Break this solution into atomic "thought units" - discrete concepts that can
            potentially be substituted or recombined with thoughts from other solutions.

            Each thought should be:
            - Self-contained (understandable on its own)
            - Typed by its role in the solution
            - Ordered by its position in the solution logic

            ## Thought Types
            - CORE: Core mechanism or principle
            - COMPONENT: Functional component
            - INTERACTION: How components interact
            - CONSTRAINT: Constraints or boundaries
            - OUTPUT: Expected outputs or effects

            ## Output Format (JSON array)
            ```json
            [
              {
                "id": "thought_1",
                "content": "Description of the atomic thought",
                "type": "CORE",
                "position": 1,
                "role_explanation": "Why this is essential to the solution"
              }
            ]
            ```

            Output ONLY the JSON array, no additional text.
            """;
    }

    public IReadOnlyList<ParsedThought> ParseThoughts(string llmOutput, string solutionId, string problemId)
    {
        var json = ExtractJsonArray(llmOutput);
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            var results = new List<ParsedThought>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var id = element.GetProperty("id").GetString() ?? $"{solutionId}_thought_{results.Count + 1}";
                var content = element.GetProperty("content").GetString() ?? "";
                var typeStr = element.TryGetProperty("type", out var t) ? t.GetString() : "COMPONENT";
                var position = element.TryGetProperty("position", out var p) ? p.GetInt32() : results.Count + 1;

                var type = ParseThoughtType(typeStr);
                results.Add(new ParsedThought(id, content, type, position));
            }

            return results;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ThoughtType ParseThoughtType(string? typeStr)
    {
        return typeStr?.ToUpperInvariant() switch
        {
            "CORE" => ThoughtType.Core,
            "COMPONENT" => ThoughtType.Component,
            "INTERACTION" => ThoughtType.Interaction,
            "CONSTRAINT" => ThoughtType.Constraint,
            "OUTPUT" => ThoughtType.Output,
            _ => ThoughtType.Component
        };
    }

    private static string ExtractJsonArray(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }
}

