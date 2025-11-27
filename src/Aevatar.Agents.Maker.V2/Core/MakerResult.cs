namespace Aevatar.Agents.Maker.V2;

// ============================================================
//  MAKER Result - Clean, Queryable Output
// ============================================================

/// <summary>
/// Result of a MAKER execution.
/// </summary>
public sealed record MakerResult
{
    /// <summary>
    /// Whether the execution succeeded.
    /// </summary>
    public required bool Success { get; init; }
    
    /// <summary>
    /// Final output content.
    /// </summary>
    public required string Content { get; init; }
    
    /// <summary>
    /// Execution trace for debugging/analysis.
    /// </summary>
    public required MakerTrace Trace { get; init; }
    
    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; init; }
    
    /// <summary>
    /// Total LLM calls made.
    /// </summary>
    public int TotalLLMCalls => Trace.TotalLLMCalls;
    
    /// <summary>
    /// Total execution time.
    /// </summary>
    public TimeSpan Duration => Trace.Duration;
}

/// <summary>
/// Execution trace containing all steps and votes.
/// </summary>
public sealed record MakerTrace
{
    /// <summary>
    /// Unique execution ID.
    /// </summary>
    public required string ExecutionId { get; init; }
    
    /// <summary>
    /// Root task node.
    /// </summary>
    public required TaskNode RootTask { get; init; }
    
    /// <summary>
    /// Total LLM calls across all voting rounds.
    /// </summary>
    public int TotalLLMCalls { get; init; }
    
    /// <summary>
    /// Total execution duration.
    /// </summary>
    public TimeSpan Duration { get; init; }
    
    /// <summary>
    /// Red flags raised during execution.
    /// </summary>
    public IReadOnlyList<RedFlagEvent> RedFlags { get; init; } = [];
}

/// <summary>
/// A node in the task tree.
/// </summary>
public sealed record TaskNode
{
    /// <summary>
    /// Task identifier.
    /// </summary>
    public required string TaskId { get; init; }
    
    /// <summary>
    /// Task description.
    /// </summary>
    public required string Description { get; init; }
    
    /// <summary>
    /// Depth in the tree (0 = root).
    /// </summary>
    public int Depth { get; init; }
    
    /// <summary>
    /// Whether this was an atomic task (solved directly).
    /// </summary>
    public bool IsAtomic { get; init; }
    
    /// <summary>
    /// Voting sessions for this task (decomposition and/or solution).
    /// </summary>
    public IReadOnlyList<VotingSession> VotingSessions { get; init; } = [];
    
    /// <summary>
    /// Child tasks if decomposed.
    /// </summary>
    public IReadOnlyList<TaskNode> Children { get; init; } = [];
    
    /// <summary>
    /// Final result of this task.
    /// </summary>
    public string? Result { get; set; }
    
    /// <summary>
    /// Best candidate from voting even if no consensus (for fallback use).
    /// Per MAKER paper: when consensus fails, this can be used at max depth.
    /// </summary>
    public string? FallbackCandidate { get; init; }
}

/// <summary>
/// A voting session record.
/// </summary>
public sealed record VotingSession
{
    /// <summary>
    /// Type of decision being voted on.
    /// </summary>
    public required VotingType Type { get; init; }
    
    /// <summary>
    /// All candidates with their vote counts.
    /// </summary>
    public IReadOnlyList<VoteCandidate> Candidates { get; init; } = [];
    
    /// <summary>
    /// Winning candidate (if consensus reached).
    /// </summary>
    public VoteCandidate? Winner { get; init; }
    
    /// <summary>
    /// Total rounds of voting.
    /// </summary>
    public int Rounds { get; init; }
}

// VotingType and VoteCandidate are defined in MakerProgress.cs

/// <summary>
/// Red flag event.
/// </summary>
public sealed record RedFlagEvent
{
    /// <summary>
    /// Task where red flag was raised.
    /// </summary>
    public required string TaskId { get; init; }
    
    /// <summary>
    /// Reason for the red flag.
    /// </summary>
    public required string Reason { get; init; }
    
    /// <summary>
    /// Timestamp.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    
    /// <summary>
    /// Whether recovery was successful.
    /// </summary>
    public bool Recovered { get; init; }
}

