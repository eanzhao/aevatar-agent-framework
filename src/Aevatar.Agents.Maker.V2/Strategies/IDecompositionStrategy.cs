namespace Aevatar.Agents.Maker.V2;

// ============================================================
//  Decomposition Strategy - How to Break Down Tasks
// ============================================================

/// <summary>
/// Strategy for decomposing complex tasks into subtasks.
/// Implement this to customize how MAKER breaks down problems.
/// </summary>
public interface IDecompositionStrategy
{
    /// <summary>
    /// Build the prompt for decomposing a task.
    /// The LLM should output a JSON array of steps.
    /// </summary>
    /// <param name="taskDescription">The task to decompose.</param>
    /// <param name="context">Domain context variables.</param>
    /// <returns>Prompt string for the LLM.</returns>
    string BuildDecompositionPrompt(string taskDescription, IReadOnlyDictionary<string, string> context);
    
    /// <summary>
    /// Determine if a task is atomic (cannot be decomposed further).
    /// </summary>
    /// <param name="taskDescription">The task description.</param>
    /// <param name="currentDepth">Current recursion depth.</param>
    /// <param name="maxDepth">Maximum allowed depth.</param>
    /// <returns>True if the task should be solved directly without decomposition.</returns>
    bool IsAtomic(string taskDescription, int currentDepth, int maxDepth);
    
    /// <summary>
    /// Parse the LLM output into a list of subtask descriptions.
    /// </summary>
    /// <param name="llmOutput">Raw LLM output.</param>
    /// <returns>List of (stepId, description) tuples.</returns>
    IReadOnlyList<(string StepId, string Description)> ParseDecomposition(string llmOutput);
}

/// <summary>
/// A decomposed step.
/// </summary>
public readonly record struct DecomposedStep(string StepId, string Description);

