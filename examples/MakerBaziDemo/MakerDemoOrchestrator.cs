using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Maker;
using Aevatar.Agents.Core.Hierarchy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MakerBaziDemo;

public sealed class MakerDemoOrchestrator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MakerTimelineStore _timeline;
    private readonly ILogger<MakerDemoOrchestrator> _logger;
    private readonly object _syncRoot = new();

    private Task? _currentRun;
    private string? _currentRunId;
    private MakerSnapshotDto _latestSnapshot = MakerSnapshotDto.Empty;
    private List<string> _activeWorkerIds = new();

    public MakerDemoOrchestrator(
        IServiceScopeFactory scopeFactory,
        MakerTimelineStore timeline,
        ILogger<MakerDemoOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _timeline = timeline;
        _logger = logger;
    }

    public async Task<MakerRunResponse> StartRunAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            if (_currentRun is { IsCompleted: false })
            {
                return MakerRunResponse.AlreadyRunning(_currentRunId!);
            }

            _timeline.Clear();
            _currentRunId = $"run-{Guid.NewGuid():N}";
            _timeline.Append("info", "orchestrator", $"启动 { _currentRunId }");
            _latestSnapshot = MakerSnapshotDto.Empty with { Status = "running", RunId = _currentRunId };

            _currentRun = Task.Run(() => ExecuteRunAsync(_currentRunId, cancellationToken), cancellationToken);
            return MakerRunResponse.Started(_currentRunId);
        }
    }

    public MakerRunStatus GetStatus()
    {
        var running = _currentRun is { IsCompleted: false };
        return new MakerRunStatus(running ? "running" : "idle", _currentRunId);
    }

    public MakerSnapshotDto GetSnapshot() => _latestSnapshot;

    private async Task ExecuteRunAsync(string runId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IGAgentActorFactory>();

            var taskId = Guid.NewGuid();
            var taskActor = await factory.CreateGAgentActorAsync<BaziMakerTaskAgent>(taskId, cancellationToken);
            var workers = new List<IGAgentActor>();
            for (var i = 0; i < 4; i++)
            {
                var worker = await factory.CreateGAgentActorAsync<BaziMakerWorkerAgent>(Guid.NewGuid(), cancellationToken);
                await ActorHierarchyCoordinator.LinkAsync(taskActor, worker, _logger, cancellationToken);
                workers.Add(worker);
            }
            _activeWorkerIds = workers.Select(w => w.Id.ToString()).ToList();

            var profile = BaziProfile.CreateDemoProfile();
            _timeline.Append("info", "profile", $"主题：{profile.FocusTopic}");

            var goal = BaziGoalBuilder.BuildGoal(profile);
            var runtimeTaskId = $"bazi-root-{Guid.NewGuid():N}";

            await taskActor.PublishEventAsync(new AssignTaskEvent
            {
                TaskId = runtimeTaskId,
                GoalDescription = goal,
                CurrentDepth = 0,
                ContextVariables =
                {
                    { "analysis_topic", "bazi_report" },
                    { "birth_city", profile.Birthplace },
                    { "lunar_day", profile.LunarDayStemBranch }
                }
            }, EventDirection.Down, cancellationToken);

            var taskAgent = (BaziMakerTaskAgent)taskActor.GetAgent();
            await ObserveAsync(taskAgent, cancellationToken);

            foreach (var worker in workers)
            {
                await worker.DeactivateAsync(cancellationToken);
            }

            await taskActor.DeactivateAsync(cancellationToken);

            _timeline.Append("info", "orchestrator", "运行结束");
        }
        catch (Exception ex)
        {
            _timeline.Append("error", "orchestrator", ex.Message);
            _logger.LogError(ex, "Maker demo run failed.");
        }
        finally
        {
            lock (_syncRoot)
            {
                _currentRun = null;
                _latestSnapshot = _latestSnapshot with { Status = "idle" };
                _activeWorkerIds = new List<string>();
            }
        }
    }

    private async Task ObserveAsync(BaziMakerTaskAgent agent, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        while (!cancellationToken.IsCancellationRequested)
        {
            var state = agent.GetCustomState();
            UpdateSnapshot(state);

            if (state.Phase is TaskAgentState.Types.Phase.Completed or TaskAgentState.Types.Phase.Failed)
            {
                _timeline.Append("info", "orchestrator", $"任务结束：{state.Phase}");
                if (!string.IsNullOrWhiteSpace(state.FinalResult))
                {
                    _timeline.Append("info", "result", state.FinalResult);
                }
                break;
            }

            if (sw.Elapsed > TimeSpan.FromMinutes(5))
            {
                _timeline.Append("warning", "orchestrator", "运行超时，自动停止。");
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private void UpdateSnapshot(TaskAgentState state)
    {
        var snapshot = MakerSnapshotDto.FromState(
            state,
            runId: _currentRunId,
            status: _currentRun is { IsCompleted: false } ? "running" : "idle",
            timelineCount: _timeline.Count,
            workerIds: _activeWorkerIds);

        _latestSnapshot = snapshot;
    }
}

public sealed record MakerRunResponse(string RunId, string Status)
{
    public static MakerRunResponse Started(string runId) => new(runId, "started");
    public static MakerRunResponse AlreadyRunning(string runId) => new(runId, "running");
}

public sealed record MakerRunStatus(string Status, string? RunId);

public sealed record MakerSnapshotDto
{
    public static readonly MakerSnapshotDto Empty = new()
    {
        Status = "idle",
        RunId = string.Empty,
        TaskId = string.Empty,
        Phase = "Unknown",
        CurrentDepth = 0,
        PendingChildren = [],
        CompletedChildren = [],
        VoteTallies = new(),
        CandidatePreviews = new(),
        MicroObjective = string.Empty,
        MicroCursor = 0,
        MicroTotal = 0,
        MicroObjectives = [],
        PlannedSteps = [],
        WorkerIds = [],
        FinalResult = string.Empty,
        TimelineCount = 0
    };

    public string Status { get; init; } = "idle";
    public string RunId { get; init; } = string.Empty;
    public string TaskId { get; init; } = string.Empty;
    public string Phase { get; init; } = "Unknown";
    public int CurrentDepth { get; init; }
    public List<string> PendingChildren { get; init; } = [];
    public List<string> CompletedChildren { get; init; } = [];
    public Dictionary<string, int> VoteTallies { get; init; } = new();
    public Dictionary<string, string> CandidatePreviews { get; init; } = new();
    public string MicroObjective { get; init; } = string.Empty;
    public int MicroCursor { get; init; }
    public int MicroTotal { get; init; }
    public List<string> MicroObjectives { get; init; } = [];
    public List<PlannedStepDto> PlannedSteps { get; init; } = [];
    public List<string> WorkerIds { get; init; } = [];
    public string FinalResult { get; init; } = string.Empty;
    public int TimelineCount { get; init; }

    public static MakerSnapshotDto FromState(
        TaskAgentState state,
        string? runId,
        string status,
        int timelineCount,
        IEnumerable<string>? workerIds = null)
    {
        var voteTallies = state.VoteTallies?.ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? new Dictionary<string, int>();
        var candidatePreviews = state.CandidateContent?
            .ToDictionary(pair => pair.Key, pair => BuildPreview(pair.Value)) ?? new Dictionary<string, string>();

        return new MakerSnapshotDto
        {
            Status = status,
            RunId = runId ?? string.Empty,
            TaskId = state.TaskId,
            Phase = state.Phase.ToString(),
            CurrentDepth = state.CurrentDepth,
            PendingChildren = state.PendingChildIds.ToList(),
            CompletedChildren = state.ChildResults.Keys.OrderBy(x => x).ToList(),
            VoteTallies = voteTallies,
            CandidatePreviews = candidatePreviews,
            MicroObjective = state.MicroCurrentObjective,
            MicroCursor = state.MicroCursor,
            MicroTotal = state.MicroObjectives.Count,
            MicroObjectives = state.MicroObjectives.ToList(),
            PlannedSteps = state.PlannedSteps.Select(p => new PlannedStepDto(p.StepId, p.Description)).ToList(),
            WorkerIds = workerIds?.ToList() ?? [],
            FinalResult = state.FinalResult,
            TimelineCount = timelineCount
        };
    }

    private static string BuildPreview(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "[empty]";
        }

        var normalized = content.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 160
            ? normalized
            : normalized[..160] + "...";
    }

    public sealed record PlannedStepDto(string StepId, string Description);
}

