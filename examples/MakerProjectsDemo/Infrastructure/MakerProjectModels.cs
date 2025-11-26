using Aevatar.Agents.Maker;

namespace MakerProjectsDemo.Infrastructure;

public sealed record MakerProjectInfo(
    string Id,
    string Name,
    string Description,
    string Icon);

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
        ProjectId = string.Empty,
        TaskId = string.Empty,
        Phase = "Unknown",
        CurrentDepth = 0,
        PendingChildren = [],
        CompletedChildren = [],
        VoteTallies = new(),
        VoteClusters = [],
        CandidatePreviews = new(),
        MicroObjective = string.Empty,
        MicroCursor = 0,
        MicroTotal = 0,
        MicroObjectives = [],
        PlannedSteps = [],
        WorkerIds = [],
        FinalResult = string.Empty,
        FailureReason = string.Empty,
        TimelineCount = 0
    };

    public string Status { get; init; } = "idle";
    public string RunId { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public string TaskId { get; init; } = string.Empty;
    public string Phase { get; init; } = "Unknown";
    public int CurrentDepth { get; init; }
    public List<string> PendingChildren { get; init; } = [];
    public List<string> CompletedChildren { get; init; } = [];
    public Dictionary<string, int> VoteTallies { get; init; } = new();
    public List<VoteClusterDto> VoteClusters { get; init; } = new();
    public Dictionary<string, string> CandidatePreviews { get; init; } = new();
    public string MicroObjective { get; init; } = string.Empty;
    public int MicroCursor { get; init; }
    public int MicroTotal { get; init; }
    public List<string> MicroObjectives { get; init; } = [];
    public List<PlannedStepDto> PlannedSteps { get; init; } = [];
    public List<string> WorkerIds { get; init; } = [];
    public string FinalResult { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
    public int TimelineCount { get; init; }

    public static MakerSnapshotDto FromState(
        string projectId,
        TaskAgentState state,
        string? runId,
        string status,
        int timelineCount,
        IEnumerable<string>? workerIds = null)
    {
        var voteTallies = state.VoteTallies?.ToDictionary(pair => pair.Key, pair => pair.Value)
                           ?? new Dictionary<string, int>();

        var clusters = state.VoteClusters?.Values.Select(c => new VoteClusterDto(
            c.ClusterId,
            BuildPreview(c.RepresentativeContent),
            c.VoteCount,
            c.VariantHashes.Count))
            .OrderByDescending(c => c.Votes)
            .ToList() ?? new List<VoteClusterDto>();

        var candidatePreviews = state.CandidateContent?
            .ToDictionary(pair => pair.Key, pair => BuildPreview(pair.Value)) ?? new Dictionary<string, string>();

        return new MakerSnapshotDto
        {
            ProjectId = projectId,
            Status = status,
            RunId = runId ?? string.Empty,
            TaskId = state.TaskId,
            Phase = "PHASE_" + state.Phase.ToString().ToUpperInvariant(),
            FailureReason = state.FailureReason,
            CurrentDepth = state.CurrentDepth,
            PendingChildren = state.PendingChildIds.ToList(),
            CompletedChildren = state.ChildResults.Keys.OrderBy(x => x).ToList(),
            VoteTallies = voteTallies,
            VoteClusters = clusters,
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
    public sealed record VoteClusterDto(string Id, string Content, int Votes, int Variants);
}

