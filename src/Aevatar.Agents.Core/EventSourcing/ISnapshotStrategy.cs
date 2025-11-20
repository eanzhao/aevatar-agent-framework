namespace Aevatar.Agents.Core.EventSourcing;

/// <summary>
/// Snapshot strategy interface
/// </summary>
public interface ISnapshotStrategy
{
    bool ShouldCreateSnapshot(long version);
}