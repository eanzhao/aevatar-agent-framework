using System.Linq;

namespace MakerProjectsDemo.Infrastructure;

public sealed class MakerProjectsService
{
    private readonly IReadOnlyDictionary<string, IMakerProjectRunner> _runners;

    public MakerProjectsService(IEnumerable<IMakerProjectRunner> runners)
    {
        _runners = runners.ToDictionary(r => r.Info.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<MakerProjectInfo> GetProjects() => _runners.Values
        .Select(r => r.Info)
        .OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public Task<MakerRunResponse> StartRunAsync(string projectId, CancellationToken cancellationToken = default)
    {
        return GetRunner(projectId).StartRunAsync(cancellationToken);
    }

    public MakerRunStatus GetStatus(string projectId) => GetRunner(projectId).GetStatus();
    public MakerSnapshotDto GetSnapshot(string projectId) => GetRunner(projectId).GetSnapshot();
    public IReadOnlyList<TimelineEventDto> GetTimeline(string projectId) => GetRunner(projectId).GetTimeline();

    private IMakerProjectRunner GetRunner(string projectId)
    {
        if (!_runners.TryGetValue(projectId, out var runner))
        {
            throw new KeyNotFoundException($"未知项目：{projectId}");
        }

        return runner;
    }
}

