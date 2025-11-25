using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.EventRouting;
using Aevatar.Agents.Runtime.Orleans;
using Business.Server;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Aevatar.BusinessServer.PerformanceTests;

/// <summary>
/// Multi-Topic Multi-Agent Benchmark
/// Tests TRUE multi-topic Orleans Stream behavior
/// 
/// TRUE Multi-Topic Architecture:
/// - Each agent type uses a different Stream Namespace
/// - TypeA → "AevatarAgents-TypeA" → Kafka Topic "AevatarAgents-TypeA"
/// - TypeB → "AevatarAgents-TypeB" → Kafka Topic "AevatarAgents-TypeB"
/// - etc.
/// 
/// Test Scenario:
/// - 5 different agent types (Type A, B, C, D, E)
/// - 20 agents per type (total 100 agents) - reduced to avoid MongoDB connection pool issues
/// - Each type has its own Kafka topic
/// - Verify true topic isolation at Kafka level
/// </summary>
public class MultiTopicBenchmark
{
    private readonly Dictionary<string, IGAgentActorManager> _actorManagersByType;
    private readonly ILogger _logger;
    private readonly IClusterClient _clusterClient;
    
    // Test configuration
    private const int AGENT_TYPES = 5;
    private const int AGENTS_PER_TYPE = 20;  // Reduced from 100 to avoid MongoDB connection pool issues
    
    // Scenario configuration
    private int _messagesPerType = 100;  // Can be adjusted per test
    
    // Stream namespaces for each type
    private static readonly Dictionary<string, string> TypeNamespaces = new()
    {
        { "TypeA", "AevatarAgents-TypeA" },
        { "TypeB", "AevatarAgents-TypeB" },
        { "TypeC", "AevatarAgents-TypeC" },
        { "TypeD", "AevatarAgents-TypeD" },
        { "TypeE", "AevatarAgents-TypeE" }
    };
    
    public MultiTopicBenchmark(IClusterClient clusterClient, ILogger logger)
    {
        _clusterClient = clusterClient;
        _logger = logger;
        _actorManagersByType = new Dictionary<string, IGAgentActorManager>();
    }
    
    private async Task InitializeActorManagersAsync()
    {
        Console.WriteLine("🔧 Initializing Actor Managers for each type...");
        
        foreach (var kvp in TypeNamespaces)
        {
            var typeName = kvp.Key;
            var namespace_ = kvp.Value;
            
            var manager = await CreateActorManagerForNamespace(namespace_);
            _actorManagersByType[typeName] = manager;
            
            Console.WriteLine($"   ✅ {typeName}: Namespace='{namespace_}'");
        }
        
        Console.WriteLine();
    }
    
    private async Task<IGAgentActorManager> CreateActorManagerForNamespace(string streamNamespace)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(_clusterClient);
        services.AddSingleton<IGrainFactory>(_clusterClient);
        services.AddSingleton<IClusterClient>(_clusterClient);

        // Configure with type-specific namespace
        services.Configure<Aevatar.Agents.StreamingOptions>(options =>
        {
            options.StreamProviderName = "Default";
            options.DefaultStreamNamespace = streamNamespace;  // Type-specific namespace!
        });

        // Register factory provider
        services.AddGAgentActorFactoryProvider();
        
        // Register IGAgentFactory for creating agent instances
        services.AddSingleton<IGAgentFactory, AIGAgentFactory>();
        
        // Register IGAgentActorFactory for Orleans runtime
        services.AddSingleton<IGAgentActorFactory, OrleansGAgentActorFactory>();
        services.AddSingleton<IGAgentActorManager, OrleansGAgentActorManager>();

        var serviceProvider = services.BuildServiceProvider();
        var manager = serviceProvider.GetRequiredService<IGAgentActorManager>();
        
        // Important: Wait a bit to avoid overwhelming the system
        await Task.Delay(100);
        
