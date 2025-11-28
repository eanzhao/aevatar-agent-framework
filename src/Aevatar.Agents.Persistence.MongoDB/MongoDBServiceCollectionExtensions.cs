using System;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB;

/// <summary>
/// DI extension methods for MongoDB persistence
/// </summary>
public static class MongoDBServiceCollectionExtensions
{
    /// <summary>
    /// Add MongoDB services with proper connection management
    /// 
    /// Usage:
    /// <code>
    /// services.AddAevatarMongoDB("mongodb://localhost:27017", "aevatar");
    /// </code>
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="connectionString">MongoDB connection string</param>
    /// <param name="databaseName">Database name (default: "aevatar")</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddAevatarMongoDB(
        this IServiceCollection services,
        string connectionString,
        string databaseName = "aevatar")
    {
        ArgumentNullException.ThrowIfNull(connectionString);

        // Register IMongoClient as singleton (connection pool is managed internally)
        services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));

        // Register IMongoDatabase as singleton (bound to the single client)
        services.AddSingleton(sp =>
            sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));

        return services;
    }

    /// <summary>
    /// Add MongoDB services with custom client settings
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="settings">MongoDB client settings</param>
    /// <param name="databaseName">Database name (default: "aevatar")</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddAevatarMongoDB(
        this IServiceCollection services,
        MongoClientSettings settings,
        string databaseName = "aevatar")
    {
        ArgumentNullException.ThrowIfNull(settings);

        services.AddSingleton<IMongoClient>(_ => new MongoClient(settings));
        services.AddSingleton(sp =>
            sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));

        return services;
    }

    /// <summary>
    /// Add MongoDB state store for a specific state type
    /// Requires AddAevatarMongoDB to be called first
    /// </summary>
    /// <typeparam name="TState">State type (must be a class)</typeparam>
    /// <param name="services">Service collection</param>
    /// <param name="collectionName">Optional custom collection name</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddMongoDBStateStore<TState>(
        this IServiceCollection services,
        string? collectionName = null)
        where TState : class
    {
        services.AddSingleton(sp =>
        {
            var database = sp.GetRequiredService<IMongoDatabase>();
            return new MongoDBStateStore<TState>(database, collectionName);
        });

        return services;
    }

    /// <summary>
    /// Add MongoDB config store for a specific config type
    /// Requires AddAevatarMongoDB to be called first
    /// </summary>
    /// <typeparam name="TConfig">Config type (must be a class with parameterless constructor)</typeparam>
    /// <param name="services">Service collection</param>
    /// <param name="collectionName">Optional custom collection name</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddMongoDBConfigStore<TConfig>(
        this IServiceCollection services,
        string? collectionName = null)
        where TConfig : class, new()
    {
        services.AddSingleton(sp =>
        {
            var database = sp.GetRequiredService<IMongoDatabase>();
            return new MongoDbConfigStore<TConfig>(database, collectionName);
        });

        return services;
    }

    /// <summary>
    /// Add MongoDB EventRouter store
    /// Requires AddAevatarMongoDB to be called first
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="collectionName">Optional custom collection name</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddMongoDBEventRouterStore(
        this IServiceCollection services,
        string? collectionName = null)
    {
        services.AddSingleton(sp =>
        {
            var database = sp.GetRequiredService<IMongoDatabase>();
            return new MongoDBEventRouterStore(database, collectionName);
        });

        return services;
    }
}

