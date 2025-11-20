namespace Aevatar.Agents.Core.EventSourcing;

/// <summary>
/// Hybrid snapshot strategy (interval + time)
/// </summary>
public class HybridSnapshotStrategy : ISnapshotStrategy
{
    private readonly long _interval;
    private readonly TimeSpan _timeSpan;
    private DateTime _lastSnapshotTime = DateTime.UtcNow;

    public HybridSnapshotStrategy(long interval, TimeSpan timeSpan)
    {
        _interval = interval;
        _timeSpan = timeSpan;
    }

    public bool ShouldCreateSnapshot(long version)
    {
        // Strategy 1: Every N events
        if (version % _interval == 0) return true;

        // Strategy 2: Time-based
        if ((DateTime.UtcNow - _lastSnapshotTime) > _timeSpan)
        {
            _lastSnapshotTime = DateTime.UtcNow;
            return true;
        }

        return false;
    }
}