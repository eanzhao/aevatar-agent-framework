namespace Aevatar.Agents.Maker.V2;

// ============================================================
//  MAKER Progress - Real-time Feedback
// ============================================================

/// <summary>
/// Progress update during MAKER execution.
/// </summary>
public sealed record MakerProgress
{
    /// <summary>
    /// Current phase of execution.
    /// </summary>
    public required MakerPhase Phase { get; init; }
    
    /// <summary>
    /// Current task being processed.
    /// </summary>
    public required string TaskId { get; init; }
    
    /// <summary>
    /// Human-readable message.
    /// </summary>
    public required string Message { get; init; }
    
    /// <summary>
    /// Current depth in task tree.
    /// </summary>
    public int Depth { get; init; }
    
    /// <summary>
    /// Voting progress (if in voting phase).
    /// </summary>
    public VotingProgress? Voting { get; init; }
    
    /// <summary>
    /// LLM proposal content (when a new proposal is received).
    /// </summary>
    public LLMProposal? Proposal { get; init; }
    
    /// <summary>
    /// Timestamp.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// LLM proposal received during voting.
/// </summary>
public sealed record LLMProposal
{
    /// <summary>
    /// Unique ID for this proposal (e.g., "P1", "P2").
    /// </summary>
    public required string ProposalId { get; init; }
    
    /// <summary>
    /// The full content of the LLM response.
    /// </summary>
    public required string Content { get; init; }
    
    /// <summary>
    /// Whether this proposal was successful (no errors).
    /// </summary>
    public bool Success { get; init; }
    
    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; init; }
    
    /// <summary>
    /// Prompt tokens used.
    /// </summary>
    public int PromptTokens { get; init; }
    
    /// <summary>
    /// Completion tokens used.
    /// </summary>
    public int CompletionTokens { get; init; }
    
    /// <summary>
    /// Total tokens used.
    /// </summary>
    public int TotalTokens => PromptTokens + CompletionTokens;
    
    /// <summary>
    /// Latency in milliseconds.
    /// </summary>
    public long LatencyMs { get; init; }
}

/// <summary>
/// Execution phase.
/// </summary>
public enum MakerPhase
{
    /// <summary>Starting execution.</summary>
    Starting,
    
    /// <summary>Assessing if task is atomic.</summary>
    Assessing,
    
    /// <summary>Decomposing task into subtasks.</summary>
    Decomposing,
    
    /// <summary>Voting on proposals.</summary>
    Voting,
    
    /// <summary>Executing subtasks.</summary>
    Executing,
    
    /// <summary>Solving atomic task.</summary>
    Solving,
    
    /// <summary>Composing results from subtasks.</summary>
    Composing,
    
    /// <summary>Red flag raised, attempting recovery.</summary>
    RedFlag,
    
    /// <summary>Completed successfully.</summary>
    Completed,
    
    /// <summary>Failed.</summary>
    Failed
}

/// <summary>
/// Voting progress details.
/// </summary>
public sealed record VotingProgress
{
    /// <summary>
    /// Type of vote.
    /// </summary>
    public required VotingType Type { get; init; }
    
    /// <summary>
    /// Current round number.
    /// </summary>
    public int Round { get; init; }
    
    /// <summary>
    /// Total votes collected so far.
    /// </summary>
    public int TotalVotes { get; init; }
    
    /// <summary>
    /// Votes needed for consensus.
    /// </summary>
    public int VotesNeeded { get; init; }
    
    /// <summary>
    /// Current leader's vote count.
    /// </summary>
    public int LeaderVotes { get; init; }
    
    /// <summary>
    /// Runner-up's vote count.
    /// </summary>
    public int RunnerUpVotes { get; init; }
    
    /// <summary>
    /// Number of semantic clusters formed.
    /// </summary>
    public int ClusterCount { get; init; }
    
    /// <summary>
    /// Whether semantic clustering was used.
    /// </summary>
    public bool UsedSemanticClustering { get; init; }
}

/// <summary>
/// A vote candidate/cluster.
/// </summary>
public sealed record VoteCandidate
{
    public required string Hash { get; init; }
    public required string Content { get; init; }
    public required int Votes { get; init; }
    public int ClusterSize { get; init; } = 1;
}

/// <summary>
/// Type of voting decision.
/// </summary>
public enum VotingType
{
    /// <summary>Voting on how to decompose a task.</summary>
    Decomposition,
    
    /// <summary>Voting on the solution to an atomic task.</summary>
    Solution
}

