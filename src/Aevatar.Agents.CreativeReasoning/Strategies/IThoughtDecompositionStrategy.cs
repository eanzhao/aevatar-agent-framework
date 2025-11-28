using Aevatar.Agents.CreativeReasoning.Messages;

namespace Aevatar.Agents.CreativeReasoning.Strategies;

// ============================================================
//  Thought Decomposition Strategy - Step 2
//  Breaks down solutions into atomic thought units that can
//  be recombined in novel ways.
// ============================================================

/// <summary>
/// Strategy for decomposing solutions into thought units.
/// C-UoT Step 2: Decompose Each Solution into Thoughts.
/// </summary>
public interface IThoughtDecompositionStrategy
{
    /// <summary>
    /// Build prompt to decompose a solution into atomic thoughts.
    /// Each thought should represent a distinct concept/mechanism.
    /// </summary>
    string BuildDecompositionPrompt(string solution, string problemContext);
    
    /// <summary>
    /// Parse LLM output to extract thought units.
    /// </summary>
    IReadOnlyList<ParsedThought> ParseThoughts(string llmOutput, string solutionId, string problemId);
}

public record ParsedThought(
    string Id,
    string Content,
    ThoughtType Type,
    int Position);

