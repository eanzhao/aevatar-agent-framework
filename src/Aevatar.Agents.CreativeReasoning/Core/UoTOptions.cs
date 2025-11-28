namespace Aevatar.Agents.CreativeReasoning.Core;

/// <summary>
/// Configuration options for Universe of Thoughts (UoT) creative reasoning.
/// </summary>
public class UoTOptions
{
    /// <summary>Maximum analogous problems to retrieve (default: 5)</summary>
    public int MaxAnalogies { get; set; } = 5;
    
    /// <summary>Solutions to generate per analogous problem (default: 3)</summary>
    public int SolutionsPerAnalogy { get; set; } = 3;
    
    /// <summary>Maximum candidate solutions to synthesize (default: 10)</summary>
    public int MaxCandidates { get; set; } = 10;
    
    /// <summary>Minimum feasibility score to pass (hard constraint, default: 0.6)</summary>
    public float FeasibilityThreshold { get; set; } = 0.6f;
    
    /// <summary>Semantic similarity threshold for embedding comparisons (default: 0.7)</summary>
    public float SemanticSimilarityThreshold { get; set; } = 0.7f;
    
    /// <summary>Weight for utility in composite score (default: 0.5)</summary>
    public float UtilityWeight { get; set; } = 0.5f;
    
    /// <summary>Weight for novelty in composite score (default: 0.5)</summary>
    public float NoveltyWeight { get; set; } = 0.5f;
    
    /// <summary>Distance threshold for "far" donors in Far-then-Analogical selection (default: 0.6)</summary>
    public float FarDistanceThreshold { get; set; } = 0.6f;
    
    /// <summary>Number of far donors to consider (default: 3)</summary>
    public int FarDonorsCount { get; set; } = 3;
    
    /// <summary>LLM provider name (required)</summary>
    public string ProviderName { get; set; } = string.Empty;
    
    /// <summary>Optional domain hint for better analogy retrieval</summary>
    public string? DomainHint { get; set; }
    
    /// <summary>Progress callback</summary>
    public Action<UoTProgress>? OnProgress { get; set; }
}

/// <summary>
/// Progress information during UoT execution.
/// </summary>
public class UoTProgress
{
    public required UoTPhase Phase { get; init; }
    public required string Message { get; init; }
    public float ProgressPercent { get; init; }
    public int AnalogiesFound { get; init; }
    public int ThoughtsExtracted { get; init; }
    public int CandidatesGenerated { get; init; }
    public int CandidatesPassed { get; init; }
}

public enum UoTPhase
{
    Starting,
    RetrievingAnalogies,
    DecomposingThoughts,
    SelectingHost,
    SelectingDonors,
    Synthesizing,
    Evaluating,
    Completed,
    Failed
}

