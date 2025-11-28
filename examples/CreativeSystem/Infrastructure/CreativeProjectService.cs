using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Aevatar.Agents.CreativeReasoning.Core;
using Aevatar.Agents.CreativeReasoning.Execution;
using Aevatar.Agents.CreativeReasoning.Messages;
using Microsoft.Extensions.Logging;

namespace CreativeSystem.Infrastructure;

// ============================================================
//  Creative Project Service
//  Manages C-UoT creative reasoning execution with real-time events
// ============================================================

public record CreativeSolveRequest(
    string Problem,
    string? DomainHint = null,
    int MaxAnalogies = 5,
    int MaxCandidates = 10,
    float FeasibilityThreshold = 0.6f,
    float UtilityWeight = 0.5f,
    float NoveltyWeight = 0.5f);

public record SampleProblem(
    string Id,
    string Title,
    string Problem,
    string DomainHint,
    string Category);

public record RunStatus(
    string RunId,
    string Status,
    string Phase,
    float Progress,
    int AnalogiesFound,
    int ThoughtsExtracted,
    int CandidatesGenerated,
    int CandidatesPassed,
    int LlmCalls,
    long TotalTokens,
    string? Error);

public record RunResult(
    string RunId,
    bool Success,
    CandidateSolution? BestSolution,
    IReadOnlyList<CandidateSolution> AllCandidates,
    UoTResultTrace Trace);

public record SSEEvent
{
    public required string Type { get; init; }  // "progress", "analogy", "thought", "candidate", "result", "error"
    public required string RunId { get; init; }
    public string? Phase { get; init; }
    public string? Message { get; init; }
    public float? Progress { get; init; }
    public int? AnalogiesFound { get; init; }
    public int? ThoughtsExtracted { get; init; }
    public int? CandidatesGenerated { get; init; }
    public int? CandidatesPassed { get; init; }
    public object? Data { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public class CreativeRun
{
    public string RunId { get; } = Guid.NewGuid().ToString("N")[..8];
    public string Status { get; set; } = "running";
    public string Phase { get; set; } = "Starting";
    public float Progress { get; set; }
    public int AnalogiesFound { get; set; }
    public int ThoughtsExtracted { get; set; }
    public int CandidatesGenerated { get; set; }
    public int CandidatesPassed { get; set; }
    public int LlmCalls { get; set; }
    public long TotalTokens { get; set; }
    public string? Error { get; set; }
    public UoTResult? Result { get; set; }
    public Channel<SSEEvent> EventChannel { get; } = Channel.CreateUnbounded<SSEEvent>();
}

public class CreativeProjectService
{
    private readonly IUoTExecutor _executor;
    private readonly ILogger<CreativeProjectService> _logger;
    private readonly ConcurrentDictionary<string, CreativeRun> _runs = new();

    private static readonly List<SampleProblem> SampleProblems =
    [
        new("bridge", "Bridge Traffic Problem",
            "Design a mechanism for managing two-way traffic on a single-lane bridge where vehicles from both directions need to cross safely without collision.",
            "transportation, distributed systems, resource scheduling",
            "Engineering"),
        
        new("bookstore", "Bookstore Revival",
            "How can a traditional physical bookstore regain growth and relevance in the era of e-commerce and digital books?",
            "retail, business strategy, community",
            "Business"),
        
        new("fitness", "Senior Fitness App",
            "Design an innovative fitness app feature specifically for users aged 60+ that encourages regular physical activity while being safe and engaging.",
            "health, gamification, accessibility",
            "Product"),
        
        new("startup", "Innovative Drink Startup",
            "Develop a unique beverage product concept that addresses an unmet consumer need and can compete in the crowded beverage market.",
            "food, consumer goods, innovation",
            "Business"),
        
        new("collaboration", "Remote Team Collaboration",
            "Design a new approach to remote team collaboration that solves the problem of 'Zoom fatigue' while maintaining team cohesion and productivity.",
            "workplace, communication, psychology",
            "Workplace"),
        
        new("sustainability", "Urban Sustainability",
            "Propose an innovative solution for reducing food waste in urban residential buildings while creating value for residents.",
            "environment, urban planning, community",
            "Environment")
    ];

