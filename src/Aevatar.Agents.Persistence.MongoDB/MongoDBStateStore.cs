using System;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.Persistence;
using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB;

/// <summary>
/// MongoDB state store implementation
/// Stores agent states in MongoDB collections
/// </summary>
/// <typeparam name="TState">State type</typeparam>
public class MongoDBStateStore<TState> : IVersionedStateStore<TState>
    where TState : class
{
    private readonly IMongoCollection<AgentStateDocument<TState>> _collection;

    /// <summary>
    /// Create MongoDB state store
    /// </summary>
    /// <param name="database">MongoDB database instance</param>
    /// <param name="collectionName">Optional custom collection name</param>
    public MongoDBStateStore(
        IMongoDatabase database,
        string? collectionName = null)
    {
        var name = collectionName ?? $"agent_states_{typeof(TState).Name}";
        _collection = database.GetCollection<AgentStateDocument<TState>>(name);

        // Ensure indexes are created (idempotent, runs once per collection per process)
        MongoDBIndexManager.EnsureStateStoreIndexes(_collection);
    }

    /// <summary>
    /// Load state from MongoDB
    /// </summary>
    public async Task<TState?> LoadAsync(Guid agentId, CancellationToken ct = default)
    {
        var doc = await _collection.Find(x => x.AgentId == agentId)
                                   .FirstOrDefaultAsync(ct)
                                   .ConfigureAwait(false);
        return doc?.State;
    }

    /// <summary>
    /// Save state to MongoDB (upsert)
    /// </summary>
    public async Task SaveAsync(Guid agentId, TState state, CancellationToken ct = default)
    {
        var doc = new AgentStateDocument<TState>
        {
            AgentId = agentId,
            State = state,
            Version = 1,
            UpdatedAt = DateTime.UtcNow
        };

        await _collection.ReplaceOneAsync(
            x => x.AgentId == agentId,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Save with version control (optimistic concurrency)
    /// </summary>
    public async Task SaveAsync(Guid agentId, TState state, long expectedVersion, CancellationToken ct = default)
    {
        var currentVersion = await GetCurrentVersionAsync(agentId, ct).ConfigureAwait(false);

        if (currentVersion != expectedVersion)
        {
            throw new StateVersionConflictException(agentId, expectedVersion, currentVersion);
        }

        var doc = new AgentStateDocument<TState>
        {
            AgentId = agentId,
            State = state,
            Version = currentVersion + 1,
            UpdatedAt = DateTime.UtcNow
        };

        var result = await _collection.ReplaceOneAsync(
            x => x.AgentId == agentId && x.Version == expectedVersion,
            doc,
            new ReplaceOptions { IsUpsert = false },
            ct).ConfigureAwait(false);

        if (result.MatchedCount == 0)
        {
            throw new StateVersionConflictException(agentId, expectedVersion, currentVersion);
        }
    }

    /// <summary>
    /// Get current version
    /// </summary>
    public async Task<long> GetCurrentVersionAsync(Guid agentId, CancellationToken ct = default)
    {
        var doc = await _collection.Find(x => x.AgentId == agentId)
                                   .Project(x => x.Version)
                                   .FirstOrDefaultAsync(ct)
                                   .ConfigureAwait(false);
        return doc;
    }

    /// <summary>
    /// Delete state from MongoDB
    /// </summary>
    public async Task DeleteAsync(Guid agentId, CancellationToken ct = default)
    {
        await _collection.DeleteOneAsync(x => x.AgentId == agentId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Check if state exists
    /// </summary>
    public async Task<bool> ExistsAsync(Guid agentId, CancellationToken ct = default)
    {
        var count = await _collection.CountDocumentsAsync(x => x.AgentId == agentId, cancellationToken: ct)
                                      .ConfigureAwait(false);
        return count > 0;
    }
}

/// <summary>
/// MongoDB state store factory for DI
/// </summary>
public static class MongoDBStateStoreFactory
{
    /// <summary>
    /// Create MongoDB state store factory function using DI-registered IMongoDatabase
    /// 
    /// Preferred usage:
    /// <code>
    /// services.AddAevatarMongoDB("mongodb://localhost:27017");
    /// services.AddMongoDBStateStore&lt;MyState&gt;();
    /// </code>
    /// </summary>
    /// <typeparam name="TState">State type</typeparam>
    /// <param name="collectionName">Optional custom collection name</param>
    /// <returns>Factory function for DI</returns>
    public static Func<IServiceProvider, object> Create<TState>(string? collectionName = null)
        where TState : class
    {
        return sp =>
        {
            var database = sp.GetService(typeof(IMongoDatabase)) as IMongoDatabase
                ?? throw new InvalidOperationException(
                    "IMongoDatabase not registered. Call services.AddAevatarMongoDB() first.");
            return new MongoDBStateStore<TState>(database, collectionName);
        };
    }
}