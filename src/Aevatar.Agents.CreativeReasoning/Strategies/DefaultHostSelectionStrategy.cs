using System.Text;
using System.Text.Json;

namespace Aevatar.Agents.CreativeReasoning.Strategies;

/// <summary>
/// Default implementation of host selection strategy.
/// Selects the most suitable base solution for thought substitution.
/// </summary>
public class DefaultHostSelectionStrategy : IHostSelectionStrategy
{
    public string BuildHostSelectionPrompt(
        string originalProblem, 
        IReadOnlyList<SolutionForSelection> solutions)
    {
        var solutionList = new StringBuilder();
        for (var i = 0; i < solutions.Count; i++)
        {
            var sol = solutions[i];
            solutionList.AppendLine($"""
                ### Solution {i + 1}: {sol.Id}
                Domain: {sol.Domain}
                Source Problem: {sol.SourceProblem}
                Thought Count: {sol.ThoughtCount}
                Content: {sol.Content}
                """);
        }

        return $$"""
            # Task: Select Host Solution and Substitution Sites

            ## Original Problem to Solve
            {{originalProblem}}

            ## Available Solutions (from analogous problems)
            {{solutionList}}

            ## Instructions
            Select the BEST solution to use as a "host" for creative modification.
            
            The ideal host should:
            - Have a structure that can accommodate substitutions
            - Be most adaptable to the original problem
            - Have clear thought boundaries (multiple thoughts)

            Also identify 1-3 "substitution sites" - positions where thoughts
            could be replaced with donor thoughts from other solutions.

            ## Output Format (JSON object)
            ```json
            {
              "host_solution_id": "solution_1",
              "substitution_sites": [1, 3],
              "rationale": "Why this solution is the best host and why these sites are good for substitution"
            }
            ```

            Output ONLY the JSON object, no additional text.
            """;
    }

    public HostSelectionResult ParseHostSelection(string llmOutput)
    {
        var json = ExtractJsonObject(llmOutput);
        
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            var hostId = root.GetProperty("host_solution_id").GetString() ?? "";
            var rationale = root.TryGetProperty("rationale", out var r) ? r.GetString() ?? "" : "";
            
            var sites = new List<int>();
            if (root.TryGetProperty("substitution_sites", out var sitesArray))
            {
                foreach (var site in sitesArray.EnumerateArray())
                {
                    sites.Add(site.GetInt32());
                }
            }

            return new HostSelectionResult(hostId, sites, rationale);
        }
        catch (JsonException)
        {
            // Fallback: return first solution with site 1
            return new HostSelectionResult("solution_1", [1], "Parsing failed, using default");
        }
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }
}

