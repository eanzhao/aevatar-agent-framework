using System.Diagnostics;
using System.Linq;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Hierarchy;
using Aevatar.Agents.Maker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MakerProjectsDemo.Infrastructure;

public interface IMakerProjectRunner
{
    MakerProjectInfo Info { get; }
    Task<MakerRunResponse> StartRunAsync(CancellationToken cancellationToken = default);
    MakerRunStatus GetStatus();
    MakerSnapshotDto GetSnapshot();
    IReadOnlyList<TimelineEventDto> GetTimeline();
}

public sealed class MakerProjectRunner : IMakerProjectRunner
{
    private readonly MakerProjectSpec _spec;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MakerTimelineHub _timelineHub;
    private readonly IMakerProjectContextAccessor _contextAccessor;
    private readonly ILogger _logger;
    private readonly object _syncRoot = new();

    private Task? _currentRun;
    private string? _currentRunId;
    private CancellationTokenSource? _runCts;
    private MakerSnapshotDto _latestSnapshot = MakerSnapshotDto.Empty;
    private List<string> _activeWorkerIds = new();

    public MakerProjectRunner(
        MakerProjectSpec spec,
        IServiceScopeFactory scopeFactory,
        MakerTimelineHub timelineHub,
        IMakerProjectContextAccessor contextAccessor,
        ILoggerFactory loggerFactory)
    {
        _spec = spec;
        _scopeFactory = scopeFactory;
        _timelineHub = timelineHub;
        _contextAccessor = contextAccessor;
        _logger = loggerFactory.CreateLogger($"{typeof(MakerProjectRunner).FullName}.{spec.Id}");
    }

    public MakerProjectInfo Info => new(_spec.Id, _spec.Name, _spec.Description, _spec.Icon);

    public Task<MakerRunResponse> StartRunAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            if (_currentRun is { IsCompleted: false })
            {
                return Task.FromResult(MakerRunResponse.AlreadyRunning(_currentRunId!));
            }

            var runId = $"run-{Guid.NewGuid():N}";
            _currentRunId = runId;
            _timelineHub.Clear(_spec.Id);
            var timeline = _timelineHub.GetStore(_spec.Id);
            timeline.Append("info", "orchestrator", $"启动 {runId}");
            _latestSnapshot = MakerSnapshotDto.Empty with { Status = "running", RunId = runId, ProjectId = _spec.Id };
            _runCts?.Dispose();
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _currentRun = Task.Run(() => ExecuteRunAsync(runId, _runCts.Token), CancellationToken.None);
            return Task.FromResult(MakerRunResponse.Started(runId));
        }
    }

    public MakerRunStatus GetStatus()
    {
        var running = _currentRun is { IsCompleted: false };
        return new MakerRunStatus(running ? "running" : "idle", _currentRunId);
    }

    public MakerSnapshotDto GetSnapshot() => _latestSnapshot;

    public IReadOnlyList<TimelineEventDto> GetTimeline() => _timelineHub.GetEvents(_spec.Id);

    private async Task ExecuteRunAsync(string runId, CancellationToken cancellationToken)
    {
        using var projectScope = _contextAccessor.Push(_spec.Id);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var factory = services.GetRequiredService<IGAgentActorFactory>();
            var timeline = _timelineHub.GetStore(_spec.Id);
            var rootTaskId = $"{_spec.Id}-root-{Guid.NewGuid():N}";

            var runContext = new MakerProjectRunContext(_spec.Id, runId, rootTaskId, timeline, services);
            _spec.OnRunStarting?.Invoke(runContext);

            var taskActor = await _spec.TaskFactory(factory, cancellationToken);

            var workers = new List<IGAgentActor>();
            for (var i = 0; i < _spec.WorkerCount; i++)
            {
                var worker = await _spec.WorkerFactory(factory, cancellationToken);
                await ActorHierarchyCoordinator.LinkAsync(taskActor, worker, _logger, cancellationToken);
                workers.Add(worker);
            }
            _activeWorkerIds = workers.Select(w => w.Id.ToString()).ToList();

            var assignEvent = _spec.BuildRootAssignTask(runContext);
            await taskActor.PublishEventAsync(assignEvent, EventDirection.Down, cancellationToken);

            if (taskActor.GetAgent() is not MakerTaskAgent taskAgent)
            {
                throw new InvalidOperationException("MakerTaskAgent instance is required.");
            }

            await ObserveAsync(taskAgent, cancellationToken);

            foreach (var worker in workers)
            {
                await worker.DeactivateAsync(cancellationToken);
            }

            await taskActor.DeactivateAsync(cancellationToken);

            timeline.Append("info", "orchestrator", "运行结束");
            _spec.OnRunCompleted?.Invoke(runContext);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _timelineHub.GetStore(_spec.Id).Append("warning", "orchestrator", "运行被取消。");
        }
        catch (Exception ex)
        {
            _timelineHub.GetStore(_spec.Id).Append("error", "orchestrator", ex.Message);
            _logger.LogError(ex, "Project {ProjectId} run failed.", _spec.Id);
        }
        finally
        {
            lock (_syncRoot)
            {
                _currentRun = null;
                _runCts?.Dispose();
                _runCts = null;
                _latestSnapshot = _latestSnapshot with { Status = "idle" };
                _activeWorkerIds = new List<string>();
            }
        }
    }

    private async Task ObserveAsync(MakerTaskAgent agent, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var timeline = _timelineHub.GetStore(_spec.Id);

        while (!cancellationToken.IsCancellationRequested)
        {
            var state = agent.GetCustomState();
            UpdateSnapshot(state, timeline);

            if (state.Phase is TaskAgentState.Types.Phase.Completed or TaskAgentState.Types.Phase.Failed)
            {
                timeline.Append("info", "orchestrator", $"任务结束：{state.Phase}");
                if (!string.IsNullOrWhiteSpace(state.FinalResult))
                {
                    timeline.Append("info", "result", state.FinalResult);
                }
                break;
            }

            if (sw.Elapsed > TimeSpan.FromMinutes(60))
            {
                timeline.Append("warning", "orchestrator", "运行超时，自动停止。");
                break;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private void UpdateSnapshot(TaskAgentState state, MakerTimelineStore timeline)
    {
        var snapshot = MakerSnapshotDto.FromState(
            _spec.Id,
            state,
            _currentRunId,
            _currentRun is { IsCompleted: false } ? "running" : "idle",
            timeline.Count,
            _activeWorkerIds);

        _latestSnapshot = snapshot;
    }
}

