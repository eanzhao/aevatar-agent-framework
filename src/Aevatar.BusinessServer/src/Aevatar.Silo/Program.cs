using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using MongoDB.Driver;
using Aevatar.Silo.Extensions;
using Aevatar.BusinessServer.Agents.Agents; // CRITICAL: Reference to force assembly load
using Aevatar.Agents.Runtime.Orleans.Extensions;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Persistence.MongoDB;
using Aevatar.Agents.Runtime.Orleans.EventSourcing;
using Aevatar.Agents.Runtime.Orleans.MongoDB;
using Aevatar.Agents.Orleans.MongoDB;

namespace Aevatar.Silo;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Configure Serilog
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .CreateLogger();

        try
        {
            Log.Information("🚀 Starting Aevatar Silo");
            Log.Information("  Environment: {Environment}", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production");
            Log.Information("  Storage Provider: {Provider}", configuration["Storage:Provider"] ?? "Memory");
            Log.Information("  Streaming Provider: {Provider}", configuration["Streaming:Provider"] ?? "OrleansStream");
            
            var host = CreateHostBuilder(args).Build();
            
            await host.RunAsync();
            
            Log.Information("✅ Silo shutdown completed");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "❌ Silo terminated unexpectedly!");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .UseOrleansConfiguration() // Extension method from OrleansHostExtension
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.Configure(app =>
                {
                    // Map health check endpoints for Kubernetes
                    app.MapOrleansHealthChecks();
                });
                
                webBuilder.ConfigureKestrel(options =>
                {
                    // Health check endpoint port
                    options.ListenAnyIP(8080);
                });
            })
            .ConfigureServices((context, services) =>
            {
                services.AddOrleansHealthChecks();
                
                // MongoDB configuration
                var mongoConnectionString = context.Configuration.GetConnectionString("MongoDB") 
                    ?? "mongodb://localhost:27017/AevatarBusiness";
                var databaseName = context.Configuration.GetSection("Storage")
                    .GetValue("DatabaseName", "AevatarBusiness");
                
                services.AddSingleton<IMongoClient>(sp => new MongoClient(mongoConnectionString));
                services.AddSingleton<IMongoDatabase>(sp => 
                    sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));
                
                // MongoDB Event Repository for EventStore
                services.AddSingleton<IEventRepository>(sp => new MongoEventRepository(
                    sp.GetRequiredService<IMongoClient>(),
                    new MongoEventRepositoryOptions
                    {
                        DatabaseName = databaseName,
                        CollectionName = "agent_events",
                        EnableDetailedLogging = true
                    },
                    sp.GetRequiredService<ILogger<MongoEventRepository>>()));

                // Aevatar Agent System with MongoDB stores
                services.AddAevatarAgentSystem(options =>
                {
                    options.StateStoreType = typeof(MongoDBStateStore<>);
                    options.ConfigStoreType = typeof(MongoDbConfigStore<>);
                    options.EventRouterStoreType = typeof(MongoDBEventRouterStore);
                    options.EventStoreType = typeof(OrleansEventStore);
                }, builder => builder.UseOrleansRuntime());
                
                Log.Information("✅ Aevatar Agent System configured with MongoDB stores");
            });
    }
}

