using System.Collections.Concurrent;
using System.Diagnostics;
using Aevatar.Agents.Maker.V2;
using MakerProjectsDemoV2.Projects.Bazi;
using MakerProjectsDemoV2.Projects.Paper;

namespace MakerProjectsDemoV2.Infrastructure;

// ============================================================
//  Project Service - Clean, Simple, ~150 lines
//  Compare to ~400 lines across multiple files in V1
// ============================================================

/// <summary>
/// Project definition.
/// </summary>
public sealed record ProjectDef(
    string Id,
    string Name,
    string Description,
    string Icon,
    Func<MakerOptions> BuildOptions);

/// <summary>
/// Run state for a project.
/// </summary>
public sealed class ProjectRun
{
    public string RunId { get; } = Guid.NewGuid().ToString("N")[..8];
    public string Status { get; set; } = "running";
    public MakerResult? Result { get; set; }
    public List<ProgressEntry> Timeline { get; } = [];
    public HashSet<string> WorkerIds { get; } = [];
    public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
    
    // SSE: Thread-safe event channel
    public System.Threading.Channels.Channel<SSEEvent> EventChannel { get; } = 
        System.Threading.Channels.Channel.CreateUnbounded<SSEEvent>();
    
    // Process files: category -> filename -> content
    public Dictionary<string, Dictionary<string, string>> Files { get; } = new()
    {
        ["proposals"] = new(),      // LLM proposals (decomposition/solution)
        ["votes"] = new(),          // Voting results
        ["artifacts"] = new()       // Stage results and final result
    };
}

/// <summary>
/// File info for frontend.
/// </summary>
public sealed record FileInfo(string Category, string Name, string Path);

public sealed record ProgressEntry(string Phase, string Message, DateTimeOffset Timestamp);

