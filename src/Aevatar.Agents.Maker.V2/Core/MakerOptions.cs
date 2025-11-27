namespace Aevatar.Agents.Maker.V2;

// ============================================================
//  MAKER Configuration - Minimal, Principled, Auto-Computed
// ============================================================

/// <summary>
/// Reliability level determines the voting parameters.
/// Higher reliability = more samples = higher cost = lower error rate.
/// </summary>
public enum ReliabilityLevel
{
    /// <summary>K=1, N=1. Fast, cheap, but may fail. Use for exploration.</summary>
    Low = 1,
    
    /// <summary>K=2, N=3. Balanced for most tasks.</summary>
    Medium = 2,
    
    /// <summary>K=3, N=5. Reliable for important tasks.</summary>
    High = 3,
    
    /// <summary>K=4, N=7. High reliability for important decisions.</summary>
    VeryHigh = 4,
    
    /// <summary>K=5, N=9. Maximum reliability for critical tasks.</summary>
    Critical = 5,
    
    /// <summary>K=7, N=13. Ultra-high reliability for mission-critical.</summary>
    UltraCritical = 7,
    
    /// <summary>K=10, N=19. Extreme reliability, very expensive.</summary>
    Extreme = 10
}

/// <summary>
/// MAKER execution options. Most parameters have smart defaults.
/// </summary>
public sealed record MakerOptions
{
    /// <summary>
    /// Reliability level. Framework computes N = 2K - 1 automatically.
    /// Default: Medium (K=2, N=3)
    /// </summary>
    public ReliabilityLevel Reliability { get; init; } = ReliabilityLevel.Medium;
    
    /// <summary>
    /// Override K value directly. If set, ignores Reliability level.
    /// Use this for fine-grained control over voting parameters.
    /// </summary>
    public int? CustomK { get; init; }
    
    /// <summary>
    /// Maximum recursion depth. Default: 4 (sufficient for most tasks).
    /// </summary>
    public int MaxDepth { get; init; } = 4;
    
    /// <summary>
    /// Custom decomposition strategy. If null, uses DefaultDecomposer.
    /// </summary>
    public IDecompositionStrategy? Decomposer { get; init; }
    
    /// <summary>
    /// Custom solution strategy. If null, uses DefaultSolver.
    /// </summary>
    public ISolutionStrategy? Solver { get; init; }
    
    /// <summary>
    /// Custom composition strategy. If null, uses DefaultComposer.
    /// </summary>
    public ICompositionStrategy? Composer { get; init; }
    
    /// <summary>
    /// Domain-specific context variables passed to all strategies.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Context { get; init; }
    
    /// <summary>
    /// Progress callback for UI/logging.
    /// </summary>
    public Action<MakerProgress>? OnProgress { get; init; }
    
    /// <summary>
    /// Maximum wait time before raising red flag. Default: 60 seconds per step.
    /// </summary>
    public TimeSpan StepTimeout { get; init; } = TimeSpan.FromSeconds(60);
    
    /// <summary>
    /// LLM provider name (resolved via ILLMProviderFactory).
    /// </summary>
    public string? ProviderName { get; init; }
    
    // ============================================================
    //  Computed Properties (from ReliabilityLevel)
    // ============================================================
    
    /// <summary>
    /// K value for first-to-ahead-by-K voting.
    /// Uses CustomK if set, otherwise derived from Reliability level.
    /// </summary>
    public int ConsensusK => CustomK ?? (int)Reliability;
    
    /// <summary>
    /// Number of samples per voting round. N = 2K - 1.
    /// </summary>
    public int SamplesPerRound => 2 * ConsensusK - 1;
    
    /// <summary>
    /// Maximum voting rounds before red flag.
    /// </summary>
    public int MaxVotingRounds => 3;
}

