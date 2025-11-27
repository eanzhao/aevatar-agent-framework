namespace Aevatar.Agents.Maker;

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
    /// Build the prompt for decomposing a task with specified granularity.
    /// The LLM should output a JSON array of steps.
    /// </summary>
    /// <param name="taskDescription">The task to decompose.</param>
    /// <param name="context">Domain context variables.</param>
    /// <param name="granularity">Decomposition granularity hint.</param>
    /// <returns>Prompt string for the LLM.</returns>
    string BuildDecompositionPrompt(
        string taskDescription, 
        IReadOnlyDictionary<string, string> context,
        DecompositionGranularity granularity)
        => BuildDecompositionPrompt(taskDescription, context); // Default implementation for backward compatibility
    
    /// <summary>
    /// Determine if a task is atomic (cannot be decomposed further).
    /// This is used as a fallback when LLM-based atomicity assessment fails.
    /// </summary>
    /// <param name="taskDescription">The task description.</param>
    /// <param name="currentDepth">Current recursion depth (for heuristics).</param>
    /// <returns>True if the task should be solved directly without decomposition.</returns>
    bool IsAtomic(string taskDescription, int currentDepth);
    
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

