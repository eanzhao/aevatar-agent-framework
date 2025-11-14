using Aevatar.Agents.Abstractions.EventSourcing;

namespace Aevatar.Agents.Abstractions;

/// <summary>
/// GAgent configuration options
/// Used in Composition Root to configure GAgent behavior
/// </summary>
public class GAgentOptions
{
    /// <summary>
    /// StateStore factory function
    /// Creates IStateStore instance for specific GAgent
    /// </summary>
    public Func<IServiceProvider, object>? StateStore { get; set; }

    /// <summary>
    /// Whether to enable EventSourcing
    /// If enabled, StateStore should be EventSourcingStateStore
    /// </summary>
    public bool EnableEventSourcing { get; set; }

    /// <summary>
    /// EventStore factory (required for EventSourcing)
    /// </summary>
    public Func<IServiceProvider, IEventStore>? EventStore { get; set; }

    /// <summary>
    /// Snapshot strategy (used by EventSourcing)
    /// </summary>
    // TODO: Add ISnapshotStrategy interface
    // public EventSourcing.ISnapshotStrategy? SnapshotStrategy { get; set; }

    /// <summary>
    /// Snapshot interval (used by EventSourcing, default 100 events)
    /// </summary>
    public int SnapshotInterval { get; set; } = 100;
}
