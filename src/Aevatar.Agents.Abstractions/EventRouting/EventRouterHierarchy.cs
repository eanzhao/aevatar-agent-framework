namespace Aevatar.Agents.Abstractions.EventRouting;

/// <summary>
/// Data model for persisting EventRouter hierarchy relationships
/// </summary>
public class EventRouterHierarchy
{
    /// <summary>
    /// Parent agent ID (null if this is a root agent)
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Collection of child agent IDs
    /// </summary>
    public HashSet<Guid> ChildrenIds { get; set; } = new();
}
