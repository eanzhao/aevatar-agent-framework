using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aevatar.Agents.Maker.V2;

// ============================================================
//  Configurable Strategy - Zero-Code MAKER Configuration
//  Users can define strategies via JSON without writing code
// ============================================================

/// <summary>
/// Project configuration that can be loaded from JSON/YAML.
/// </summary>
public sealed record ProjectConfig
{
    /// <summary>
    /// Project name for display.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "Custom Project";
    
    /// <summary>
    /// Project description.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";
    
    /// <summary>
    /// Icon emoji for UI.
    /// </summary>
    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "🔧";
    
    /// <summary>
    /// The main task to execute.
    /// </summary>
    [JsonPropertyName("task")]
    public string Task { get; init; } = "";
    
    /// <summary>
    /// Context variables to pass to prompts.
    /// </summary>
    [JsonPropertyName("context")]
    public Dictionary<string, string>? Context { get; init; }
    
    /// <summary>
    /// Decomposition configuration.
    /// </summary>
    [JsonPropertyName("decomposition")]
    public DecompositionConfig Decomposition { get; init; } = new();
    
    /// <summary>
    /// Solution configuration.
    /// </summary>
    [JsonPropertyName("solution")]
    public SolutionConfig Solution { get; init; } = new();
    
    /// <summary>
    /// Reliability level (Low, Medium, High, VeryHigh, Critical, UltraCritical, Extreme).
    /// </summary>
    [JsonPropertyName("reliability")]
    public string Reliability { get; init; } = "Medium";
    
    /// <summary>
    /// Custom K value for voting. If set, overrides reliability level.
    /// K=2 means 3 workers, K=5 means 9 workers, K=10 means 19 workers.
    /// </summary>
    [JsonPropertyName("customK")]
    public int? CustomK { get; init; }
    
    /// <summary>
    /// Maximum total LLM calls allowed.
    /// </summary>
    [JsonPropertyName("maxTotalLlmCalls")]
    public int MaxTotalLlmCalls { get; init; } = 100;
    
    /// <summary>
    /// Maximum total tokens allowed.
    /// </summary>
    [JsonPropertyName("maxTotalTokens")]
    public long MaxTotalTokens { get; init; } = 500_000;
    
    /// <summary>
    /// Maximum execution duration in minutes.
    /// </summary>
    [JsonPropertyName("maxDurationMinutes")]
    public int MaxDurationMinutes { get; init; } = 10;
    
    /// <summary>
    /// Parse from JSON string.
    /// </summary>
    public static ProjectConfig FromJson(string json)
    {
        return JsonSerializer.Deserialize<ProjectConfig>(json) 
            ?? throw new ArgumentException("Invalid project config JSON");
    }
    
    /// <summary>
    /// Build MakerOptions from this config.
    /// </summary>
    public MakerOptions BuildOptions(Action<MakerProgress>? onProgress = null)
    {
        var reliability = Enum.TryParse<ReliabilityLevel>(Reliability, true, out var level) 
            ? level 
            : ReliabilityLevel.Medium;
        
        return new MakerOptions
        {
            Reliability = reliability,
            CustomK = CustomK,  // Direct K override if specified
            MaxTotalLlmCalls = MaxTotalLlmCalls,
            MaxTotalTokens = MaxTotalTokens,
            MaxDuration = TimeSpan.FromMinutes(MaxDurationMinutes),
            Context = Context,
            Decomposer = new ConfigurableDecomposer(Decomposition),
            Solver = new ConfigurableSolver(Solution),
            OnProgress = onProgress
        };
    }
}

/// <summary>
/// Decomposition configuration.
/// </summary>
public sealed record DecompositionConfig
{
    /// <summary>
    /// System prompt for decomposition.
    /// Default: Generic task decomposition prompt.
    /// </summary>
    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; init; }
    
    /// <summary>
    /// User prompt template. Use {task} and {context} placeholders.
    /// </summary>
    [JsonPropertyName("promptTemplate")]
    public string? PromptTemplate { get; init; }
    
    /// <summary>
    /// Minimum depth before considering task atomic.
    /// </summary>
    [JsonPropertyName("minDepthForAtomic")]
    public int MinDepthForAtomic { get; init; } = 1;
    
    /// <summary>
    /// Keywords that indicate task is atomic (won't decompose further).
    /// </summary>
    [JsonPropertyName("atomicKeywords")]
    public List<string>? AtomicKeywords { get; init; }
}

/// <summary>
/// Solution configuration.
/// </summary>
public sealed record SolutionConfig
{
    /// <summary>
    /// System prompt for solving.
    /// </summary>
    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; init; }
    
    /// <summary>
    /// User prompt template. Use {task} and {context} placeholders.
    /// </summary>
    [JsonPropertyName("promptTemplate")]
    public string? PromptTemplate { get; init; }
    
    /// <summary>
    /// Output format hint (e.g., "markdown", "json", "text").
    /// </summary>
    [JsonPropertyName("outputFormat")]
    public string OutputFormat { get; init; } = "markdown";
}

