namespace Aevatar.Agents.Maker.Examples;

// ============================================================
//  Usage Examples - How Simple the API Is
// ============================================================

/// <summary>
/// Example usage patterns for MAKER V2.
/// </summary>
public static class UsageExamples
{
    /// <summary>
    /// Example 1: Zero-config usage with defaults.
    /// </summary>
    public static async Task<MakerResult> SimpleUsageAsync(IMakerExecutor maker)
    {
        // That's it! No inheritance, no configuration, no actor management.
        return await maker.ExecuteAsync("Analyze the key contributions of this research paper.");
    }

    /// <summary>
    /// Example 2: Custom reliability level.
    /// </summary>
    public static async Task<MakerResult> HighReliabilityAsync(IMakerExecutor maker)
    {
        return await maker.ExecuteAsync(
            "Calculate the optimal route for this logistics problem.",
            new MakerOptions
            {
                Reliability = ReliabilityLevel.High  // K=3, N=5
            });
    }

    /// <summary>
    /// Example 4: Paper summary with content injection.
    /// </summary>
    public static async Task<MakerResult> PaperSummaryAsync(IMakerExecutor maker, string paperContent)
    {
        return await maker.ExecuteAsync(
            "Summarize the paper by decomposing it into logical sections.",
            new MakerOptions
            {
                Reliability = ReliabilityLevel.Medium,
                Decomposer = new PaperDecomposer(),
                Solver = new PaperSolver(paperContent)
            });
    }

    /// <summary>
    /// Example 5: With progress callback.
    /// </summary>
    public static async Task<MakerResult> WithProgressAsync(
        IMakerExecutor maker,
        Action<string> log)
    {
        return await maker.ExecuteAsync(
            "Design a marketing strategy for a new product launch.",
            new MakerOptions
            {
                OnProgress = progress =>
                {
                    log($"[{progress.Phase}] {progress.Message}");
                    if (progress.Voting != null)
                    {
                        log($"  Voting: {progress.Voting.LeaderVotes}/{progress.Voting.VotesNeeded} consensus");
                    }
                }
            });
    }

    /// <summary>
    /// Example 6: Full trace inspection.
    /// </summary>
    public static void InspectTrace(MakerResult result)
    {
        Console.WriteLine($"Success: {result.Success}");
        Console.WriteLine($"Total LLM Calls: {result.TotalLLMCalls}");
        Console.WriteLine($"Duration: {result.Duration.TotalSeconds:F1}s");
        
        if (result.Trace.RedFlags.Count > 0)
        {
            Console.WriteLine("Red Flags:");
            foreach (var flag in result.Trace.RedFlags)
            {
                Console.WriteLine($"  - {flag.TaskId}: {flag.Reason} (Recovered: {flag.Recovered})");
            }
        }

        PrintTaskTree(result.Trace.RootTask, 0);
    }

    private static void PrintTaskTree(TaskNode node, int indent)
    {
        var prefix = new string(' ', indent * 2);
        Console.WriteLine($"{prefix}- {node.TaskId}: {(node.IsAtomic ? "ATOMIC" : "DECOMPOSED")}");
        
        foreach (var session in node.VotingSessions)
        {
            Console.WriteLine($"{prefix}  Vote ({session.Type}): {session.Winner?.Votes ?? 0} votes, {session.Rounds} rounds");
        }

        foreach (var child in node.Children)
        {
            PrintTaskTree(child, indent + 1);
        }
    }
}

