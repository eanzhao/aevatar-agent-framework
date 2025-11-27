using Aevatar.Agents.Maker;

namespace MakerSystem.Projects;

// ============================================================
//  Paper Revision Strategies
//  Multi-Agent collaborative paper improvement
//  LLMs directly revise the paper to publication quality
// ============================================================

/// <summary>
/// Paper content loader for markdown papers.
/// Configure the path to your paper here.
/// </summary>
public static class ReviewablePaper
{
    // ============================================================
    //  ⚙️ CONFIGURATION - Set your paper path here!
    // ============================================================
    
    /// <summary>
    /// Path to the markdown paper to be revised.
    /// Can be absolute or relative to the project root.
    /// </summary>
    public static string PaperPath { get; set; } = "papers/my_paper.md";
    
    /// <summary>
    /// Target venue/journal for publication (affects revision criteria).
    /// Examples: "NeurIPS", "ICML", "Nature", "ACL", "IEEE TPAMI"
    /// </summary>
    public static string TargetVenue { get; set; } = "Top-tier AI Conference";
    
    /// <summary>
    /// Language of the paper.
    /// </summary>
    public static string Language { get; set; } = "English";

    // ============================================================
    //  Internal Implementation
    // ============================================================
    
    private static readonly Lazy<string> LazyContent = new(LoadPaper, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<string> LazyTitle = new(ExtractTitle, LazyThreadSafetyMode.ExecutionAndPublication);
    
    public static string Content => LazyContent.Value;
    public static string Title => LazyTitle.Value;

    private static string LoadPaper()
    {
        var path = FindPaperPath();
        if (path != null && File.Exists(path))
        {
            return File.ReadAllText(path);
        }
        return $"[Paper not found at: {PaperPath}. Please set ReviewablePaper.PaperPath before running.]";
    }

    private static string? FindPaperPath()
    {
        // Try absolute path first
        if (Path.IsPathRooted(PaperPath) && File.Exists(PaperPath))
            return PaperPath;
        
        // Search from current directory upwards
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(current); i++)
        {
            var candidate = Path.Combine(current, PaperPath);
            if (File.Exists(candidate)) return candidate;
            current = Directory.GetParent(current)?.FullName;
        }
        
        // Also try from working directory
        var workingDir = Directory.GetCurrentDirectory();
        var fromWorking = Path.Combine(workingDir, PaperPath);
        if (File.Exists(fromWorking)) return fromWorking;
        
        return null;
    }

    private static string ExtractTitle()
    {
        var content = Content;
        // Try to extract title from first # heading
        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ") && !trimmed.StartsWith("## "))
            {
                return trimmed[2..].Trim();
            }
        }
        // Fallback to filename
        return Path.GetFileNameWithoutExtension(PaperPath);
    }
}

// ============================================================
//  Revision Decomposition Strategy
// ============================================================

/// <summary>
/// Decomposes paper revision into sections.
/// Each section will be revised independently by multiple LLMs.
/// </summary>
public sealed class PaperRevisionDecomposer : IDecompositionStrategy
{
    public string BuildDecompositionPrompt(string task, IReadOnlyDictionary<string, string> ctx)
    {
        var jsonFormat = """[{"step_id":"S1","description":"Revise section: Abstract"}]""";
        
        return $"""
            You are a Senior Editor at "{ReviewablePaper.TargetVenue}".
            
            Analyze this paper and decompose the revision task into sections.
            
            For each section, identify what needs to be revised/improved.
            
            Common sections:
            - Abstract
            - Introduction
            - Related Work / Background
            - Methods / Methodology
            - Experiments / Results
            - Discussion
            - Conclusion
            
            Output: JSON array {jsonFormat}
            Output ONLY valid JSON, no markdown code blocks.
            
            [Original Paper]
            ```markdown
            {ReviewablePaper.Content}
            ```
            
            [Task]
            {task}
            """;
    }

    public bool IsAtomic(string task, int depth) => depth >= 1;

    public IReadOnlyList<(string, string)> ParseDecomposition(string output)
        => new DefaultDecomposer().ParseDecomposition(output);
}

// ============================================================
//  Revision Solution Strategy
// ============================================================

/// <summary>
/// Directly revises a specific section of the paper.
/// Outputs: Revised content + Change summary.
/// </summary>
public sealed class PaperRevisionSolver : ISolutionStrategy
{
    public string BuildSolvePrompt(string task, IReadOnlyDictionary<string, string> ctx)
    {
        return $"""
            You are an Expert Editor for "{ReviewablePaper.TargetVenue}".
            Your task is to DIRECTLY REVISE the specified section to publication quality.
            
            ## Instructions
            
            1. **Read** the original section carefully.
            2. **Revise** the content directly - improve clarity, logic, language, and rigor.
            3. **Output** the revised section in Markdown format.
            4. **Append** a brief summary of what you changed at the end.
            
            ## Revision Guidelines
            
            - Fix grammatical and spelling errors
            - Improve sentence structure and flow
            - Strengthen argumentation and logic
            - Add transitions between paragraphs
            - Make claims more precise and well-supported
            - Ensure consistency in terminology
            - Improve technical accuracy
            
            ## Output Format
            
            [Output the REVISED section content directly in Markdown]
            
            ---
            
            **Changes Made:**
            - [List each specific change you made]
            - ...
            
            ---
            
            ## Original Paper ({ReviewablePaper.Language})
            
            ```markdown
            {ReviewablePaper.Content}
            ```
            
            ---
            
            [Task: Revise this section]
            {task}
            
            [Your Revised Version]
            """;
    }
}

