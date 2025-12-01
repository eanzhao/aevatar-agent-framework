namespace Aevatar.Agents.CreativeReasoning.Strategies;

// ============================================================
//  Host Selection Strategy - Step 3
//  Selects the best solution to use as a "host" for thought
//  substitution, and identifies substitution sites.
// ============================================================

/// <summary>
/// Strategy for selecting a host solution and substitution sites.
/// C-UoT Step 3: Choose a Host &amp; Substitution Sites.
/// </summary>
public interface IHostSelectionStrategy
{
    /// <summary>
    /// Build prompt to select the best host solution and identify
    /// positions where thoughts can be substituted.
    /// </summary>
    string BuildHostSelectionPrompt(
        string originalProblem,
        IReadOnlyList<SolutionForSelection> solutions);
    
    /// <summary>
    /// Parse LLM output to get host selection result.
    /// </summary>
    HostSelectionResult ParseHostSelection(string llmOutput);
}

public record SolutionForSelection(
    string Id,
    string Content,
    string SourceProblem,
    string Domain,
    int ThoughtCount);

public record HostSelectionResult(
    string HostSolutionId,
    IReadOnlyList<int> SubstitutionSites,
    string Rationale);

