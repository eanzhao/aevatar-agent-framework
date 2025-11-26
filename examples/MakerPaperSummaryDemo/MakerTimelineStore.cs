using System.Collections.Concurrent;

namespace MakerPaperSummaryDemo;

public sealed record TimelineEventDto(DateTime Timestamp, string Level, string Source, string Message);

public sealed class MakerTimelineStore
{
    private const int MaxEvents = 2000;
    private readonly ConcurrentQueue<TimelineEventDto> _events = new();

    public void Append(string level, string source, string message)
    {
        _events.Enqueue(new TimelineEventDto(DateTime.UtcNow, level, source, message));

        while (_events.Count > MaxEvents && _events.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<TimelineEventDto> GetEvents()
    {
        return _events
            .OrderBy(e => e.Timestamp)
            .ToList();
    }

    public void Clear()
    {
        while (_events.TryDequeue(out _))
        {
        }
    }

    public int Count => _events.Count;
}

