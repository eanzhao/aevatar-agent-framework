using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Maker;

namespace MakerProjectsDemo.Infrastructure;

public sealed record MakerProjectSpec
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string Icon { get; init; } = "🧠";
    public int WorkerCount { get; init; } = 4;
    public Func<IGAgentActorFactory, CancellationToken, Task<IGAgentActor>> TaskFactory { get; init; } =
        default!;
    public Func<IGAgentActorFactory, CancellationToken, Task<IGAgentActor>> WorkerFactory { get; init; } =
        default!;
    public Func<MakerProjectRunContext, AssignTaskEvent> BuildRootAssignTask { get; init; } = default!;
    public Action<MakerProjectRunContext>? OnRunStarting { get; init; }
    public Action<MakerProjectRunContext>? OnRunCompleted { get; init; }
}

public sealed class MakerProjectRunContext
{
    public MakerProjectRunContext(
        string projectId,
        string runId,
        string rootTaskId,
        MakerTimelineStore timeline,
        IServiceProvider services)
    {
        ProjectId = projectId;
        RunId = runId;
        RootTaskId = rootTaskId;
        Timeline = timeline;
        Services = services;
    }

    public string ProjectId { get; }
    public string RunId { get; }
    public string RootTaskId { get; }
    public MakerTimelineStore Timeline { get; }
    public IServiceProvider Services { get; }
    public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();

    public T? GetItem<T>(string key) where T : class
    {
        return Items.TryGetValue(key, out var value) ? value as T : null;
    }

    public void SetItem<T>(string key, T value) where T : class
    {
        Items[key] = value;
    }
}

