using Aevatar.Agents.CreativeReasoning.Messages;

namespace Aevatar.Agents.CreativeReasoning.Strategies;

// ============================================================
//  Donor Selection Strategy - Step 4
//  Far-then-Analogical: First select semantically distant donors,
//  then find analogically related ones for creative substitution.
// ============================================================

/// <summary>
/// Strategy for selecting donor thoughts using Far-then-Analogical method.
/// C-UoT Step 4: Far-then-Analogical Donor Selection.
/// </summary>
public interface IDonorSelectionStrategy
{
    /// <summary>
    /// Build prompt to select donors for a specific substitution site.
    /// Should prioritize "far" (semantically distant) donors first,
    /// then consider analogical relevance.
    /// </summary>
    string BuildDonorSelectionPrompt(
        ThoughtUnit originalThought,
        IReadOnlyList<ThoughtUnit> candidateDonors,
        string originalProblem,
        float farDistanceThreshold);
    
    /// <summary>
    /// Parse LLM output to get selected donor and rationale.
    /// </summary>
    DonorSelectionResult ParseDonorSelection(string llmOutput);
    
    /// <summary>
    /// Filter candidate donors by semantic distance (Far-first heuristic).
    /// Returns donors sorted by distance (farthest first).
    /// </summary>
    IReadOnlyList<ThoughtUnit> FilterByDistance(
        ThoughtUnit target,
        IReadOnlyList<ThoughtUnit> candidates,
        float farThreshold,
        int maxCount);
}

public record DonorSelectionResult(
    string DonorThoughtId,
    string Rationale,
    float EstimatedNovelty);

