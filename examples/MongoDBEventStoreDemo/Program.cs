using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Orleans.MongoDB;
using Aevatar.Agents.Runtime.Orleans;
using Aevatar.Agents.Runtime.Orleans.EventSourcing;
using Aevatar.Agents.Runtime.Orleans.MongoDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDBEventStoreDemo;
using Orleans.Serialization;

Console.WriteLine("🌌 Aevatar Agent Framework - OrleansEventStore + MongoDB Demo");
Console.WriteLine("==============================================================\n");
Console.WriteLine("This demo showcases:");
Console.WriteLine("  ✅ OrleansEventStore (Grain-based event coordination)");
Console.WriteLine("  ✅ MongoDB as Orleans GrainStorage backend");
Console.WriteLine("  ✅ Distributed concurrency control via Orleans");
Console.WriteLine("  ✅ Event persistence to MongoDB");
Console.WriteLine("  ✅ Automatic event replay from MongoDB\n");

// ========== MongoDB Configuration ==========
const string mongoConnectionString = "mongodb://localhost:27017";
const string mongoDatabase = "OrleansEventStore";
const string collectionPrefix = "BankAccounts";

Console.WriteLine("📊 Architecture:");
Console.WriteLine("   OrleansEventStore (IEventStore)");
Console.WriteLine("      ↓");
Console.WriteLine("   IEventStorageGrain (Orleans Grain)");
Console.WriteLine("      ↓");
Console.WriteLine("   Orleans GrainStorage (MongoDB Provider)");
Console.WriteLine("      ↓");
Console.WriteLine($"   MongoDB: {mongoDatabase}\n");

Console.WriteLine("📊 MongoDB Configuration:");
Console.WriteLine($"   Connection: {mongoConnectionString}");
Console.WriteLine($"   Database: {mongoDatabase}");
Console.WriteLine($"   Collection Prefix: {collectionPrefix}");
Console.WriteLine($"   Collection Name: {collectionPrefix}-EventStorageState\n");

// ========== Build Host with Orleans + MongoDB ==========
var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .UseOrleans((context, siloBuilder) =>
    {
        siloBuilder
            // Localhost clustering for demo
            .UseLocalhostClustering()

            // ✅ Configure MongoDB Client (REQUIRED before AddMongoDBGrainStorage)
            .UseMongoDBClient(provider =>
            {
                var settings = MongoClientSettings.FromConnectionString(mongoConnectionString);
                settings.MaxConnectionPoolSize = 100;
                settings.MinConnectionPoolSize = 10;
                return settings;
            })

            // ✅ Protobuf serialization (REQUIRED for AgentStateEvent!)
            .ConfigureServices(services =>
            {
                services.AddSerializer(serializerBuilder =>
                {
                    serializerBuilder.AddProtobufSerializer();
                });
            })
            
            .AddMemoryStreams(AevatarAgentsOrleansConstants.StreamProviderName)

            // ✅ MongoDB GrainStorage for EventStore
            .AddMongoDBGrainStorage("EventStoreStorage", options =>
            {
                options.DatabaseName = mongoDatabase;
                options.CollectionPrefix = collectionPrefix;
                options.CreateShardKeyForCosmos = false;
            })

            // ✅ MongoDB GrainStorage for PubSub storage (required for streams)
            .AddMongoDBGrainStorage("PubSubStore", options =>
            {
                options.DatabaseName = mongoDatabase;
                options.CollectionPrefix = collectionPrefix;
                options.CreateShardKeyForCosmos = false;
            });
    })
    .ConfigureServices(services =>
    {
        // ✅ Register MongoDB Event Repository (decoupled storage implementation)
        // IMPORTANT: Use separate collection per Agent type for better performance
        services.AddSingleton<IEventRepository>(sp =>
        {
            var mongoClient = sp.GetRequiredService<IMongoClient>();
            var logger = sp.GetRequiredService<ILogger<MongoEventRepository>>();
            
            // Collection name pattern: {AgentType}Events
            // This ensures each agent type has its own collection with optimized indexes
            return new MongoEventRepository(
                mongoClient, 
                mongoDatabase, 
                collectionName: "BankAccountEvents",  // ✅ Separate collection for BankAccount
                logger);
        });
        
        // ✅ Register OrleansEventStore (uses IEventRepository + IEventStorageGrain)
        services.AddSingleton<IEventStore, OrleansEventStore>();
        
        // ✅ Register AIGAgentFactory for automatic EventStore injection
        services.AddSingleton<IGAgentFactory, AIGAgentFactory>();
        
        // ✅ Register Orleans Agent Factory
        services.AddOrleansActorFactory();
        
        // ✅ Register Auto-Discovery Factory Provider
        services.AddGAgentActorFactoryProvider();
    })
    .Build();

// Start Orleans Silo
Console.WriteLine("🚀 Starting Orleans Silo with MongoDB backend...\n");
await host.StartAsync();

