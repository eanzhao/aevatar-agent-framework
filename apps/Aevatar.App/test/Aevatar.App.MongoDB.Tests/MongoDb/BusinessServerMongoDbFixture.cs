using System;
using EphemeralMongo;

namespace Aevatar.App.MongoDB;

public class AppMongoDbFixture : IDisposable
{
    public readonly static IMongoRunner MongoDbRunner;

    static AppMongoDbFixture()
    {
        MongoDbRunner = MongoRunner.Run(new MongoRunnerOptions
        {
            // Disable replica set for faster startup in test environments
            // Most tests don't require replica set features
            UseSingleNodeReplicaSet = false
        });
    }

    public static string GetRandomConnectionString()
    {
        return GetConnectionString("Db_" + Guid.NewGuid().ToString("N"));
    }

    public static string GetConnectionString(string databaseName)
    {
        var connectionString = MongoDbRunner.ConnectionString;
        var parts = connectionString.Split('?');
        var baseConnection = parts[0].TrimEnd('/');
        var queryString = parts.Length > 1 ? "?" + parts[1] : "";
        return $"{baseConnection}/{databaseName}{queryString}";
    }

    public void Dispose()
    {
        try
        {
            MongoDbRunner?.Dispose();
        }
        catch (System.IO.FileNotFoundException)
        {
            // EphemeralMongo 1.1.0 has a known issue with MongoDB.Driver.Core assembly loading during Dispose
            // This happens when MongoDB.Driver is upgraded from 3.0.0 to 3.3.0 (required by ABP 9.3.1)
            // MongoDB.Driver 3.3.0 uses MongoDB.Driver.Core 2.19.0.0, but EphemeralMongo expects a different version
            // Tests work correctly, only cleanup has this issue - safe to ignore
        }
    }
}
