using Aevatar.Agents.CreativeReasoning.Messages;

namespace Aevatar.Agents.CreativeReasoning.Core;

/// <summary>
/// Result of Universe of Thoughts creative reasoning execution.
/// </summary>
public class UoTResult
{
    /// <summary>Whether execution completed successfully</summary>
    public required bool Success { get; init; }
    
    /// <summary>The best creative solution found</summary>
    public CandidateSolution? BestSolution { get; init; }
    
    /// <summary>All candidate solutions, ranked by composite score</summary>
    public IReadOnlyList<CandidateSolution> AllCandidates { get; init; } = [];
    
    /// <summary>Error message if failed</summary>
    public string? Error { get; init; }
    
    /// <summary>Execution trace for analysis</summary>
    public required UoTResultTrace Trace { get; init; }
}

/// <summary>
/// Execution trace for debugging and analysis.
/// </summary>
public class UoTResultTrace
{
    public required string ExecutionId { get; init; }
    public required string OriginalProblem { get; init; }
    public int AnalogiesExplored { get; init; }
    public int ThoughtsExtracted { get; init; }
    public int CandidatesGenerated { get; init; }
    public int CandidatesPassedFeasibility { get; init; }
    public int TotalLLMCalls { get; init; }
    public long TotalTokens { get; init; }
    public TimeSpan Duration { get; init; }
    
    /// <summary>Analogies explored (for debugging)</summary>
    public IReadOnlyList<AnalogousProblem> Analogies { get; init; } = [];
}