/// <summary>
/// SSE event for real-time streaming.
/// </summary>
public sealed record SSEEvent
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public required string Type { get; init; }  // "progress", "proposal", "voting", "result", "error"
    
    [System.Text.Json.Serialization.JsonPropertyName("taskId")]
    public required string TaskId { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("phase")]
    public string? Phase { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public string? Message { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("content")]
    public string? Content { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("success")]
    public bool? Success { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("depth")]
    public int? Depth { get; init; }
    
    // Voting progress
    [System.Text.Json.Serialization.JsonPropertyName("votingType")]
    public string? VotingType { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("round")]
    public int? Round { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("totalVotes")]
    public int? TotalVotes { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("votesNeeded")]
    public int? VotesNeeded { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("leaderVotes")]
    public int? LeaderVotes { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("runnerUpVotes")]
    public int? RunnerUpVotes { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Main service managing all projects.
/// </summary>
public sealed class MakerProjectService
{
    private readonly IMakerExecutor _executor;
    private readonly ILogger<MakerProjectService> _logger;
    private readonly ConcurrentDictionary<string, ProjectRun> _runs = new();

    private static readonly ProjectDef[] Projects =
    [
        new("bazi", "八字推演", "多 Agent 八字推演，涵盖格局拆解、喜忌分析与报告综合。", "🌓",
            () =>
            {
                var profile = BaziProfile.CreateDemo();
                return new MakerOptions
                {
                    Reliability = ReliabilityLevel.Medium,
                    Decomposer = new BaziDecomposer(),
                    Solver = new BaziSolver(),
                    MaxDepth = 2,
                    Context = new Dictionary<string, string>
                    {
                        ["birth_info"] = profile.SolarBirth,
                        ["bazi"] = $"{profile.LunarYearStemBranch} {profile.LunarMonthStemBranch} {profile.LunarDayStemBranch} {profile.LunarHourStemBranch}"
                    }
                };
            }),
        new("paper", "论文总结", "多 Agent 协作拆解技术论文，生成结构化总结。", "📄",
            () => new MakerOptions
            {
                Reliability = ReliabilityLevel.Medium,
                Decomposer = new PaperDecomposer(),
                Solver = new PaperSolver(),
                MaxDepth = 2
            })
    ];

    public MakerProjectService(IMakerExecutor executor, ILogger<MakerProjectService> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    /// <summary>
    /// Record task tree results as artifacts.
    /// </summary>
    private static void RecordTaskTree(ProjectRun run, TaskNode node, int depth)
    {
        var indent = new string(' ', depth * 2);
        var depthPrefix = depth == 0 ? "root" : $"D{depth}";
        var fileName = $"{depthPrefix}_{node.TaskId}_result.md";
        
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Task Result: {node.TaskId}");
        sb.AppendLine();
        sb.AppendLine($"**Depth:** {depth}  ");
        sb.AppendLine($"**Type:** {(node.IsAtomic ? "Atomic (Solved Directly)" : "Composite (Decomposed)")}  ");
        sb.AppendLine($"**Description:** {node.Description}");
        sb.AppendLine();
        
        // Voting sessions
        if (node.VotingSessions.Count > 0)
        {
            sb.AppendLine("## Voting Sessions");
            sb.AppendLine();
            foreach (var session in node.VotingSessions)
            {
                sb.AppendLine($"### {session.Type} ({session.Rounds} rounds)");
                if (session.Winner != null)
                {
                    sb.AppendLine($"**Winner:** {session.Winner.Votes} votes");
                    sb.AppendLine("```");
                    sb.AppendLine(session.Winner.Content.Length > 500 
                        ? session.Winner.Content[..500] + "..." 
                        : session.Winner.Content);
                    sb.AppendLine("```");
                }
                sb.AppendLine();
            }
        }
        
        // Result
        if (!string.IsNullOrEmpty(node.Result))
        {
            sb.AppendLine("## Result");
            sb.AppendLine();
            sb.AppendLine(node.Result);
        }
        
        // Children
        if (node.Children.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"## Child Tasks ({node.Children.Count})");
            sb.AppendLine();
            foreach (var child in node.Children)
            {
                sb.AppendLine($"- `{child.TaskId}`: {child.Description}");
            }
        }
        
        run.Files["artifacts"][fileName] = sb.ToString();
        
        // Recurse into children
        foreach (var child in node.Children)
        {
            RecordTaskTree(run, child, depth + 1);
        }
    }

    public IEnumerable<object> GetProjects() =>
        Projects.Select(p => new { p.Id, p.Name, p.Description, p.Icon });

    public object? GetStatus(string projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return new { status = "idle", runId = (string?)null };

        return new
        {
            status = run.Status,
            runId = run.RunId,
            elapsed = run.Stopwatch.Elapsed.TotalSeconds
        };
    }

    public object? GetSnapshot(string projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return new { status = "idle" };

        return new
        {
            status = run.Status,
            runId = run.RunId,
            result = run.Result?.Content,
            finalResult = run.Result?.Content,
            success = run.Result?.Success,
            llmCalls = run.Result?.TotalLLMCalls,
            duration = run.Result?.Duration.TotalSeconds,
            workerIds = run.WorkerIds.ToArray()
        };
    }

    public IEnumerable<object> GetTimeline(string projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return [];

        return run.Timeline.Select(e => new { phase = e.Phase, message = e.Message, timestamp = e.Timestamp });
    }

    /// <summary>
    /// Get SSE event stream for a project.
    /// </summary>
    public async IAsyncEnumerable<SSEEvent> GetEventStreamAsync(
        string projectId, 
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            yield break;

        await foreach (var evt in run.EventChannel.Reader.ReadAllAsync(ct))
        {
            yield return evt;
        }
    }

    /// <summary>
    /// Get list of generated files.
    /// </summary>
    public IEnumerable<FileInfo> GetFiles(string projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return [];

        var files = new List<FileInfo>();
        foreach (var (category, fileDict) in run.Files)
        {
            foreach (var name in fileDict.Keys)
            {
                files.Add(new FileInfo(category, name, $"{category}/{name}"));
            }
        }
        return files.OrderBy(f => f.Category).ThenBy(f => f.Name);
    }

    /// <summary>
    /// Get file content.
    /// </summary>
    public string? GetFileContent(string projectId, string category, string name)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return null;

        if (!run.Files.TryGetValue(category, out var files))
            return null;

        return files.GetValueOrDefault(name);
    }

    public async Task<object> StartRunAsync(string projectId, CancellationToken ct)
    {
        var project = Projects.FirstOrDefault(p => p.Id == projectId);
        if (project == null)
            return new { success = false, error = "Project not found" };

        if (_runs.TryGetValue(projectId, out var existing) && existing.Status == "running")
            return new { success = false, error = "Already running", runId = existing.RunId };

        var run = new ProjectRun();
        _runs[projectId] = run;

        _logger.LogInformation("Starting {Project} run {RunId}", projectId, run.RunId);
        run.Timeline.Add(new ProgressEntry("Starting", $"开始执行 {project.Name}", DateTimeOffset.UtcNow));

        // Build task description
        var taskDescription = projectId == "bazi"
            ? BaziProfile.CreateDemo().BuildGoal()
            : $"Summarize the paper '{PaperContent.Title}' by decomposing it into logical sections.";

        var options = project.BuildOptions();
        options = options with
        {
            OnProgress = p =>
            {
                // Progress log
                run.Timeline.Add(new ProgressEntry(p.Phase.ToString(), p.Message, p.Timestamp));
                _logger.LogInformation("[{Project}] {Phase}: {Message}", projectId, p.Phase, p.Message);
                
                // SSE: progress event (for STRATEGIC_PLANNING)
                run.EventChannel.Writer.TryWrite(new SSEEvent
                {
                    Type = "progress",
                    TaskId = p.TaskId,
                    Phase = p.Phase.ToString(),
                    Message = p.Message,
                    Depth = p.Depth,
                    Timestamp = p.Timestamp
                });
                
                // SSE: voting event (for CONSENSUS_PROTOCOL)
                if (p.Voting != null)
                {
                    run.EventChannel.Writer.TryWrite(new SSEEvent
                    {
                        Type = "voting",
                        TaskId = p.TaskId,
                        VotingType = p.Voting.Type.ToString(),
                        Round = p.Voting.Round,
                        TotalVotes = p.Voting.TotalVotes,
                        VotesNeeded = p.Voting.VotesNeeded,
                        LeaderVotes = p.Voting.LeaderVotes,
                        RunnerUpVotes = p.Voting.RunnerUpVotes,
                        Timestamp = p.Timestamp
                    });
                    
                    // Record voting state to file
                    var voteFileName = $"vote_{p.TaskId}_{p.Voting.Type}_R{p.Voting.Round}.md";
                    var voteContent = $"""
                        # Voting: {p.Voting.Type} - Round {p.Voting.Round}
                        
                        **Task:** `{p.TaskId}`  
                        **Depth:** {p.Depth}  
                        **Time:** {p.Timestamp:HH:mm:ss}
                        
                        ## Progress
                        - Total Votes: {p.Voting.TotalVotes}
                        - Leader: {p.Voting.LeaderVotes} votes
                        - Runner-up: {p.Voting.RunnerUpVotes} votes
                        - Need lead by: {p.Voting.VotesNeeded}
                        - Current gap: {p.Voting.LeaderVotes - p.Voting.RunnerUpVotes}
                        """;
                    run.Files["votes"][voteFileName] = voteContent;
                }
                
                // SSE: proposal event (LLM response for SYSTEM_NODES)
                if (p.Proposal != null)
                {
                    var workerId = $"{p.TaskId}:{p.Proposal.ProposalId}";
                    run.WorkerIds.Add(workerId);
                    
                    run.EventChannel.Writer.TryWrite(new SSEEvent
                    {
                        Type = "proposal",
                        TaskId = workerId,
                        Content = p.Proposal.Content,
                        Success = p.Proposal.Success,
                        Message = p.Proposal.Error,
                        Timestamp = p.Timestamp
                    });
                    
                    // Determine proposal type from ID (D=Decomposition, S=Solution)
                    var proposalType = p.Proposal.ProposalId.StartsWith("D") ? "decomposition" : "solution";
                    var fileName = $"{proposalType}_{p.TaskId}_{p.Proposal.ProposalId}.md";
                    
                    var content = $"""
                        # {proposalType.ToUpperInvariant()} Proposal: {p.Proposal.ProposalId}
                        
                        **Task:** `{p.TaskId}`  
                        **Depth:** {p.Depth}  
                        **Status:** {(p.Proposal.Success ? "✓ Success" : "✗ Failed")}  
                        **Time:** {p.Timestamp:HH:mm:ss}
                        
                        ## Content
                        
                        ```
                        {p.Proposal.Content ?? p.Proposal.Error ?? "No content"}
                        ```
                        """;
                    run.Files["proposals"][fileName] = content;
                    
                    // Send file event
                    run.EventChannel.Writer.TryWrite(new SSEEvent
                    {
                        Type = "file",
                        TaskId = fileName,
                        Phase = "proposals",
                        Message = $"New {proposalType} proposal",
                        Timestamp = p.Timestamp
                    });
                }
            }
        };

        // Run in background - DON'T use the HTTP request's cancellation token!
        // The HTTP request completes immediately, but the MAKER execution continues.
        _ = Task.Run(async () =>
        {
            try
            {
                run.Result = await _executor.ExecuteAsync(taskDescription, options, CancellationToken.None);
                run.Status = run.Result.Success ? "completed" : "failed";
                
                _logger.LogInformation("[{Project}] Execution finished. Success={Success}, ContentLength={Length}", 
                    projectId, run.Result.Success, run.Result.Content?.Length ?? 0);
                
                // Record final result as artifact
                if (!string.IsNullOrEmpty(run.Result.Content))
                {
                    var finalContent = $"""
                        # Final Report
                        
                        **Project:** {project.Name}  
                        **Run ID:** {run.RunId}  
                        **Status:** {(run.Result.Success ? "✓ Success" : "✗ Failed")}  
                        **Duration:** {run.Stopwatch.Elapsed.TotalSeconds:F1}s  
                        **LLM Calls:** {run.Result.TotalLLMCalls}
                        
                        ---
                        
                        {run.Result.Content}
                        """;
                    run.Files["artifacts"]["final_report.md"] = finalContent;
                    
                    // Send file event
                    run.EventChannel.Writer.TryWrite(new SSEEvent
                    {
                        Type = "file",
                        TaskId = "final_report.md",
                        Phase = "artifacts",
                        Message = "Final report generated",
                        Timestamp = DateTimeOffset.UtcNow
                    });
                }
                
                // Record execution trace
                if (run.Result.Trace.RootTask != null)
                {
                    RecordTaskTree(run, run.Result.Trace.RootTask, 0);
                }
                
                run.Timeline.Add(new ProgressEntry(
                    run.Status,
                    run.Result.Success ? "执行完成" : $"执行失败: {run.Result.Error}",
                    DateTimeOffset.UtcNow));
            }
            catch (Exception ex)
            {
                run.Status = "failed";
                run.Timeline.Add(new ProgressEntry("Failed", ex.Message, DateTimeOffset.UtcNow));
                _logger.LogError(ex, "Run failed for {Project}", projectId);
                
                // Send error event
                run.EventChannel.Writer.TryWrite(new SSEEvent
                {
                    Type = "error",
                    TaskId = run.RunId,
                    Message = ex.Message,
                    Success = false
                });
            }
            finally
            {
                run.Stopwatch.Stop();
                
                // Send final result event
                if (run.Result != null)
                {
                    run.EventChannel.Writer.TryWrite(new SSEEvent
                    {
                        Type = "result",
                        TaskId = run.RunId,
                        Content = run.Result.Content,
                        Success = run.Result.Success,
                        Message = run.Result.Error
                    });
                }
                
                // Complete the channel
                run.EventChannel.Writer.Complete();
            }
        }, CancellationToken.None);

        return new { success = true, runId = run.RunId };
    }
}

