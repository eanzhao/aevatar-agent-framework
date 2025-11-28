using System.Text;
using System.Text.Json;

namespace Aevatar.Agents.CreativeReasoning.Strategies;

/// <summary>
/// Default implementation of three-dimensional evaluation strategy.
/// Evaluates Feasibility (hard constraint), Utility, and Novelty.
/// </summary>
public class DefaultEvaluationStrategy : IEvaluationStrategy
{
    public string BuildFeasibilityPrompt(
        string candidateSolution,
        string originalProblem,
        IReadOnlyList<string>? constraints = null)
    {
        var constraintSection = constraints?.Count > 0
            ? $"\n## Constraints\n{string.Join("\n- ", constraints)}"
            : "";

        return $$"""
            # Task: Evaluate Solution Feasibility

            ## Problem
            {{originalProblem}}
            {{constraintSection}}

            ## Candidate Solution
            {{candidateSolution}}

            ## Instructions
            Evaluate whether this solution is FEASIBLE - can it actually be implemented
            and work in practice for the given problem?

            Consider:
            - Technical feasibility (is this physically/technically possible?)
            - Resource feasibility (reasonable resources required?)
            - Constraint satisfaction (does it meet the problem's constraints?)
            - Implementation clarity (clear enough to be actionable?)

            ## Output Format (JSON object)
            ```json
            {
              "feasibility_score": 0.75,
              "passes_threshold": true,
              "rationale": "Explanation of the feasibility assessment",
              "concerns": ["Any specific concerns or risks"]
            }
            ```

            Output ONLY the JSON object, no additional text.
            """;
    }

    public string BuildUtilityPrompt(string candidateSolution, string originalProblem)
    {
        return $$"""
            # Task: Evaluate Solution Utility

            ## Problem
            {{originalProblem}}

            ## Candidate Solution
            {{candidateSolution}}

            ## Instructions
            Evaluate the UTILITY of this solution - how useful and effective is it
            at solving the problem?

            Consider:
            - Effectiveness (how well does it solve the core problem?)
            - Efficiency (does it minimize resources/effort?)
            - Completeness (does it address all aspects of the problem?)
            - Side effects (any negative consequences?)

            ## Output Format (JSON object)
            ```json
            {
              "utility_score": 0.8,
              "rationale": "Explanation of utility assessment"
            }
            ```

            Output ONLY the JSON object, no additional text.
            """;
    }

    public string BuildNoveltyPrompt(
        string candidateSolution,
        string originalProblem,
        IReadOnlyList<string> existingSolutions)
    {
        var existingList = new StringBuilder();
        for (var i = 0; i < existingSolutions.Count; i++)
        {
            existingList.AppendLine($"### Existing Solution {i + 1}\n{existingSolutions[i]}\n");
        }

        return $$"""
            # Task: Evaluate Solution Novelty

            ## Problem
            {{originalProblem}}

            ## Candidate Solution
            {{candidateSolution}}

            ## Existing Solutions (for comparison)
            {{existingList}}

            ## Instructions
            Evaluate the NOVELTY of this solution - how different and creative is it
            compared to existing approaches?

            Consider:
            - Conceptual novelty (new principles or mechanisms?)
            - Structural novelty (different organization/approach?)
            - Cross-domain transfer (ideas from unexpected domains?)
            - Unexpected combinations (novel synthesis of concepts?)

            High novelty = fundamentally different approach
            Low novelty = incremental variation of existing solutions

            ## Output Format (JSON object)
            ```json
            {
              "novelty_score": 0.7,
              "novel_aspects": ["What makes this solution different"],
              "rationale": "Explanation of novelty assessment"
            }
            ```

            Output ONLY the JSON object, no additional text.
            """;
    }

    public FeasibilityResult ParseFeasibility(string llmOutput)
    {
        var json = ExtractJsonObject(llmOutput);
        
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            var score = root.GetProperty("feasibility_score").GetSingle();
            var passes = root.TryGetProperty("passes_threshold", out var p) && p.GetBoolean();
            var rationale = root.TryGetProperty("rationale", out var r) ? r.GetString() ?? "" : "";

            return new FeasibilityResult(score, passes, rationale);
        }
        catch (JsonException)
        {
            return new FeasibilityResult(0.5f, false, "Parsing failed");
        }
    }

    public float ParseUtility(string llmOutput)
    {
        var json = ExtractJsonObject(llmOutput);
        
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("utility_score").GetSingle();
        }
        catch (JsonException)
        {
            return 0.5f;
        }
    }

    public float ParseNovelty(string llmOutput)
    {
        var json = ExtractJsonObject(llmOutput);
        
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("novelty_score").GetSingle();
        }
        catch (JsonException)
        {
            return 0.5f;
        }
    }

    public float ComputeCompositeScore(float utility, float novelty, float utilityWeight, float noveltyWeight)
    {
        // Normalize weights
        var totalWeight = utilityWeight + noveltyWeight;
        if (totalWeight == 0) totalWeight = 1;

        var normalizedUtilityWeight = utilityWeight / totalWeight;
        var normalizedNoveltyWeight = noveltyWeight / totalWeight;

        return (utility * normalizedUtilityWeight) + (novelty * normalizedNoveltyWeight);
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }
}

