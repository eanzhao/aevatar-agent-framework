using Aevatar.Agents.CreativeReasoning.Messages;

namespace Aevatar.Agents.CreativeReasoning.Strategies;

// ============================================================
//  Synthesis Strategy - Step 5
//  Combines host solution with donor thoughts to create
//  novel candidate solutions.
// ============================================================

/// <summary>
/// Strategy for synthesizing new solutions from host + donors.
/// C-UoT Step 5: Substitute to Synthesize New Combinations.
/// </summary>
public interface ISynthesisStrategy
{
    /// <summary>
    /// Build prompt to synthesize a new solution by substituting
    /// donor thoughts into the host solution.
    /// </summary>
    string BuildSynthesisPrompt(
        string hostSolution,
        IReadOnlyList<ThoughtSubstitution> substitutions,
        string originalProblem);
    
    /// <summary>
    /// Parse LLM output to extract the synthesized solution.
    /// </summary>
    string ParseSynthesizedSolution(string llmOutput);
}

