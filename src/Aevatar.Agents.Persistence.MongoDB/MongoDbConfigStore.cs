using System;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.Persistence;
using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB;

/// <summary>
/// MongoDB configuration store implementation
/// Stores agent configurations in MongoDB collections
/// </summary>
/// <typeparam name="TConfig">Configuration type</typeparam>
public class MongoDbConfigStore<TConfig> : IConfigStore<TConfig>
    where TConfig : class, new()
{
    private readonly IMongoCollection<AgentConfigDocument<TConfig>> _collection;

    /// <summary>
    /// Create MongoDB configuration store
    /// </summary>
    /// <param name="database">MongoDB database instance</param>
    /// <param name="collectionName">Optional custom collection name</param>
    public MongoDbConfigStore(
        IMongoDatabase database,
        string? collectionName = null)
    {
        var name = collectionName ?? $"agent_configs_{typeof(TConfig).Name}";
        _collection = database.GetCollection<AgentConfigDocument<TConfig>>(name);

        // Create index on AgentId
        var indexKeys = Builders<AgentConfigDocument<TConfig>>.IndexKeys.Ascending(x => x.AgentId);
        var indexModel = new CreateIndexModel<AgentConfigDocument<TConfig>>(indexKeys);
        _collection.Indexes.CreateOne(indexModel);
    }

    /// <summary>
    /// Load configuration from MongoDB
    /// </summary>
    public async Task<TConfig?> LoadAsync(Guid agentId, CancellationToken ct = default)
    {
        var doc = await _collection.Find(x => x.AgentId == agentId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return doc?.Config;
    }

    /// <summary>
    /// Save configuration to MongoDB (upsert)
    /// </summary>
    public async Task SaveAsync(Guid agentId, TConfig config, CancellationToken ct = default)
    {
        var doc = new AgentConfigDocument<TConfig>
        {
            AgentId = agentId,
            Config = config,
            UpdatedAt = DateTime.UtcNow
        };

        await _collection.ReplaceOneAsync(
            x => x.AgentId == agentId,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Delete configuration from MongoDB
    /// </summary>
    public async Task DeleteAsync(Guid agentId, CancellationToken ct = default)
    {
        await _collection.DeleteOneAsync(x => x.AgentId == agentId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Check if configuration exists
    /// </summary>
    public async Task<bool> ExistsAsync(Guid agentId, CancellationToken ct = default)
    {
        var count = await _collection.CountDocumentsAsync(
            x => x.AgentId == agentId,
            cancellationToken: ct).ConfigureAwait(false);
        return count > 0;
    }
}