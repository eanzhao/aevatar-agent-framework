using System.Text.Json;
using Aevatar.Agents.CreativeReasoning.Messages;

namespace Aevatar.Agents.CreativeReasoning.Strategies;

/// <summary>
/// Default implementation of analogy retrieval strategy.
/// Uses structured JSON output for reliable parsing.
/// </summary>
public class DefaultAnalogyStrategy : IAnalogyStrategy
{
    public string BuildAnalogyRetrievalPrompt(string problem, string? domainHint, int maxCount)
    {
        var domainContext = string.IsNullOrWhiteSpace(domainHint) 
            ? "" 
            : $"\nDomain context: {domainHint}";
        
        return $$"""
            # Task: Find Analogous Problems for Creative Transfer

            ## Original Problem
            {{problem}}
            {{domainContext}}

            ## Instructions
            Identify {{maxCount}} problems from DIFFERENT domains that share structural similarities
            with the original problem. These analogies should enable creative solution transfer.

            Look for problems with similar:
            - Constraints or resource limitations
            - Coordination/synchronization needs
            - Optimization objectives
            - Information flow patterns
            - Scale/distribution challenges

            ## Output Format (JSON array)
            ```json
            [
              {
                "id": "analogy_1",
                "description": "Clear description of the analogous problem",
                "domain": "Domain name (e.g., biology, distributed_systems, logistics)",
                "similarity_rationale": "Why this is structurally similar",
                "estimated_similarity": 0.85
              }
            ]
            ```

            Output ONLY the JSON array, no additional text.
            """;
    }

    public string BuildSolutionHarvestPrompt(string analogousProblem, string domain, int solutionsCount)
    {
        return $$"""
            # Task: Generate Solutions for Analogous Problem

            ## Problem (Domain: {{domain}})
            {{analogousProblem}}

            ## Instructions
            Generate {{solutionsCount}} distinct solutions to this problem.
            Each solution should represent a different approach or mechanism.

            ## Output Format (JSON array)
            ```json
            [
              {
                "id": "solution_1",
                "content": "Detailed description of the solution approach and mechanism",
                "key_mechanism": "The core principle that makes this work",
                "estimated_effectiveness": 0.8
              }
            ]
            ```

            Output ONLY the JSON array, no additional text.
            """;
    }

    public IReadOnlyList<ParsedAnalogy> ParseAnalogies(string llmOutput)
    {
        var json = ExtractJsonArray(llmOutput);
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            var results = new List<ParsedAnalogy>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var id = element.GetProperty("id").GetString() ?? $"analogy_{results.Count + 1}";
                var description = element.GetProperty("description").GetString() ?? "";
                var domain = element.GetProperty("domain").GetString() ?? "unknown";
                var similarity = element.TryGetProperty("estimated_similarity", out var sim) 
                    ? sim.GetSingle() 
                    : 0.7f;

                results.Add(new ParsedAnalogy(id, description, domain, similarity));
            }

            return results;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public IReadOnlyList<ParsedSolution> ParseSolutions(string llmOutput, string problemId)
    {
        var json = ExtractJsonArray(llmOutput);
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            var results = new List<ParsedSolution>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var id = element.GetProperty("id").GetString() ?? $"solution_{results.Count + 1}";
                var content = element.GetProperty("content").GetString() ?? "";
                var effectiveness = element.TryGetProperty("estimated_effectiveness", out var eff) 
                    ? eff.GetSingle() 
                    : 0.7f;

                results.Add(new ParsedSolution(id, problemId, content, effectiveness));
            }

            return results;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string ExtractJsonArray(string text)
    {
        // Find JSON array in the text (handles markdown code blocks)
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        
        if (start >= 0 && end > start)
        {
            return text[start..(end + 1)];
        }
        
        return text;
    }
}

