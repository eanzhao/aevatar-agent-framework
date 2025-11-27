namespace Aevatar.Agents.Maker.Examples;

// ============================================================
//  Example: Paper Summary Decomposer
//  Clean, focused, minimal code.
// ============================================================

/// <summary>
/// Paper summary decomposition strategy.
/// </summary>
public sealed class PaperDecomposer : IDecompositionStrategy
{
    /// <inheritdoc />
    public string BuildDecompositionPrompt(string taskDescription, IReadOnlyDictionary<string, string> context)
    {
        var jsonFormat = """[{"step_id":"S1","description":"Summarize Section X: ..."}]""";
        return $"""
            You are a Senior Editor at a scientific journal. Decompose the paper summary task into 4-6 logical sections.
            
            Typical sections:
            1. Abstract/Introduction summary
            2. Methodology summary
            3. Key findings/Results
            4. Discussion/Implications
            5. Limitations and Future Work
            
            Output: JSON array {jsonFormat}
            Output ONLY JSON.
            
            [Task]
            {taskDescription}
            """;
    }

    /// <inheritdoc />
    public bool IsAtomic(string taskDescription, int currentDepth)
    {
        // Paper summary: 1 level is enough (decompose into sections → summarize each)
        return currentDepth >= 1;
    }

    /// <inheritdoc />
    public IReadOnlyList<(string StepId, string Description)> ParseDecomposition(string llmOutput)
    {
        return new DefaultDecomposer().ParseDecomposition(llmOutput);
    }
}

/// <summary>
/// Paper summary solution strategy with full paper content injection.
/// </summary>
public sealed class PaperSolver : ISolutionStrategy
{
    private readonly string _paperContent;

    /// <summary>
    /// Create a paper solver with the full paper text.
    /// </summary>
    /// <param name="paperContent">The full paper content.</param>
    public PaperSolver(string paperContent)
    {
        _paperContent = paperContent;
    }

    /// <inheritdoc />
    public string BuildSolvePrompt(string taskDescription, IReadOnlyDictionary<string, string> context)
    {
        return $"""
            You are a Research Assistant. Summarize the specific section requested.
            
            Instructions:
            1. Read the task description to identify the target section.
            2. Locate that section in the paper text below.
            3. Write a concise summary (100-150 words).
            4. Use markdown formatting.
            5. Do NOT use a global "# Summary" heading.
            
            --- PAPER CONTENT ---
            {_paperContent}
            --- END CONTENT ---
            
            [Task]
            {taskDescription}
            
            [Summary]
            """;
    }
}