        return manager;
    }
    
    /// <summary>
    /// Scenario 1: Two types SHARE ONE Topic (Single Topic Mode)
    /// Both TypeA and TypeB use the same Namespace → Same Kafka Topic
    /// </summary>
    public async Task<MultiTopicResults> RunSingleTopicTestAsync(int messagesPerType)
    {
        _messagesPerType = messagesPerType;
        Console.WriteLine("\n🎯 Scenario 1: Single Topic (Shared) Test");
        Console.WriteLine("=" .PadRight(60, '='));
        Console.WriteLine($"📋 TypeA and TypeB SHARE the same topic");
        Console.WriteLine($"📋 Both use Namespace: 'AevatarAgents-Shared'");
        Console.WriteLine($"Configuration:");
        Console.WriteLine($"  • Agent Types: 2 (Type A, B) on SAME topic");
        Console.WriteLine($"  • Agents per Type: {AGENTS_PER_TYPE}");
        Console.WriteLine($"  • Total Agents: {AGENTS_PER_TYPE * 2}");
        Console.WriteLine($"  • Messages: TypeA × {messagesPerType} + TypeB × {messagesPerType} = {messagesPerType * 2} total");
        Console.WriteLine();
        
        var results = new MultiTopicResults();
        var sw = Stopwatch.StartNew();
        
        // Step 1: Create agents for TypeA and TypeB using SHARED namespace
        Console.WriteLine("📦 Step 1: Creating agents with SHARED namespace...");
        await CreateAgentsWithSharedNamespaceAsync(results);
        Console.WriteLine($"   ✅ Created {results.TotalAgentsCreated} agents in {sw.ElapsedMilliseconds}ms\n");
        
        // Step 2: Create two publishers (both on the same topic)
        sw.Restart();
        Console.WriteLine("📡 Step 2: Creating publisher agents (both on shared topic)...");
        var sharedManager = await CreateActorManagerForNamespace("AevatarAgents-Shared");
        
        var publisherA_Id = Guid.NewGuid();
        var publisherA = await sharedManager.CreateAndRegisterAsync<TypedAgent>(publisherA_Id);
        var publisherA_Agent = (TypedAgent)publisherA.GetAgent();
        publisherA_Agent.AgentType = "PublisherA";
        Console.WriteLine($"   ✅ PublisherA created: {publisherA_Id}");
        
        var publisherB_Id = Guid.NewGuid();
        var publisherB = await sharedManager.CreateAndRegisterAsync<TypedAgent>(publisherB_Id);
        var publisherB_Agent = (TypedAgent)publisherB.GetAgent();
        publisherB_Agent.AgentType = "PublisherB";
        Console.WriteLine($"   ✅ PublisherB created: {publisherB_Id}\n");
        
        // Step 3: Subscribe Type A to PublisherA, Type B to PublisherB (same topic!)
        sw.Restart();
        Console.WriteLine("🔗 Step 3: Setting up subscriptions (SAME TOPIC)...");
        
        var typeAAgents = results.AgentsByType["TypeA"];
        foreach (var agentInfo in typeAAgents)
        {
            await agentInfo.Actor.SetParentAsync(publisherA.Id);
            await publisherA.AddChildAsync(agentInfo.AgentId);
        }
        Console.WriteLine($"   ✅ Type A subscribed to PublisherA ({typeAAgents.Count} agents)");
        
        var typeBAgents = results.AgentsByType["TypeB"];
        foreach (var agentInfo in typeBAgents)
        {
            await agentInfo.Actor.SetParentAsync(publisherB.Id);
            await publisherB.AddChildAsync(agentInfo.AgentId);
        }
        Console.WriteLine($"   ✅ Type B subscribed to PublisherB ({typeBAgents.Count} agents)");
        Console.WriteLine($"   ✅ Subscriptions established in {sw.ElapsedMilliseconds}ms\n");
        
        results.SubscribedAgents = typeAAgents.Count + typeBAgents.Count;
        
        // Wait for subscriptions to be fully active
        Console.WriteLine("⏳ Waiting 3s for subscriptions to stabilize...");
        await Task.Delay(3000);
        
        // Step 4: Publish messages to both Type A and Type B (parallel, same topic)
        sw.Restart();
        Console.WriteLine($"📤 Step 4: Publishing messages (PARALLEL on SAME topic)...");
        
        var publishTaskA = Task.Run(async () =>
        {
            Console.WriteLine($"   📤 Publishing {messagesPerType} to Type A...");
            await PublishMessagesAsync(publisherA, results, "TypeA", messagesPerType);
            Console.WriteLine($"   ✅ Type A publishing complete");
        });
        
        var publishTaskB = Task.Run(async () =>
        {
            Console.WriteLine($"   📤 Publishing {messagesPerType} to Type B...");
            await PublishMessagesAsync(publisherB, results, "TypeB", messagesPerType);
            Console.WriteLine($"   ✅ Type B publishing complete");
        });
        
        await Task.WhenAll(publishTaskA, publishTaskB);
        
        results.PublishDurationMs = (int)sw.ElapsedMilliseconds;
        Console.WriteLine($"   ✅ All publishing complete in {results.PublishDurationMs}ms\n");
        
        // Wait for message propagation
        Console.WriteLine("⏳ Waiting 5s for message propagation...");
        await Task.Delay(5000);
        
        // Step 5: Collect statistics
        Console.WriteLine("📊 Step 5: Collecting statistics...");
        await CollectStatisticsAsync(results);
        
        results.TotalDurationMs = (int)sw.ElapsedMilliseconds;
        
        return results;
    }
    
    private async Task CreateAgentsWithSharedNamespaceAsync(MultiTopicResults results)
    {
        // Create a SINGLE manager for the shared namespace
        var sharedNamespace = "AevatarAgents-Shared";
        Console.WriteLine($"🔧 Creating Actor Manager with SHARED namespace: '{sharedNamespace}'");
        var sharedManager = await CreateActorManagerForNamespace(sharedNamespace);
        
        var typeNames = new[] { "TypeA", "TypeB" };
        
        for (int typeIndex = 0; typeIndex < 2; typeIndex++)
        {
            var typeName = typeNames[typeIndex];
            var typeAgents = new List<TypedAgentInfo>();
            
            Console.WriteLine($"   Creating {typeName} agents (Namespace: {sharedNamespace})...");
            
            for (int i = 0; i < AGENTS_PER_TYPE; i++)
            {
                var agentId = Guid.NewGuid();
                var actor = await sharedManager.CreateAndRegisterAsync<TypedAgent>(agentId);
                var agent = (TypedAgent)actor.GetAgent();
                
                // Set agent type
                agent.AgentType = typeName;
                agent.StreamNamespace = sharedNamespace;  // Same namespace!
                
                typeAgents.Add(new TypedAgentInfo
                {
                    AgentId = agentId,
                    Actor = actor,
                    Agent = agent,
                    TypeName = typeName
                });
                
                if ((i + 1) % 10 == 0)
                {
                    Console.WriteLine($"      Progress: {i + 1}/{AGENTS_PER_TYPE}");
                }
            }
            
            results.AgentsByType[typeName] = typeAgents;
            results.TotalAgentsCreated += typeAgents.Count;
        }
    }
    
    private async Task CreateAgentsAsync(MultiTopicResults results)
    {
        await InitializeActorManagersAsync();
        
        var typeNames = new[] { "TypeA", "TypeB", "TypeC", "TypeD", "TypeE" };
        
        for (int typeIndex = 0; typeIndex < AGENT_TYPES; typeIndex++)
        {
            var typeName = typeNames[typeIndex];
            var typeAgents = new List<TypedAgentInfo>();
            var manager = _actorManagersByType[typeName];  // Use type-specific manager!
            
            Console.WriteLine($"   Creating {typeName} agents (Namespace: {TypeNamespaces[typeName]})...");
            
            for (int i = 0; i < AGENTS_PER_TYPE; i++)
            {
                var agentId = Guid.NewGuid();
                var actor = await manager.CreateAndRegisterAsync<TypedAgent>(agentId);
                var agent = (TypedAgent)actor.GetAgent();
                
                // Set agent type
                agent.AgentType = typeName;
                agent.StreamNamespace = TypeNamespaces[typeName];  // Store namespace in state
                
                typeAgents.Add(new TypedAgentInfo
                {
                    AgentId = agentId,
                    Actor = actor,
                    Agent = agent,
                    TypeName = typeName
                });
                
                if ((i + 1) % 10 == 0)
                {
                    Console.WriteLine($"      Progress: {i + 1}/{AGENTS_PER_TYPE}");
                }
            }
            
            results.AgentsByType[typeName] = typeAgents;
            results.TotalAgentsCreated += typeAgents.Count;
        }
    }
    
    private async Task SetupSubscriptionsAsync(MultiTopicResults results, IGAgentActor publisher)
    {
        // Only Type A agents subscribe to publisher
        var typeAAgents = results.AgentsByType["TypeA"];
        
        foreach (var agentInfo in typeAAgents)
        {
            await agentInfo.Actor.SetParentAsync(publisher.Id);
            await publisher.AddChildAsync(agentInfo.AgentId);
        }
        
        results.SubscribedAgents = typeAAgents.Count;
    }
    
    private async Task PublishMessagesAsync(IGAgentActor publisher, MultiTopicResults results, string targetType, int messageCount)
    {
        for (int i = 1; i <= messageCount; i++)
        {
            var message = new BusinessMessageEvent
            {
                Message = $"Test message {i} for {targetType} agents",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            
            await publisher.PublishEventAsync(message, Aevatar.Agents.EventDirection.Down);
            
            if (i % 100 == 0 || i == messageCount)
            {
                Console.WriteLine($"      Published: {i}/{messageCount}");
            }
        }
        
        results.MessagesSent += messageCount;
    }
    
    /// <summary>
    /// Scenario 2: Two types send messages concurrently
    /// </summary>
    public async Task<MultiTopicResults> RunDualTypeTestAsync(int messagesPerType)
    {
        _messagesPerType = messagesPerType;
        Console.WriteLine("\n🎯 Scenario 2: Dual Type Test");
        Console.WriteLine("=" .PadRight(60, '='));
        Console.WriteLine($"📋 TypeA sends {messagesPerType}, TypeB sends {messagesPerType}");
        Console.WriteLine($"Configuration:");
        Console.WriteLine($"  • Agent Types: {AGENT_TYPES} (Type A, B, C, D, E)");
        Console.WriteLine($"  • Agents per Type: {AGENTS_PER_TYPE}");
        Console.WriteLine($"  • Total Agents: {AGENT_TYPES * AGENTS_PER_TYPE}");
        Console.WriteLine($"  • Messages: TypeA × {messagesPerType} + TypeB × {messagesPerType} = {messagesPerType * 2} total");
        Console.WriteLine();
        
        var results = new MultiTopicResults();
        var sw = Stopwatch.StartNew();
        
        // Step 1: Create 500 agents
        Console.WriteLine("📦 Step 1: Creating agents...");
        await CreateAgentsAsync(results);
        Console.WriteLine($"   ✅ Created {results.TotalAgentsCreated} agents in {sw.ElapsedMilliseconds}ms\n");
        
        // Step 2: Create two publishers (for TypeA and TypeB)
        sw.Restart();
        Console.WriteLine("📡 Step 2: Creating publisher agents...");
        var publisherA_Id = Guid.NewGuid();
        var publisherA_Manager = _actorManagersByType["TypeA"];
        var publisherA = await publisherA_Manager.CreateAndRegisterAsync<TypedAgent>(publisherA_Id);
        var publisherA_Agent = (TypedAgent)publisherA.GetAgent();
        publisherA_Agent.AgentType = "PublisherA";
        Console.WriteLine($"   ✅ PublisherA created: {publisherA_Id}");
        
        var publisherB_Id = Guid.NewGuid();
        var publisherB_Manager = _actorManagersByType["TypeB"];
        var publisherB = await publisherB_Manager.CreateAndRegisterAsync<TypedAgent>(publisherB_Id);
        var publisherB_Agent = (TypedAgent)publisherB.GetAgent();
        publisherB_Agent.AgentType = "PublisherB";
        Console.WriteLine($"   ✅ PublisherB created: {publisherB_Id}\n");
        
        // Step 3: Subscribe Type A to PublisherA, Type B to PublisherB
        sw.Restart();
        Console.WriteLine("🔗 Step 3: Setting up subscriptions...");
        
        var typeAAgents = results.AgentsByType["TypeA"];
        foreach (var agentInfo in typeAAgents)
        {
            await agentInfo.Actor.SetParentAsync(publisherA.Id);
            await publisherA.AddChildAsync(agentInfo.AgentId);
        }
        Console.WriteLine($"   ✅ Type A subscribed to PublisherA ({typeAAgents.Count} agents)");
        
        var typeBAgents = results.AgentsByType["TypeB"];
        foreach (var agentInfo in typeBAgents)
        {
            await agentInfo.Actor.SetParentAsync(publisherB.Id);
            await publisherB.AddChildAsync(agentInfo.AgentId);
        }
        Console.WriteLine($"   ✅ Type B subscribed to PublisherB ({typeBAgents.Count} agents)");
        Console.WriteLine($"   ✅ Subscriptions established in {sw.ElapsedMilliseconds}ms\n");
        
        results.SubscribedAgents = typeAAgents.Count + typeBAgents.Count;
        
        // Wait for subscriptions to be fully active
        Console.WriteLine("⏳ Waiting 3s for subscriptions to stabilize...");
        await Task.Delay(3000);
        
        // Step 4: Publish messages to both Type A and Type B
        sw.Restart();
        Console.WriteLine($"📤 Step 4: Publishing messages to both Type A and Type B...");
        
        var publishTaskA = Task.Run(async () =>
        {
            Console.WriteLine($"   📤 Publishing {messagesPerType} to Type A...");
            await PublishMessagesAsync(publisherA, results, "TypeA", messagesPerType);
            Console.WriteLine($"   ✅ Type A publishing complete");
        });
        
        var publishTaskB = Task.Run(async () =>
        {
            Console.WriteLine($"   📤 Publishing {messagesPerType} to Type B...");
            await PublishMessagesAsync(publisherB, results, "TypeB", messagesPerType);
            Console.WriteLine($"   ✅ Type B publishing complete");
        });
        
        await Task.WhenAll(publishTaskA, publishTaskB);
        
        results.PublishDurationMs = (int)sw.ElapsedMilliseconds;
        Console.WriteLine($"   ✅ All publishing complete in {results.PublishDurationMs}ms\n");
        
        // Wait for message propagation
        Console.WriteLine("⏳ Waiting 5s for message propagation...");
        await Task.Delay(5000);
        
        // Step 5: Collect statistics
        Console.WriteLine("📊 Step 5: Collecting statistics...");
        await CollectStatisticsAsync(results);
        
        results.TotalDurationMs = (int)sw.ElapsedMilliseconds;
        
        return results;
    }
    
    private async Task CollectStatisticsAsync(MultiTopicResults results)
    {
        foreach (var kvp in results.AgentsByType)
        {
            var typeName = kvp.Key;
            var agents = kvp.Value;
            
            var typeStats = new TypeStatistics
            {
                TypeName = typeName,
                TotalAgents = agents.Count
            };
            
            foreach (var agentInfo in agents)
            {
                var state = agentInfo.Agent.GetState();
                var processedCount = state.ProcessedCount;
                
                typeStats.TotalMessagesReceived += processedCount;
                
                if (processedCount > 0)
                {
                    typeStats.AgentsReceivedMessages++;
                    typeStats.MessageDistribution.Add(processedCount);
                }
            }
            
            // Calculate distribution statistics
            if (typeStats.MessageDistribution.Any())
            {
                typeStats.MinMessages = typeStats.MessageDistribution.Min();
                typeStats.MaxMessages = typeStats.MessageDistribution.Max();
                typeStats.AvgMessages = typeStats.MessageDistribution.Average();
                typeStats.MedianMessages = CalculateMedian(typeStats.MessageDistribution);
            }
            
            results.StatisticsByType[typeName] = typeStats;
        }
    }
    
    private double CalculateMedian(List<int> values)
    {
        var sorted = values.OrderBy(x => x).ToList();
        var count = sorted.Count;
        
        if (count == 0) return 0;
        if (count % 2 == 1)
            return sorted[count / 2];
        else
            return (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
    }
    
    public static void PrintResults(MultiTopicResults results)
    {
        Console.WriteLine("\n" + "=".PadRight(60, '='));
        Console.WriteLine("📊 Test Results Summary");
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine();
        
        Console.WriteLine($"⏱️  Timing:");
        Console.WriteLine($"   • Publish Duration: {results.PublishDurationMs}ms");
        Console.WriteLine($"   • Total Duration: {results.TotalDurationMs}ms");
        Console.WriteLine();
        
        Console.WriteLine($"📤 Messages Sent: {results.MessagesSent}");
        Console.WriteLine($"🔗 Subscribed Agents: {results.SubscribedAgents} (Type A only)");
        Console.WriteLine();
        
        Console.WriteLine("📊 Message Distribution by Type:");
        Console.WriteLine();
        
        foreach (var typeName in new[] { "TypeA", "TypeB", "TypeC", "TypeD", "TypeE" })
        {
            if (!results.StatisticsByType.TryGetValue(typeName, out var stats))
                continue;
            
            var isTargetType = typeName == "TypeA";
            var emoji = isTargetType ? "🎯" : "🚫";
            
            Console.WriteLine($"{emoji} {typeName} ({stats.TotalAgents} agents):");
            Console.WriteLine($"   • Agents Received: {stats.AgentsReceivedMessages}/{stats.TotalAgents} " +
                            $"({stats.AgentsReceivedMessages * 100.0 / stats.TotalAgents:F1}%)");
            Console.WriteLine($"   • Total Messages: {stats.TotalMessagesReceived}");
            
            if (stats.AgentsReceivedMessages > 0)
            {
                Console.WriteLine($"   • Min/Max/Avg/Median: {stats.MinMessages}/{stats.MaxMessages}/" +
                                $"{stats.AvgMessages:F1}/{stats.MedianMessages:F1}");
                
                // Show distribution histogram for subscribed type
                if (isTargetType)
                {
                    PrintDistributionHistogram(stats.MessageDistribution);
                }
            }
            else
            {
                Console.WriteLine($"   • ✅ No messages received (as expected)");
            }
            
            Console.WriteLine();
        }
        
        // Analysis
        Console.WriteLine("🔍 Analysis:");
        Console.WriteLine();
        
        var typeAStats = results.StatisticsByType["TypeA"];
        var otherTypesReceivedMessages = results.StatisticsByType
            .Where(kvp => kvp.Key != "TypeA")
            .Sum(kvp => kvp.Value.TotalMessagesReceived);
        
        if (otherTypesReceivedMessages > 0)
        {
            Console.WriteLine($"⚠️  WARNING: Other types received {otherTypesReceivedMessages} messages!");
            Console.WriteLine($"   This indicates cross-topic leakage or incorrect filtering.");
        }
        else
        {
            Console.WriteLine($"✅ Topic Isolation: Verified (only Type A received messages)");
        }
        
        Console.WriteLine();
        
        if (typeAStats.AgentsReceivedMessages == typeAStats.TotalAgents)
        {
            Console.WriteLine($"✅ All Type A agents received messages");
        }
        else
        {
            Console.WriteLine($"⚠️  Only {typeAStats.AgentsReceivedMessages}/{typeAStats.TotalAgents} Type A agents received messages");
        }
        
        Console.WriteLine();
        
        // Distribution pattern
        var expectedPerAgent = results.MessagesSent;
        var actualAvg = typeAStats.AvgMessages;
        var distributionQuality = actualAvg / expectedPerAgent;
        
        Console.WriteLine($"📈 Distribution Pattern:");
        Console.WriteLine($"   • Expected per agent: {expectedPerAgent} messages");
        Console.WriteLine($"   • Actual average: {actualAvg:F1} messages");
        Console.WriteLine($"   • Distribution quality: {distributionQuality:P1}");
        
        if (distributionQuality > 0.95)
        {
            Console.WriteLine($"   ✅ Excellent distribution (near perfect)");
        }
        else if (distributionQuality > 0.80)
        {
            Console.WriteLine($"   ⚠️  Fair distribution (some imbalance)");
        }
        else
        {
            Console.WriteLine($"   ❌ Poor distribution (significant imbalance)");
        }
        
        Console.WriteLine();
        
        // Bottleneck analysis
        Console.WriteLine($"🔬 Bottleneck Analysis:");
        var variance = CalculateVariance(typeAStats.MessageDistribution);
        Console.WriteLine($"   • Message variance: {variance:F2}");
        
        if (variance < 1.0)
        {
            Console.WriteLine($"   • Pattern: Even distribution (Orleans Stream working well)");
        }
        else if (variance < 10.0)
        {
            Console.WriteLine($"   • Pattern: Slight variance (acceptable queue behavior)");
        }
        else
        {
            Console.WriteLine($"   • Pattern: High variance (possible queue/partition limitation)");
        }
        
        Console.WriteLine();
        Console.WriteLine("=".PadRight(60, '='));
    }
    
    private static void PrintDistributionHistogram(List<int> distribution)
    {
        if (distribution.Count == 0) return;
        
        var grouped = distribution.GroupBy(x => x)
                                   .OrderBy(g => g.Key)
                                   .ToDictionary(g => g.Key, g => g.Count());
        
        Console.WriteLine($"   • Distribution histogram:");
        foreach (var kvp in grouped.Take(10)) // Show top 10 buckets
        {
            var bar = new string('█', Math.Min(kvp.Value / 2, 40));
            Console.WriteLine($"      {kvp.Key} msgs: {bar} ({kvp.Value} agents)");
        }
        
        if (grouped.Count > 10)
        {
            Console.WriteLine($"      ... and {grouped.Count - 10} more buckets");
        }
    }
    
    private static double CalculateVariance(List<int> values)
    {
        if (values.Count == 0) return 0;
        
        var avg = values.Average();
        var sumSquares = values.Sum(x => Math.Pow(x - avg, 2));
        return sumSquares / values.Count;
    }
}

public class MultiTopicResults
{
    public Dictionary<string, List<TypedAgentInfo>> AgentsByType { get; } = new();
    public Dictionary<string, TypeStatistics> StatisticsByType { get; } = new();
    public int TotalAgentsCreated { get; set; }
    public int SubscribedAgents { get; set; }
    public int MessagesSent { get; set; }
    public int PublishDurationMs { get; set; }
    public int TotalDurationMs { get; set; }
}

public class TypedAgentInfo
{
    public Guid AgentId { get; set; }
    public IGAgentActor Actor { get; set; } = null!;
    public TypedAgent Agent { get; set; } = null!;
    public string TypeName { get; set; } = "";
}

public class TypeStatistics
{
    public string TypeName { get; set; } = "";
    public int TotalAgents { get; set; }
    public int AgentsReceivedMessages { get; set; }
    public int TotalMessagesReceived { get; set; }
    public List<int> MessageDistribution { get; } = new();
    public int MinMessages { get; set; }
    public int MaxMessages { get; set; }
    public double AvgMessages { get; set; }
    public double MedianMessages { get; set; }
}

/// <summary>
/// Typed Agent for testing topic isolation
/// Uses Protobuf-generated TypedAgentState
/// </summary>
public class TypedAgent : GAgentBase<TypedAgentState>
{
    public string AgentType
    {
        get => State.AgentType;
        set => State.AgentType = value;
    }
    
    public string StreamNamespace
    {
        get => State.StreamNamespace;
        set => State.StreamNamespace = value;
    }
    
    public new TypedAgentState GetState() => State;
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"{State.AgentType} Agent {Id.ToString().Substring(0, 8)} [NS:{State.StreamNamespace}] - Processed: {State.ProcessedCount}");
    }
    
    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        if (State.ProcessedCount == 0)
        {
            State.AgentId = Id.ToString();
            State.ProcessedCount = 0;
            State.AgentType = "Unknown";
        }
    }
    
    [EventHandler]
    public async Task HandleBusinessMessage(BusinessMessageEvent evt)
    {
        State.ProcessedCount++;
        State.LastMessage = evt.Message;
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        
        Logger.LogDebug("[{AgentType}] Agent {AgentId} received message: {Message}", 
            State.AgentType, Id.ToString().Substring(0, 8), evt.Message);
        
        await Task.CompletedTask;
    }
}

// Note: TypedAgentState is generated from typed_agent.proto

