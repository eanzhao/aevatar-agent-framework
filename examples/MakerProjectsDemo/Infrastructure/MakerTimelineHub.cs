using System.Collections.Concurrent;

namespace MakerProjectsDemo.Infrastructure;

public sealed class MakerTimelineHub
{
    private readonly ConcurrentDictionary<string, MakerTimelineStore> _stores = new(StringComparer.OrdinalIgnoreCase);

    public MakerTimelineStore GetStore(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException("ProjectId cannot be empty", nameof(projectId));
        }

        return _stores.GetOrAdd(projectId, _ => new MakerTimelineStore());
    }

    public IReadOnlyList<TimelineEventDto> GetEvents(string projectId)
    {
        return GetStore(projectId).GetEvents();
    }

    public void Clear(string projectId)
    {
        GetStore(projectId).Clear();
    }
}

