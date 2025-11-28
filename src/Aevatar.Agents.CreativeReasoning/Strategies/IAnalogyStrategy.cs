namespace Aevatar.Agents.CreativeReasoning.Strategies;

// ============================================================
//  Analogy Strategy - Step 1: Analogical Retrieval
//  Responsible for finding semantically similar problems
//  from different domains that may offer transferable solutions.
// ============================================================

/// <summary>
/// Strategy for finding analogous problems and harvesting solutions.
/// C-UoT Step 1: Analogical Retrieval &amp; Solution Harvesting.
/// </summary>
public interface IAnalogyStrategy
{
    /// <summary>
    /// Build prompt to find analogous problems from various domains.
    /// The LLM should return problems that are structurally similar
    /// but from different domains (for creative transfer).
    /// </summary>
    string BuildAnalogyRetrievalPrompt(
        string problem, 
        string? domainHint, 
        int maxCount);
    
    /// <summary>
    /// Build prompt to generate solutions for an analogous problem.
    /// </summary>
    string BuildSolutionHarvestPrompt(
        string analogousProblem,
        string domain,
        int solutionsCount);
    
    /// <summary>
    /// Parse LLM output to extract analogous problems.
    /// </summary>
    IReadOnlyList<ParsedAnalogy> ParseAnalogies(string llmOutput);
    
    /// <summary>
    /// Parse LLM output to extract solutions.
    /// </summary>
    IReadOnlyList<ParsedSolution> ParseSolutions(string llmOutput, string problemId);
}

public record ParsedAnalogy(
    string Id,
    string Description,
    string Domain,
    float EstimatedSimilarity);

public record ParsedSolution(
    string Id,
    string ProblemId,
    string Content,
    float EstimatedEffectiveness);

