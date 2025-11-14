using System;
using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB;

/// <summary>
/// MongoDB configuration store factory for DI
/// </summary>
public static class MongoDBConfigurationStoreFactory
{
    /// <summary>
    /// Create MongoDB configuration store factory function
    /// </summary>
    public static Func<IServiceProvider, object> Create<TConfig>(
        string? connectionString = null,
        string? databaseName = null)
        where TConfig : class, new()
    {
        return sp =>
        {
            var mongoClient = new MongoClient(connectionString ?? "mongodb://localhost:27017");
            var database = mongoClient.GetDatabase(databaseName ?? "aevatar");
            return new MongoDBConfigurationStore<TConfig>(database);
        };
    }
}