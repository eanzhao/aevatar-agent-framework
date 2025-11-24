using System;
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;

namespace Aevatar.Agents.Persistence.MongoDB;

/// <summary>
/// MongoDB document for EventRouter hierarchy
/// </summary>
public class EventRouterHierarchyDocument
{
    /// <summary>
    /// Agent ID (primary key)
    /// </summary>
    [BsonId]
    public Guid AgentId { get; set; }

    /// <summary>
    /// Parent agent ID (null if root)
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Collection of child agent IDs
    /// </summary>
    public List<Guid> ChildrenIds { get; set; } = new();

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