    public CreativeProjectService(IUoTExecutor executor, ILogger<CreativeProjectService> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    public IReadOnlyList<SampleProblem> GetSampleProblems() => SampleProblems;

    public async Task<object> StartSolveAsync(CreativeSolveRequest request, CancellationToken ct)
    {
        var run = new CreativeRun();
        _runs[run.RunId] = run;

        _logger.LogInformation("Starting C-UoT run {RunId} for problem: {Problem}",
            run.RunId, request.Problem[..Math.Min(50, request.Problem.Length)]);

        // Start execution in background
        _ = ExecuteAsync(run, request, ct);

        return new { runId = run.RunId, status = "started" };
    }

    private async Task ExecuteAsync(CreativeRun run, CreativeSolveRequest request, CancellationToken ct)
    {
        try
        {
            // Build UoT options with progress callback
            var options = new UoTOptions
            {
                ProviderName = "claude",
                DomainHint = request.DomainHint,
                MaxAnalogies = request.MaxAnalogies,
                MaxCandidates = request.MaxCandidates,
                FeasibilityThreshold = request.FeasibilityThreshold,
                UtilityWeight = request.UtilityWeight,
                NoveltyWeight = request.NoveltyWeight,
                OnProgress = progress =>
                {
                    run.Phase = progress.Phase.ToString();
                    run.Progress = progress.ProgressPercent;
                    run.AnalogiesFound = progress.AnalogiesFound > 0 ? progress.AnalogiesFound : run.AnalogiesFound;
                    run.ThoughtsExtracted = progress.ThoughtsExtracted > 0 ? progress.ThoughtsExtracted : run.ThoughtsExtracted;
                    run.CandidatesGenerated = progress.CandidatesGenerated > 0 ? progress.CandidatesGenerated : run.CandidatesGenerated;
                    run.CandidatesPassed = progress.CandidatesPassed > 0 ? progress.CandidatesPassed : run.CandidatesPassed;

                    run.EventChannel.Writer.TryWrite(new SSEEvent
                    {
                        Type = "progress",
                        RunId = run.RunId,
                        Phase = progress.Phase.ToString(),
                        Message = progress.Message,
                        Progress = progress.ProgressPercent,
                        AnalogiesFound = run.AnalogiesFound,
                        ThoughtsExtracted = run.ThoughtsExtracted,
                        CandidatesGenerated = run.CandidatesGenerated,
                        CandidatesPassed = run.CandidatesPassed
                    });
                }
            };

            // Execute via IUoTExecutor (uses IGAgentActorFactory internally)
            var result = await _executor.ExecuteAsync(request.Problem, options, ct);

            // Update run state
            run.Result = result;
            run.Status = result.Success ? "completed" : "failed";
            run.LlmCalls = result.Trace.TotalLLMCalls;
            run.TotalTokens = result.Trace.TotalTokens;

            if (!result.Success)
            {
                run.Error = result.Error;
            }

            // Send final event
            run.EventChannel.Writer.TryWrite(new SSEEvent
            {
                Type = result.Success ? "result" : "error",
                RunId = run.RunId,
                Phase = "Completed",
                Message = result.Success
                    ? $"Creative reasoning complete! Found {result.AllCandidates.Count} solutions."
                    : $"Failed: {result.Error}",
                Progress = 1.0f,
                Data = result.Success ? new
                {
                    bestScore = result.BestSolution?.Score?.Composite,
                    candidateCount = result.AllCandidates.Count
                } : null
            });

            run.EventChannel.Writer.Complete();

            _logger.LogInformation("C-UoT run {RunId} completed: Success={Success}, Candidates={Count}",
                run.RunId, result.Success, result.AllCandidates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "C-UoT run {RunId} failed with exception", run.RunId);
            run.Status = "failed";
            run.Error = ex.Message;

            run.EventChannel.Writer.TryWrite(new SSEEvent
            {
                Type = "error",
                RunId = run.RunId,
                Phase = "Failed",
                Message = ex.Message
            });

            run.EventChannel.Writer.Complete();
        }
    }

    public RunStatus GetStatus(string runId)
    {
        if (!_runs.TryGetValue(runId, out var run))
        {
            return new RunStatus(runId, "not_found", "", 0, 0, 0, 0, 0, 0, 0, "Run not found");
        }

        return new RunStatus(
            run.RunId,
            run.Status,
            run.Phase,
            run.Progress,
            run.AnalogiesFound,
            run.ThoughtsExtracted,
            run.CandidatesGenerated,
            run.CandidatesPassed,
            run.LlmCalls,
            run.TotalTokens,
            run.Error);
    }

    public RunResult? GetResult(string runId)
    {
        if (!_runs.TryGetValue(runId, out var run) || run.Result == null)
        {
            return null;
        }

        return new RunResult(
            run.RunId,
            run.Result.Success,
            run.Result.BestSolution,
            run.Result.AllCandidates,
            run.Result.Trace);
    }

    public async IAsyncEnumerable<SSEEvent> GetEventStreamAsync(
        string runId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_runs.TryGetValue(runId, out var run))
        {
            yield return new SSEEvent
            {
                Type = "error",
                RunId = runId,
                Message = "Run not found"
            };
            yield break;
        }

        await foreach (var evt in run.EventChannel.Reader.ReadAllAsync(ct))
        {
            yield return evt;
        }
    }
}