// ============================================================
//  Revision Composition Strategy
// ============================================================

/// <summary>
/// Composes all revised sections into the final paper.
/// </summary>
public sealed class PaperRevisionComposer : ICompositionStrategy
{
    /// <summary>
    /// Returns null to use LLM-based composition.
    /// We need LLM to properly merge sections and ensure consistency.
    /// </summary>
    public string? Compose(
        string originalTask,
        IReadOnlyDictionary<string, string> subtaskResults,
        IReadOnlyDictionary<string, string> context)
    {
        return null; // Use LLM synthesis
    }

    /// <summary>
    /// Build synthesis prompt to merge all revised sections.
    /// </summary>
    public string BuildSynthesisPrompt(
        string originalTask,
        IReadOnlyDictionary<string, string> subtaskResults,
        IReadOnlyDictionary<string, string> context)
    {
        var revisedSections = string.Join("\n\n---\n\n", 
            subtaskResults.Select((kv, i) => $"## Revised Section #{i + 1}: {kv.Key}\n\n{kv.Value}"));
        
        var venue = context.GetValueOrDefault("venue", ReviewablePaper.TargetVenue);
        var paperTitle = context.GetValueOrDefault("paper_title", ReviewablePaper.Title);
        
        return $"""
            You are the Chief Editor for "{venue}".
            
            Your task is to MERGE all revised sections into a complete, polished paper.
            
            ## Instructions
            
            1. **Combine** all revised sections into one coherent paper.
            2. **Ensure Consistency** - terminology, notation, and style should be uniform.
            3. **Add Transitions** - ensure smooth flow between sections.
            4. **Output** the complete revised paper in Markdown format.
            5. **Append** a comprehensive summary of ALL changes at the end.
            
            ## Output Format
            
            # {paperTitle}
            
            [Complete revised paper content...]
            
            ---
            
            ## 📝 Revision Summary
            
            ### Overview
            [Brief summary of the revision goals and approach]
            
            ### Section-by-Section Changes
            
            **Abstract:**
            - [Changes...]
            
            **Introduction:**
            - [Changes...]
            
            [Continue for all sections...]
            
            ### Key Improvements
            - [Major improvement 1]
            - [Major improvement 2]
            - ...
            
            ---
            
            ## Revised Sections to Merge
            
            {revisedSections}
            
            ---
            
            ## Original Paper (for reference)
            
            ```markdown
            {ReviewablePaper.Content}
            ```
            
            ---
            
            [Now merge all sections into one complete paper with revision summary at the end]
            """;
    }
}

// ============================================================
//  Helper: Project Registration
// ============================================================

/// <summary>
/// Helper to create the paper revision project definition.
/// </summary>
public static class PaperReviewProject
{
    /// <summary>
    /// Create a paper revision project with the given paper path.
    /// </summary>
    public static (string Task, Func<MakerOptions> BuildOptions) Create(
        string paperPath,
        string targetVenue = "Top-tier AI Conference",
        string language = "English")
    {
        // Configure the paper
        ReviewablePaper.PaperPath = paperPath;
        ReviewablePaper.TargetVenue = targetVenue;
        ReviewablePaper.Language = language;
        
        var task = $"Revise the paper '{ReviewablePaper.Title}' to publication quality for {targetVenue}. Output the complete revised paper with a summary of all changes.";
        
        Func<MakerOptions> buildOptions = () => new MakerOptions
        {
            Reliability = ReliabilityLevel.High,  // High reliability for quality revision
            Decomposer = new PaperRevisionDecomposer(),
            Solver = new PaperRevisionSolver(),
            Composer = new PaperRevisionComposer(),
            MaxTotalLlmCalls = 150,
            MaxTotalTokens = 1_000_000,  // Papers need more tokens
            Mode = ExecutionMode.Academic,
            Granularity = DecompositionGranularity.Balanced,
            // Multi-provider: auto-discovers all valid providers from config
            UseMultipleProviders = true,
            // Optional: dedicate a specific provider for Coordinator (synthesis)
            // CoordinatorProviderName = "gpt-4",  // Leave null to use round-robin
            Context = new Dictionary<string, string>
            {
                ["venue"] = targetVenue,
                ["language"] = language,
                ["paper_title"] = ReviewablePaper.Title
            }
        };
        
        return (task, buildOptions);
    }
}
