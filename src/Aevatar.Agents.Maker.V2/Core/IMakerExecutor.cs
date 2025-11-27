namespace Aevatar.Agents.Maker.V2;

// ============================================================
//  MAKER Executor Interface - The User's Entry Point
// ============================================================

/// <summary>
/// MAKER executor - the main entry point for users.
/// Handles all decomposition, voting, and composition internally.
/// </summary>
public interface IMakerExecutor
{
    /// <summary>
    /// Execute a task using the MAKER framework.
    /// </summary>
    /// <param name="taskDescription">Description of the task to execute.</param>
    /// <param name="options">Execution options (optional, uses defaults if null).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Execution result with trace.</returns>
    Task<MakerResult> ExecuteAsync(
        string taskDescription,
        MakerOptions? options = null,
        CancellationToken ct = default);
}

