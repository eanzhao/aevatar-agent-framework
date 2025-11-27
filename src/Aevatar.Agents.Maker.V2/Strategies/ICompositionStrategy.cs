namespace Aevatar.Agents.Maker.V2;

// ============================================================
//  Composition Strategy - How to Aggregate Results
// ============================================================

/// <summary>
/// Strategy for composing results from subtasks into a final result.
/// Implement this to customize how MAKER aggregates child outputs.
/// </summary>
public interface ICompositionStrategy
{
    /// <summary>
    /// Compose results from multiple subtasks into a single result.
    /// </summary>
    /// <param name="originalTask">The original parent task description.</param>
    /// <param name="subtaskResults">Results from each subtask (stepId -> result).</param>
    /// <param name="context">Domain context variables.</param>
    /// <returns>Composed result, or null to use LLM-based synthesis.</returns>
    string? Compose(
        string originalTask,
        IReadOnlyDictionary<string, string> subtaskResults,
        IReadOnlyDictionary<string, string> context);
    
    /// <summary>
    /// Build a prompt for LLM-based synthesis (used when Compose returns null).
    /// </summary>
    /// <param name="originalTask">The original parent task description.</param>
    /// <param name="subtaskResults">Results from each subtask.</param>
    /// <param name="context">Domain context variables.</param>
    /// <returns>Prompt for LLM synthesis.</returns>
    string BuildSynthesisPrompt(
        string originalTask,
        IReadOnlyDictionary<string, string> subtaskResults,
        IReadOnlyDictionary<string, string> context);
}

