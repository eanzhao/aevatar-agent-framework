namespace Aevatar.Agents.Maker.V2;

// ============================================================
//  MAKER Configuration - Minimal, Principled, Auto-Computed
// ============================================================

/// <summary>
/// Execution mode: Production (cost-efficient) vs Academic (paper-faithful).
/// </summary>
public enum ExecutionMode
{
    /// <summary>
    /// Production mode: Assess atomicity first, decompose only if needed.
    /// Optimized for cost and speed. Good for real-world applications.
    /// </summary>
    Production,
    
    /// <summary>
    /// Academic mode: Force decomposition by default (paper's "Maximal Decomposition").
    /// Maximum correctness at high cost. Use for research or critical tasks.
    /// </summary>
    Academic
}

/// <summary>
/// Context isolation mode for child tasks.
/// </summary>
public enum ContextIsolationMode
{
    /// <summary>Pass all parent context to children. May cause context bloat.</summary>
    Full,
    
    /// <summary>Only pass explicitly relevant context keys.</summary>
    Minimal,
    
    /// <summary>No context inheritance. Each task starts fresh.</summary>
    None
}

/// <summary>
/// Decomposition granularity - how many subtasks per decomposition.
/// </summary>
public enum DecompositionGranularity
{
    /// <summary>3-6 subtasks per decomposition. Good balance of depth and breadth.</summary>
    Balanced,
    
    /// <summary>2 subtasks per decomposition (binary split). Paper's m=1 approach.</summary>
    Binary,
    
    /// <summary>Only identify the immediate next step. Extreme granularity.</summary>
    Single
}

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
    
    // ============================================================
    //  Resource Budget (replaces MaxDepth)
    //  Philosophy: "Not limited by depth, but by budget"
    //  This allows decomposition to continue until truly atomic tasks,
    //  while still providing practical cost/time limits.
    // ============================================================
    
    /// <summary>
    /// Maximum total LLM calls allowed.
    /// Set to 0 or negative for unlimited (not recommended).
    /// 
    /// Estimation guide:
    /// - Simple task (K=2, 1 level): ~20-30 calls
    /// - Medium task (K=3, 2 levels): ~100-200 calls
    /// - Complex task (K=3, 3+ levels): ~300-500 calls
    /// - Very complex task: 500+ calls
    /// 
    /// Default: 500 calls (enough for most complex tasks)
    /// </summary>
    public int MaxTotalLlmCalls { get; init; } = 500;
    
    /// <summary>
    /// Maximum total tokens allowed (prompt + completion).
    /// Set to 0 or negative for unlimited (not recommended).
    /// 
    /// Estimation guide (GPT-4 class models):
    /// - Simple task: ~100K-200K tokens
    /// - Medium task: ~300K-500K tokens
    /// - Complex task: ~500K-1M tokens
    /// - Very complex task: 1M+ tokens
    /// 
    /// Default: 2,000,000 tokens (~$10-50 depending on model)
    /// </summary>
    public long MaxTotalTokens { get; init; } = 2_000_000;
    
    /// <summary>
    /// Maximum execution duration.
    /// Complex tasks with many voting rounds can take significant time.
    /// Default: 30 minutes
    /// </summary>
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(30);
    
    /// <summary>
    /// Soft depth warning threshold.
    /// Triggers a warning when depth exceeds this, but doesn't stop execution.
    /// Useful for detecting potentially runaway decomposition.
    /// Default: 10 (just a warning, not a hard limit)
    /// </summary>
    public int DepthWarningThreshold { get; init; } = 10;
    
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
    //  Advanced MAKER System Parameters
    // ============================================================
    
    /// <summary>
    /// Clustering method for voting.
    /// - "auto": Use semantic if embedding available, otherwise exact (default)
    /// - "exact": Exact hash match (fast)
    /// - "semantic": Semantic similarity clustering (requires embedding model)
    /// </summary>
    public string ClusteringMethod { get; init; } = "auto";
    
    /// <summary>
    /// Semantic similarity threshold for clustering (0.0 - 1.0).
    /// Only used when ClusteringMethod is "semantic".
    /// Higher = stricter matching, fewer clusters.
    /// Default: 0.85
    /// </summary>
    public float SemanticSimilarityThreshold { get; init; } = 0.85f;
    
    /// <summary>
    /// Temperature variance for LLM decorrelation.
    /// Each worker gets temperature ± variance to reduce correlated errors.
    /// Default: 0.1
    /// </summary>
    public float TemperatureVariance { get; init; } = 0.1f;
    
    /// <summary>
    /// Base temperature for LLM calls.
    /// Default: 0.3
    /// </summary>
    public float BaseTemperature { get; init; } = 0.3f;
    
    /// <summary>
    /// Whether to use multiple LLM providers for decorrelation.
    /// Default: false (use same provider)
    /// </summary>
    public bool UseMultipleProviders { get; init; } = false;
    
    /// <summary>
    /// Red flag threshold - number of consecutive failures before escalation.
    /// Default: 3
    /// </summary>
    public int RedFlagThreshold { get; init; } = 3;
    
    // ============================================================
    //  Execution Mode (Production vs Academic)
    //  Production: Optimized for cost/speed, uses atomicity assessment
    //  Academic: Follows paper's "Maximal Decomposition" philosophy
    // ============================================================
    
    /// <summary>
    /// Execution mode determines the decomposition strategy.
    /// - Production (default): Assess atomicity first, decompose only if needed. Cost-efficient.
    /// - Academic: Force decomposition by default (paper's approach). Maximum correctness, high cost.
    /// </summary>
    public ExecutionMode Mode { get; init; } = ExecutionMode.Production;
    
    /// <summary>
    /// Hard depth cap - absolute maximum recursion depth to prevent StackOverflow.
    /// This is NOT a business limit (use Budget for that), just a safety net.
    /// If reached, task is force-solved as atomic regardless of other settings.
    /// Default: 50 (should never be hit in normal operation)
    /// </summary>
    public int HardDepthCap { get; init; } = 50;
    
    /// <summary>
    /// Context isolation mode.
    /// - Full: Pass all parent context to children (current behavior, may cause context bloat)
    /// - Minimal: Only pass explicitly relevant context (paper's approach)
    /// - None: No context inheritance (extreme isolation)
    /// Default: Full (for backward compatibility)
    /// </summary>
    public ContextIsolationMode ContextIsolation { get; init; } = ContextIsolationMode.Full;
    
    /// <summary>
    /// Decomposition granularity hint.
    /// - Balanced: 3-6 subtasks per decomposition (default, good for most tasks)
    /// - Binary: 2 subtasks per decomposition (paper's m=1 approach, slower but more precise)
    /// - Single: Only identify the immediate next step (extreme granularity)
    /// </summary>
    public DecompositionGranularity Granularity { get; init; } = DecompositionGranularity.Balanced;
    
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
    
}

