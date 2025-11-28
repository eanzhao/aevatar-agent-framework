using Aevatar.Agents.CreativeReasoning.Messages;

namespace Aevatar.Agents.CreativeReasoning.Strategies;

// ============================================================
//  Evaluation Strategy - Step 6
//  Three-dimensional evaluation: Feasibility, Utility, Novelty
//  Feasibility is a hard constraint; Utility and Novelty are metrics.
// ============================================================

/// <summary>
/// Strategy for evaluating candidate solutions on three dimensions.
/// C-UoT Step 6: Evaluate and Rank by Feasibility, Usefulness, Novelty.
/// </summary>
public interface IEvaluationStrategy
{
    /// <summary>
    /// Build prompt to evaluate a candidate solution's feasibility.
    /// This is a HARD CONSTRAINT - solutions below threshold are rejected.
    /// </summary>
    string BuildFeasibilityPrompt(
        string candidateSolution,
        string originalProblem,
        IReadOnlyList<string>? constraints = null);
    
    /// <summary>
    /// Build prompt to evaluate utility (usefulness/effectiveness).
    /// </summary>
    string BuildUtilityPrompt(
        string candidateSolution,
        string originalProblem);
    
    /// <summary>
    /// Build prompt to evaluate novelty (difference from existing solutions).
    /// </summary>
    string BuildNoveltyPrompt(
        string candidateSolution,
        string originalProblem,
        IReadOnlyList<string> existingSolutions);
    
    /// <summary>
    /// Parse feasibility evaluation result.
    /// </summary>
    FeasibilityResult ParseFeasibility(string llmOutput);
    
    /// <summary>
    /// Parse utility evaluation result.
    /// </summary>
    float ParseUtility(string llmOutput);
    
    /// <summary>
    /// Parse novelty evaluation result.
    /// </summary>
    float ParseNovelty(string llmOutput);
    
    /// <summary>
    /// Compute composite score from utility and novelty.
    /// Feasibility is handled as a gate, not weighted.
    /// </summary>
    float ComputeCompositeScore(float utility, float novelty, float utilityWeight, float noveltyWeight);
}

public record FeasibilityResult(
    float Score,
    bool PassesThreshold,
    string Rationale);