/// <summary>
/// Configurable decomposition strategy.
/// </summary>
public sealed class ConfigurableDecomposer : IDecompositionStrategy
{
    private readonly DecompositionConfig _config;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ConfigurableDecomposer(DecompositionConfig config)
    {
        _config = config;
    }

    public string BuildDecompositionPrompt(string taskDescription, IReadOnlyDictionary<string, string> context)
    {
        if (!string.IsNullOrEmpty(_config.PromptTemplate))
        {
            var prompt = _config.PromptTemplate
                .Replace("{task}", taskDescription)
                .Replace("{context}", FormatContext(context));
            return prompt;
        }
        
        // Default template
        var contextSection = context.Count > 0
            ? $"\n[Context]\n{string.Join("\n", context.Select(kv => $"- {kv.Key}: {kv.Value}"))}\n"
            : "";

        return $$"""
            Break down the following task into 3-6 logical, sequential steps.
            
            Rules:
            1. Each step must be specific and actionable.
            2. Steps should be ordered logically.
            3. Steps should be roughly equal in complexity.
            4. Output ONLY a JSON array.
            
            Output format: [{"step_id": "S1", "description": "..."}, ...]
            {{contextSection}}
            [Task]
            {{taskDescription}}
            """;
    }

    public bool IsAtomic(string taskDescription, int currentDepth)
    {
        // Min depth check
        if (currentDepth < _config.MinDepthForAtomic) return false;
        
        // Check atomic keywords
        if (_config.AtomicKeywords?.Count > 0)
        {
            var lower = taskDescription.ToLowerInvariant();
            if (_config.AtomicKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
            {
                return true;
            }
        }
        
        // Default heuristic: short tasks are atomic
        return taskDescription.Length < 100;
    }

    public IReadOnlyList<(string StepId, string Description)> ParseDecomposition(string llmOutput)
    {
        // Try JSON parsing
        try
        {
            var trimmed = llmOutput.Trim();
            var jsonStart = trimmed.IndexOf('[');
            var jsonEnd = trimmed.LastIndexOf(']');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = trimmed[jsonStart..(jsonEnd + 1)];
                var items = JsonSerializer.Deserialize<List<StepItem>>(json, JsonOptions);
                
                if (items?.Count > 0)
                {
                    return items
                        .Where(i => !string.IsNullOrWhiteSpace(i.Description))
                        .Select(i => (i.StepId ?? $"S{items.IndexOf(i) + 1}", i.Description!))
                        .ToList();
                }
            }
        }
        catch { /* Fall through to line parsing */ }
        
        // Fallback: line-by-line parsing
        var lines = llmOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<(string, string)>();
        var stepNum = 1;
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim().TrimStart('-', '*', '•', ' ');
            if (trimmed.Length > 10)
            {
                result.Add(($"S{stepNum++}", trimmed));
            }
        }
        
        return result;
    }

    private static string FormatContext(IReadOnlyDictionary<string, string> context)
    {
        if (context.Count == 0) return "";
        return string.Join("\n", context.Select(kv => $"{kv.Key}: {kv.Value}"));
    }

    private sealed record StepItem
    {
        [JsonPropertyName("step_id")]
        public string? StepId { get; init; }
        
        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }
}

/// <summary>
/// Configurable solution strategy.
/// </summary>
public sealed class ConfigurableSolver : ISolutionStrategy
{
    private readonly SolutionConfig _config;

    public ConfigurableSolver(SolutionConfig config)
    {
        _config = config;
    }

    public string BuildSolvePrompt(string taskDescription, IReadOnlyDictionary<string, string> context)
    {
        if (!string.IsNullOrEmpty(_config.PromptTemplate))
        {
            var prompt = _config.PromptTemplate
                .Replace("{task}", taskDescription)
                .Replace("{context}", FormatContext(context));
            return prompt;
        }
        
        // Default template
        var contextSection = context.Count > 0
            ? $"\n[Context]\n{string.Join("\n", context.Select(kv => $"- {kv.Key}: {kv.Value}"))}\n"
            : "";
        
        var formatHint = _config.OutputFormat.ToLowerInvariant() switch
        {
            "json" => "Output your answer as valid JSON.",
            "markdown" => "Format your answer using Markdown.",
            _ => "Provide a clear, direct answer."
        };

        return $$"""
            Solve the following task.
            
            {{formatHint}}
            {{contextSection}}
            [Task]
            {{taskDescription}}
            """;
    }

    public string ExtractSolution(string llmOutput)
    {
        // For JSON format, try to extract JSON
        if (_config.OutputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            var trimmed = llmOutput.Trim();
            var jsonStart = trimmed.IndexOfAny(['{', '[']);
            var jsonEnd = Math.Max(trimmed.LastIndexOf('}'), trimmed.LastIndexOf(']'));
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                return trimmed[jsonStart..(jsonEnd + 1)];
            }
        }
        
        // Default: return trimmed output
        return llmOutput.Trim();
    }

    private static string FormatContext(IReadOnlyDictionary<string, string> context)
    {
        if (context.Count == 0) return "";
        return string.Join("\n", context.Select(kv => $"{kv.Key}: {kv.Value}"));
    }
}