try
{
    var factory = host.Services.GetRequiredService<IGAgentActorFactory>();

    Console.WriteLine("✅ Orleans Silo started successfully!\n");

    // ============================================================
    // Part 1: Create Account and Execute Transactions
    // ============================================================
    Console.WriteLine("📍 Part 1: Creating Account and Transactions");
    Console.WriteLine("══════════════════════════════════════════════\n");

    var accountId = Guid.NewGuid();
    Console.WriteLine($"📊 Agent ID: {accountId:N}\n");

    // ✅ Create Actor (EventSourcing is automatically enabled via IEventStore registration)
    var actor = await factory.CreateGAgentActorAsync<BankAccountAgent>(accountId);

    var agent = actor.GetAgent() as BankAccountAgent;
    if (agent == null)
    {
        throw new InvalidOperationException("Failed to get BankAccountAgent instance");
    }

    // Create account
    await agent.CreateAccountAsync("Alice Smith", 100m);
    
    Console.WriteLine($"✅ Account created");
    Console.WriteLine($"   Holder: {agent.GetState().AccountHolder}");
    Console.WriteLine($"   Balance: ${agent.GetState().Balance:F2}");
    Console.WriteLine($"   Version: v{agent.GetCurrentVersion()}\n");

    // Individual transactions
    Console.WriteLine("💰 Individual Transactions:");
    Console.WriteLine("────────────────────────────");
    await agent.DepositAsync(1000m, "Salary");
    Console.WriteLine($"  ✓ Deposited $1000 (Salary) - Balance: ${agent.GetState().Balance:F2}");

    await agent.DepositAsync(500m, "Bonus");
    Console.WriteLine($"  ✓ Deposited $500 (Bonus) - Balance: ${agent.GetState().Balance:F2}");

    await agent.WithdrawAsync(300m, "Rent");
    Console.WriteLine($"  ✓ Withdrew $300 (Rent) - Balance: ${agent.GetState().Balance:F2}\n");

    Console.WriteLine($"💵 Current Balance: ${agent.GetState().Balance:F2}");
    Console.WriteLine($"📈 Current Version: v{agent.GetCurrentVersion()}\n");

    // ============================================================
    // Part 2: Batch Transactions
    // ============================================================
    Console.WriteLine("📍 Part 2: Batch Transactions");
    Console.WriteLine("══════════════════════════════════════════════════\n");

    Console.WriteLine("⚡ Submitting 3 transactions in one batch:");
    Console.WriteLine("────────────────────────────────────────────");
    await agent.BatchTransactionsAsync(
        (200m, "Freelance payment"),
        (-150m, "Groceries"),
        (100m, "Gift")
    );

    Console.WriteLine($"  ✓ Batch completed - Balance: ${agent.GetState().Balance:F2}\n");

    Console.WriteLine($"💵 Current Balance: ${agent.GetState().Balance:F2}");
    Console.WriteLine($"📈 Current Version: v{agent.GetCurrentVersion()} (Snapshot will trigger at v11)\n");

    // ============================================================
    // Part 2.5: More Transactions to Trigger Snapshot
    // ============================================================
    Console.WriteLine("📍 Part 2.5: More Transactions (Testing Snapshot)");
    Console.WriteLine("══════════════════════════════════════════════════\n");

    Console.WriteLine("💰 Adding 5 more transactions to trigger snapshot...");
    Console.WriteLine("────────────────────────────────────────────");
    
    await agent.DepositAsync(300m, "Investment return");
    Console.WriteLine($"  ✓ Deposited $300 (Investment) - Balance: ${agent.GetState().Balance:F2}, Version: v{agent.GetCurrentVersion()}");

    await agent.WithdrawAsync(100m, "Utilities");
    Console.WriteLine($"  ✓ Withdrew $100 (Utilities) - Balance: ${agent.GetState().Balance:F2}, Version: v{agent.GetCurrentVersion()}");

    await agent.DepositAsync(50m, "Cashback");
    Console.WriteLine($"  ✓ Deposited $50 (Cashback) - Balance: ${agent.GetState().Balance:F2}, Version: v{agent.GetCurrentVersion()}");

    await agent.WithdrawAsync(200m, "Dining");
    Console.WriteLine($"  ✓ Withdrew $200 (Dining) - Balance: ${agent.GetState().Balance:F2}, Version: v{agent.GetCurrentVersion()}");

    await agent.DepositAsync(150m, "Gift received");
    Console.WriteLine($"  ✓ Deposited $150 (Gift) - Balance: ${agent.GetState().Balance:F2}, Version: v{agent.GetCurrentVersion()}");

    Console.WriteLine($"\n📸 Snapshot should be saved at version 10!");
    Console.WriteLine($"💵 Final Balance: ${agent.GetState().Balance:F2}");
    Console.WriteLine($"📈 Final Version: v{agent.GetCurrentVersion()}\n");

    var balanceBeforeRecovery = agent.GetState().Balance;
    var versionBeforeRecovery = agent.GetCurrentVersion();

    // ============================================================
    // Part 3: Event Replay with Snapshot (Crash Recovery Simulation)
    // ============================================================
    Console.WriteLine("📍 Part 3: Event Replay with Snapshot from MongoDB");
    Console.WriteLine("══════════════════════════════════════════════════\n");

    Console.WriteLine("🔄 Simulating grain deactivation and reactivation...");
    Console.WriteLine("   (Creating new actor instance with same ID)\n");

    // Create new actor instance (simulating grain reactivation)
    // Event sourcing automatically replays events from storage when the Agent handles events
    var actor2 = await factory.CreateGAgentActorAsync<BankAccountAgent>(accountId);

    var agent2 = actor2.GetAgent() as BankAccountAgent;
    if (agent2 == null)
    {
        throw new InvalidOperationException("Failed to get BankAccountAgent instance on recovery");
    }

    Console.WriteLine("✅ Grain reactivated and state recovered from MongoDB!");
    Console.WriteLine($"   Holder: {agent2.GetState().AccountHolder}");
    Console.WriteLine($"   Balance: ${agent2.GetState().Balance:F2}");
    Console.WriteLine($"   Transactions: {agent2.GetState().TransactionCount}");
    Console.WriteLine($"   Version: v{agent2.GetCurrentVersion()}");
    Console.WriteLine($"   📸 Recovery used Snapshot + incremental events!\n");

    // Verify consistency
    if (Math.Abs(agent2.GetState().Balance - balanceBeforeRecovery) < 0.01 && 
        agent2.GetCurrentVersion() == versionBeforeRecovery)
    {
        Console.WriteLine("✅ State consistency verified!");
        Console.WriteLine($"   Balance matches: ${balanceBeforeRecovery:F2}");
        Console.WriteLine($"   Version matches: v{versionBeforeRecovery}\n");
    }
    else
    {
        Console.WriteLine("❌ State mismatch detected!");
        Console.WriteLine($"   Expected: ${balanceBeforeRecovery:F2}, Got: ${agent2.GetState().Balance:F2}");
        Console.WriteLine($"   Expected version: v{versionBeforeRecovery}, Got: v{agent2.GetCurrentVersion()}\n");
    }

    // ============================================================
    // Part 4: Transaction History
    // ============================================================
    Console.WriteLine("📍 Part 4: Transaction History (from MongoDB)");
    Console.WriteLine("══════════════════════════════════════════════════\n");

    var state = agent2.GetState();
    Console.WriteLine("📜 Complete History:");
    foreach (var entry in state.History)
    {
        Console.WriteLine($"   {entry}");
    }

    Console.WriteLine($"\n📊 Final Statistics:");
    Console.WriteLine($"   Account Holder: {state.AccountHolder}");
    Console.WriteLine($"   Final Balance: ${state.Balance:F2}");
    Console.WriteLine($"   Total Transactions: {state.TransactionCount}");
    Console.WriteLine($"   Event Version: v{agent2.GetCurrentVersion()}\n");

    // ============================================================
    // MongoDB Collection Information
    // ============================================================
    Console.WriteLine("📍 MongoDB Collection Details");
    Console.WriteLine("══════════════════════════════════════════════════\n");
    
    Console.WriteLine("🗄️  Collection Location:");
    Console.WriteLine($"   Database: {mongoDatabase}");
    Console.WriteLine($"   Collection: {collectionPrefix}-EventStorageState");
    Console.WriteLine($"   Document ID: {accountId}");
    Console.WriteLine($"\n💡 View data using:");
    Console.WriteLine($"   - MongoDB Compass: {mongoConnectionString}");
    Console.WriteLine($"   - Mongo Shell: mongosh {mongoDatabase}");
    Console.WriteLine($"   - Query: db['{collectionPrefix}-EventStorageState'].find().pretty()\n");

    Console.WriteLine("✨ Demo completed successfully!");
    Console.WriteLine("\n🎯 Key Architecture Points:");
    Console.WriteLine("   1. OrleansEventStore wraps IEventStorageGrain");
    Console.WriteLine("   2. IEventStorageGrain provides Orleans concurrency control");
    Console.WriteLine("   3. Orleans GrainStorage persists to MongoDB");
    Console.WriteLine("   4. Collection name = CollectionPrefix + '-EventStorageState'");
    Console.WriteLine($"   5. This demo uses: '{collectionPrefix}-EventStorageState'");
    Console.WriteLine("   6. Orleans handles serialization automatically (Protobuf)");
    Console.WriteLine("   7. Grain deactivation/reactivation is transparent\n");
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ Error: {ex.Message}");
    Console.WriteLine($"   Type: {ex.GetType().Name}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"   Inner: {ex.InnerException.Message}");
    }
    Console.WriteLine($"\n💡 Make sure MongoDB is running:");
    Console.WriteLine($"   docker run -d -p 27017:27017 --name mongodb mongo:7.0");
    Console.WriteLine($"   OR: cd examples/MongoDBEventStoreDemo && docker-compose up -d\n");
    
    // Print stack trace for debugging
    Console.WriteLine($"\n📋 Stack Trace:");
    Console.WriteLine(ex.StackTrace);
}
finally
{
    Console.WriteLine("\n🛑 Shutting down Orleans Silo...");
    await host.StopAsync();
    Console.WriteLine("✅ Orleans Silo stopped.\n");
}
