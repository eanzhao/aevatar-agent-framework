using System;
using MongoDB.Bson.Serialization.Attributes;

namespace Aevatar.Agents.Persistence.MongoDB;

/// <summary>
/// MongoDB configuration document wrapper
/// </summary>
/// <typeparam name="TConfig">Configuration type</typeparam>
internal class AgentConfigDocument<TConfig>
{
    /// <summary>
    /// Agent ID (MongoDB _id)
    /// </summary>
    [BsonId]
    public Guid AgentId { get; set; }

    /// <summary>
    /// Configuration object
    /// </summary>
    public TConfig Config { get; set; } = default!;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}