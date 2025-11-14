using System;
using MongoDB.Bson.Serialization.Attributes;

namespace Aevatar.Agents.Persistence.MongoDB;

/// <summary>
/// MongoDB document wrapper for agent state
/// </summary>
/// <typeparam name="TState">State type</typeparam>
internal class AgentStateDocument<TState>
{
    /// <summary>
    /// Agent ID (MongoDB _id)
    /// </summary>
    [BsonId]
    public Guid AgentId { get; set; }

    /// <summary>
    /// State object
    /// </summary>
    public TState State { get; set; } = default!;

    /// <summary>
    /// Version for optimistic concurrency
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}