namespace Aevatar.Agents.Maker;

// ============================================================
//  Red Flag Handler - Error Detection and Recovery
// ============================================================

/// <summary>
/// Red flag handler for detecting and recovering from errors.
/// </summary>
public interface IRedFlagHandler
{
    /// <summary>
    /// Called when a red flag condition is detected.
    /// </summary>
    /// <param name="context">Context about the failure.</param>
    /// <returns>Recovery action to take.</returns>
    RedFlagRecovery HandleRedFlag(RedFlagContext context);
}

/// <summary>
/// Context for a red flag event.
/// </summary>
public sealed record RedFlagContext
{
    /// <summary>
    /// Task where the red flag occurred.
    /// </summary>
    public required string TaskId { get; init; }

    /// <summary>
    /// Task description.
    /// </summary>
    public required string TaskDescription { get; init; }

    /// <summary>
    /// Type of red flag.
    /// </summary>
    public required RedFlagType Type { get; init; }

    /// <summary>
    /// Detailed reason.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Current depth in task tree.
    /// </summary>
    public int Depth { get; init; }

    /// <summary>
    /// Number of previous recovery attempts.
    /// </summary>
    public int RecoveryAttempts { get; init; }

    /// <summary>
    /// Best candidate content (if any).
    /// </summary>
    public string? BestCandidate { get; init; }
}

/// <summary>
/// Types of red flag conditions.
/// </summary>
public enum RedFlagType
{
    /// <summary>Voting failed to reach consensus.</summary>
    NoConsensus,

    /// <summary>Task timed out.</summary>
    Timeout,

    /// <summary>All LLM calls failed.</summary>
    ExecutionFailure,

    /// <summary>Decomposition produced no valid steps.</summary>
    InvalidDecomposition,

    /// <summary>Maximum depth exceeded.</summary>
    DepthExceeded
}

/// <summary>
/// Recovery action for a red flag.
/// </summary>
public sealed record RedFlagRecovery
{
    /// <summary>
    /// Action to take.
    /// </summary>
    public required RecoveryAction Action { get; init; }

    /// <summary>
    /// Modified prompt to retry (if Action is Retry).
    /// </summary>
    public string? ModifiedPrompt { get; init; }

    /// <summary>
    /// Message to include in result.
    /// </summary>
    public string? Message { get; init; }
}

/// <summary>
/// Recovery actions.
/// </summary>
public enum RecoveryAction
{
    /// <summary>Retry with same or modified parameters.</summary>
    Retry,

    /// <summary>Use the best available candidate despite no consensus.</summary>
    AcceptBestEffort,

    /// <summary>Treat the task as atomic and solve directly.</summary>
    ForceAtomic,

    /// <summary>Abort this branch entirely.</summary>
    Abort
}

/// <summary>
/// Default red flag handler with sensible recovery strategies.
/// </summary>
public sealed class DefaultRedFlagHandler : IRedFlagHandler
{
    /// <summary>
    /// Maximum recovery attempts per task.
    /// </summary>
    public int MaxRecoveryAttempts { get; init; } = 2;

    /// <inheritdoc />
    public RedFlagRecovery HandleRedFlag(RedFlagContext context)
    {
        // Too many recovery attempts - give up
        if (context.RecoveryAttempts >= MaxRecoveryAttempts)
        {
            return context.BestCandidate != null
                ? new RedFlagRecovery
                {
                    Action = RecoveryAction.AcceptBestEffort,
                    Message = $"Accepting best-effort result after {context.RecoveryAttempts} recovery attempts."
                }
                : new RedFlagRecovery
                {
                    Action = RecoveryAction.Abort,
                    Message = $"Aborting after {context.RecoveryAttempts} failed recovery attempts."
                };
        }

        return context.Type switch
        {
            RedFlagType.NoConsensus => HandleNoConsensus(context),
            RedFlagType.Timeout => HandleTimeout(context),
            RedFlagType.ExecutionFailure => HandleExecutionFailure(context),
            RedFlagType.InvalidDecomposition => HandleInvalidDecomposition(context),
            RedFlagType.DepthExceeded => HandleDepthExceeded(context),
            _ => new RedFlagRecovery { Action = RecoveryAction.Abort, Message = "Unknown red flag type." }
        };
    }

    private static RedFlagRecovery HandleNoConsensus(RedFlagContext context)
    {
        // If we have a best candidate, use it
        if (!string.IsNullOrWhiteSpace(context.BestCandidate))
        {
            return new RedFlagRecovery
            {
                Action = RecoveryAction.AcceptBestEffort,
                Message = "Accepting leading candidate despite lack of consensus."
            };
        }

        // Retry with clearer prompt
        return new RedFlagRecovery
        {
            Action = RecoveryAction.Retry,
            ModifiedPrompt = $"IMPORTANT: Previous attempts failed to reach agreement. Be MORE SPECIFIC and DETERMINISTIC.\n\n{context.TaskDescription}",
            Message = "Retrying with emphasis on determinism."
        };
    }

    private static RedFlagRecovery HandleTimeout(RedFlagContext context)
    {
        // Treat as atomic to simplify
        return new RedFlagRecovery
        {
            Action = RecoveryAction.ForceAtomic,
            Message = "Timeout occurred. Treating task as atomic."
        };
    }

    private static RedFlagRecovery HandleExecutionFailure(RedFlagContext context)
    {
        // Simple retry
        return new RedFlagRecovery
        {
            Action = RecoveryAction.Retry,
            Message = "Retrying after execution failure."
        };
    }

    private static RedFlagRecovery HandleInvalidDecomposition(RedFlagContext context)
    {
        // Force atomic if decomposition fails
        return new RedFlagRecovery
        {
            Action = RecoveryAction.ForceAtomic,
            Message = "Decomposition failed. Treating as atomic task."
        };
    }

    private static RedFlagRecovery HandleDepthExceeded(RedFlagContext context)
    {
        // Already at max depth - must solve directly
        return new RedFlagRecovery
        {
            Action = RecoveryAction.ForceAtomic,
            Message = "Maximum depth exceeded. Forcing atomic solution."
        };
    }
}

