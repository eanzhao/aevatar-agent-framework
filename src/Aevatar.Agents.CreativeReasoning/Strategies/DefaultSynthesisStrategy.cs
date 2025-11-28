using System.Text;
using Aevatar.Agents.CreativeReasoning.Messages;

namespace Aevatar.Agents.CreativeReasoning.Strategies;

/// <summary>
/// Default implementation of solution synthesis strategy.
/// Combines host solution with donor thoughts to create novel solutions.
/// </summary>
public class DefaultSynthesisStrategy : ISynthesisStrategy
{
    public string BuildSynthesisPrompt(
        string hostSolution,
        IReadOnlyList<ThoughtSubstitution> substitutions,
        string originalProblem)
    {
        var substitutionList = new StringBuilder();
        foreach (var sub in substitutions)
        {
            substitutionList.AppendLine($"""
                ### Substitution at Position {sub.SubstitutionSite}
                **Original Thought**: {sub.Original.Content}
                **Donor Thought**: {sub.Donor.Content}
                **Donor Source**: {sub.Donor.SourceProblem}
                **Rationale**: {sub.Rationale}
                """);
        }

        return $$"""
            # Task: Synthesize Creative Solution

            ## Original Problem to Solve
            {{originalProblem}}

            ## Host Solution (Base)
            {{hostSolution}}

            ## Thought Substitutions
            {{substitutionList}}

            ## Instructions
            Create a NEW, COHERENT solution for the original problem by:
            1. Starting with the host solution structure
            2. Incorporating the donor thoughts at the specified positions
            3. Adapting the combined ideas to work for the ORIGINAL problem
            4. Ensuring the result is internally consistent and practical

            The synthesized solution should:
            - Be a complete, actionable solution
            - Maintain the beneficial aspects of both host and donors
            - Be novel (different from either source)
            - Be feasible for the original problem

            ## Output Format
            Provide ONLY the synthesized solution as a clear, detailed description.
            Do not include JSON formatting or meta-commentary.
            """;
    }

    public string ParseSynthesizedSolution(string llmOutput)
    {
        // Remove common markdown artifacts
        var result = llmOutput.Trim();
        
        // Remove leading/trailing code blocks if present
        if (result.StartsWith("```"))
        {
            var endOfFirstLine = result.IndexOf('\n');
            if (endOfFirstLine > 0)
            {
                result = result[(endOfFirstLine + 1)..];
            }
        }
        
        if (result.EndsWith("```"))
        {
            result = result[..^3].TrimEnd();
        }

        return result;
    }
}

