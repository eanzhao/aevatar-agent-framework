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

    public IReadOnlyList<string> GetRunFiles(string projectId)
    {
        var baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runs");
        if (!Directory.Exists(baseDir))
        {
            return Array.Empty<string>();
        }

        // Find the latest date folder
        var dateDir = Directory.GetDirectories(baseDir)
            .OrderByDescending(d => d)
            .FirstOrDefault();

        if (dateDir == null) return Array.Empty<string>();

        // Find the project folder inside (it might be nested or named with ID)
        // Updated Logic: We need to match exactly how folders are created in MakerFileRecorder.
        // The format is usually: {date}/{runId}/root
        // Here we search for folders containing the projectId.
        var projectDirs = Directory.GetDirectories(dateDir, $"*{projectId}*", SearchOption.AllDirectories);
        if (!projectDirs.Any()) return Array.Empty<string>();

        // Sort by creation time to get the latest run for this project
        var targetDir = projectDirs
            .OrderByDescending(Directory.GetCreationTimeUtc)
            .First();
        
        // Recursively find all JSON files in the target directory
        var allFiles = Directory.GetFiles(targetDir, "*.json", SearchOption.AllDirectories);
        
        // Convert to relative paths for display
        return allFiles
            .Select(f => Path.GetRelativePath(targetDir, f))
            .OrderBy(f => f) // Basic sort
            .ToList();
    }

    public async Task<string?> GetRunFileContent(string projectId, string fileName)
    {
        // fileName comes from frontend, e.g. "root/0001_Proposal.json" or "root/1/0001_Decomposition.json"
        
        var baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runs");
        if (!Directory.Exists(baseDir)) return null;

        var dateDir = Directory.GetDirectories(baseDir)
             .OrderByDescending(d => d)
             .FirstOrDefault();
        if (dateDir == null) return null;

        var projectDirs = Directory.GetDirectories(dateDir, $"*{projectId}*", SearchOption.AllDirectories);
        if (!projectDirs.Any()) return null;
        
        // Match GetRunFiles logic: select latest created directory
        var targetDir = projectDirs
            .OrderByDescending(Directory.GetCreationTimeUtc)
            .First();
        
        // Safe combination
        var fullPath = Path.GetFullPath(Path.Combine(targetDir, fileName));
        
        // Path Traversal Check
        if (!fullPath.StartsWith(Path.GetFullPath(targetDir)))
        {
            return null;
        }

        if (!File.Exists(fullPath)) return null;

        return await File.ReadAllTextAsync(fullPath);
    }

    private IMakerProjectRunner GetRunner(string projectId)
    {
        if (!_runners.TryGetValue(projectId, out var runner))
        {
            throw new KeyNotFoundException($"未知项目：{projectId}");
        }

        return runner;
    }
}
