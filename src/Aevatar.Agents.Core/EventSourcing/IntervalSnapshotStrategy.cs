namespace Aevatar.Agents.Core.EventSourcing;

/// <summary>
/// Interval-based snapshot strategy
/// </summary>
public class IntervalSnapshotStrategy : ISnapshotStrategy
{
    private readonly long _interval;

    public IntervalSnapshotStrategy(long interval)
    {
        _interval = interval;
    }

    public bool ShouldCreateSnapshot(long version)
    {
        return version % _interval == 0;
    }
}