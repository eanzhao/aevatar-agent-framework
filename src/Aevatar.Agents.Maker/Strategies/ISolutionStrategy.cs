namespace Aevatar.Agents.Maker;

// ============================================================
//  Solution Strategy - How to Solve Atomic Tasks
// ============================================================

/// <summary>
/// Strategy for solving atomic tasks.
/// Implement this to customize how MAKER handles leaf-level problems.
/// </summary>
public interface ISolutionStrategy
{
    /// <summary>
    /// Build the prompt for solving an atomic task.
    /// </summary>
    /// <param name="taskDescription">The atomic task to solve.</param>
    /// <param name="context">Domain context variables (includes results from sibling tasks).</param>
    /// <returns>Prompt string for the LLM.</returns>
    string BuildSolvePrompt(string taskDescription, IReadOnlyDictionary<string, string> context);
    
    /// <summary>
    /// Post-process the LLM output to extract the final answer.
    /// Default: return as-is.
    /// </summary>
    /// <param name="llmOutput">Raw LLM output.</param>
    /// <returns>Cleaned solution string.</returns>
    string ExtractSolution(string llmOutput) => llmOutput.Trim();
}

