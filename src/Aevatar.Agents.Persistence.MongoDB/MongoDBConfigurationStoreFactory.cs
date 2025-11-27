using System;
using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB;

/// <summary>
/// MongoDB configuration store factory for DI
/// </summary>
public static class MongoDBConfigurationStoreFactory
{
    /// <summary>
    /// Create MongoDB configuration store factory function using DI-registered IMongoDatabase
    /// 
    /// Preferred usage:
    /// <code>
    /// services.AddAevatarMongoDB("mongodb://localhost:27017");
    /// services.AddMongoDBConfigStore&lt;MyConfig&gt;();
    /// </code>
    /// </summary>
    /// <typeparam name="TConfig">Config type</typeparam>
    /// <param name="collectionName">Optional custom collection name</param>
    /// <returns>Factory function for DI</returns>
    public static Func<IServiceProvider, object> Create<TConfig>(string? collectionName = null)
        where TConfig : class, new()
    {
        return sp =>
        {
            var database = sp.GetService(typeof(IMongoDatabase)) as IMongoDatabase
                ?? throw new InvalidOperationException(
                    "IMongoDatabase not registered. Call services.AddAevatarMongoDB() first.");
            return new MongoDbConfigStore<TConfig>(database, collectionName);
        };
    }
}