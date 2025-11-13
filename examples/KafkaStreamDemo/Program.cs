using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Runtime.Orleans;
using Kafka.Demo;
using KafkaStreamDemo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Streams;
using Orleans.Streams.Kafka.Config;
using Orleans.Streams.Kafka.Core;

// ========== Orleans Kafka Stream Demo ==========
// This demo demonstrates Orleans Stream integration with Apache Kafka
// Key: Topic name MUST match Stream Namespace for proper message routing!

Console.WriteLine("=== Orleans Kafka Stream Demo ===");
Console.WriteLine("Configuration loaded from appsettings.json\n");

// Build and run Orleans host
var host = CreateHostBuilder(args).Build();

Console.WriteLine("Starting Orleans Silo...");
await host.StartAsync();

Console.WriteLine("Orleans Silo started successfully!");
Console.WriteLine("Waiting for silo to be ready...\n");
await Task.Delay(3000);

// Get the actor manager
var actorManager = host.Services.GetRequiredService<IGAgentActorManager>();

// Demo scenario
await RunDemoAsync(actorManager);

Console.WriteLine("\n=== Demo execution completed ===");
Console.WriteLine("Shutting down...");

await Task.Delay(2000); // Allow final logs to flush

await host.StopAsync();
await host.WaitForShutdownAsync();

static IHostBuilder CreateHostBuilder(string[] args)
{
    return Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((context, config) =>
        {
            // Ensure appsettings.json is loaded
            config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        })
        .UseOrleans((context, siloBuilder) =>
        {
            // Configure Orleans with Kafka Stream
            ConfigureOrleans(siloBuilder, context.Configuration);
        })
        .ConfigureServices((context, services) =>
        {
            // Configure Streaming Options from appsettings.json
            services.Configure<StreamingOptions>(
                context.Configuration.GetSection("StreamingOptions"));
            
            // Configure Kafka Options from appsettings.json
            services.Configure<KafkaConfiguration>(
                context.Configuration.GetSection("Kafka"));
            
            // Register Orleans Agents support
            services.AddOrleansAgents();
            
            // Register Actor Manager
            services.AddSingleton<IGAgentActorManager, OrleansGAgentActorManager>();
            
            // Register Auto-Discovery Factory Provider
            services.AddSingleton<IGAgentActorFactoryProvider, AutoDiscoveryGAgentActorFactoryProvider>();
        })
        .ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });
}

static void ConfigureOrleans(ISiloBuilder siloBuilder, IConfiguration configuration)
{
    // Load Kafka configuration from appsettings.json
    var kafkaConfig = configuration.GetSection("Kafka").Get<KafkaConfiguration>() 
        ?? new KafkaConfiguration();
    
    var streamingOptions = configuration.GetSection("StreamingOptions").Get<StreamingOptions>()
        ?? new StreamingOptions();
    
    Console.WriteLine($"Kafka Brokers: {string.Join(", ", kafkaConfig.Brokers)}");
    Console.WriteLine($"Stream Namespace: {streamingOptions.DefaultStreamNamespace}");
    Console.WriteLine($"Consumer Group: {kafkaConfig.ConsumerGroupId}");
    
    siloBuilder
        // Use localhost clustering for demo
        .UseLocalhostClustering()
        
        // Configure cluster
        .Configure<ClusterOptions>(options =>
        {
            options.ClusterId = "kafka-stream-demo";
            options.ServiceId = "KafkaStreamService";
        })
        
        // Configure endpoints
        .ConfigureEndpoints(siloPort: 11111, gatewayPort: 30000)
        
        // Add Memory Grain Storage (for state persistence)
        .AddMemoryGrainStorage("Default")
        
        // Add Memory Grain Storage for PubSub (Orleans Stream subscription management)
        .AddMemoryGrainStorage("PubSubStore")
        
        // Configure Kafka Streams (loaded from appsettings.json)
        // IMPORTANT: Topic name must match Stream Namespace!
        .AddPersistentStreams(streamingOptions.StreamProviderName, KafkaAdapterFactory.Create, b =>
        {
            b.ConfigureStreamPubSub(StreamPubSubType.ExplicitGrainBasedAndImplicit);
            b.Configure<KafkaStreamOptions>(ob => ob.Configure(options =>
            {
                options.BrokerList = kafkaConfig.Brokers;
                options.ConsumerGroupId = kafkaConfig.ConsumerGroupId;
                
                // Parse ConsumeMode from configuration
                options.ConsumeMode = kafkaConfig.ConsumeMode switch
                {
                    "LastCommittedMessage" => ConsumeMode.LastCommittedMessage,
                    "StreamStart" => ConsumeMode.StreamStart,
                    "StreamEnd" => ConsumeMode.StreamEnd,
                    _ => ConsumeMode.LastCommittedMessage
                };
                
                options.PollTimeout = TimeSpan.FromMilliseconds(kafkaConfig.PollTimeoutMs);
                
                // Add all topics from configuration
                // CRITICAL: Topic names MUST match StreamingOptions.DefaultStreamNamespace!
                foreach (var topic in kafkaConfig.Topics)
                {
                    Console.WriteLine($"  Adding topic: {topic.Name} (Partitions: {topic.Partitions}, RF: {topic.ReplicationFactor})");
                    options.AddTopic(topic.Name, new TopicCreationConfig
                    {
                        AutoCreate = topic.AutoCreate,
                        Partitions = topic.Partitions,
                        ReplicationFactor = topic.ReplicationFactor
                    });
                }
            }));
            
            // Configure pulling agent performance from config
            b.ConfigurePullingAgent(ob => ob.Configure(options =>
            {
                options.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(
                    kafkaConfig.Performance.GetQueueMsgsTimerPeriodMs);
            }));
        })
        
        // Configure messaging options
        .Configure<SiloMessagingOptions>(options =>
        {
            options.ResponseTimeout = TimeSpan.FromMinutes(5);
            options.SystemResponseTimeout = TimeSpan.FromMinutes(5);
        });
    
                   Console.WriteLine("✓ Orleans configured with Kafka Stream support");
}

static async Task RunDemoAsync(IGAgentActorManager actorManager)
{
    Console.WriteLine("\n=== Starting Kafka Stream Demo ===\n");
    
    try
    {
        // Create producer agent
        Console.WriteLine("1. Creating Kafka Producer Agent...");
        var producerId = Guid.NewGuid();
        var producerActor = await actorManager.CreateAndRegisterAsync<KafkaProducerAgent>(producerId);
        Console.WriteLine($"   ✓ Producer created: {producerId}\n");
        
        // Create consumer agent  
        Console.WriteLine("2. Creating Kafka Consumer Agent...");
        var consumerId = Guid.NewGuid();
        var consumerActor = await actorManager.CreateAndRegisterAsync<KafkaConsumerAgent>(consumerId);
        Console.WriteLine($"   ✓ Consumer created: {consumerId}\n");
        
               // Set up stream subscription: Consumer subscribes to Producer's stream
               Console.WriteLine("3. Setting up stream subscription...");
               await consumerActor.SetParentAsync(producerId);
               Console.WriteLine($"   ✓ Consumer subscribed to Producer's stream (Orleans Kafka Stream)\n");
        
        // Wait for agents and subscriptions to be fully initialized
        await Task.Delay(2000);
        
        // Publish messages
        Console.WriteLine("4. Publishing messages to Orleans Stream...");
        var producer = (KafkaProducerAgent)producerActor.GetAgent();
        
        // Create messages through Agent, publish through Actor
        var msg1 = producer.CreateMessage("Hello from Orleans Stream!");
        await producerActor.PublishEventAsync(msg1);
        
        var msg2 = producer.CreateMessage("This message goes through Kafka");
        await producerActor.PublishEventAsync(msg2);
        
        var msg3 = producer.CreateMessage("Event-driven architecture in action!");
        await producerActor.PublishEventAsync(msg3);
        
        Console.WriteLine("   ✓ Messages published\n");
        
        // Wait for messages to be consumed
        await Task.Delay(3000);
        
        // Publish a batch of messages
        Console.WriteLine("5. Publishing batch of messages...");
        var batchContents = Enumerable.Range(1, 5)
            .Select(i => $"Batch message #{i} - {DateTime.UtcNow:HH:mm:ss.fff}")
            .ToList();
        
        var batchMessages = producer.CreateBatch(batchContents);
        foreach (var msg in batchMessages)
        {
            await producerActor.PublishEventAsync(msg);
        }
        
        Console.WriteLine("   ✓ Batch published\n");
        
        // Wait for batch processing
        await Task.Delay(3000);
        
        // Get producer state from Grain (not from local actor instance)
        Console.WriteLine("6. Checking Producer State...");
        var producerOrleansActor = (Aevatar.Agents.Runtime.Orleans.OrleansGAgentActor)producerActor;
        var producerState = await producerOrleansActor.GetStateFromGrainAsync<KafkaProducerState>();
        
        if (producerState != null)
        {
            Console.WriteLine($"   • Messages Published: {producerState.MessagesPublished}");
            Console.WriteLine($"   • Total Bytes Sent: {producerState.TotalBytesSent}");
            Console.WriteLine($"   • Last Publish: {producerState.LastPublishTime?.ToDateTime():HH:mm:ss}\n");
        }
        
        // Get consumer state from Grain (not from local actor instance)
        Console.WriteLine("7. Checking Consumer State...");
        var consumerOrleansActor = (Aevatar.Agents.Runtime.Orleans.OrleansGAgentActor)consumerActor;
        var consumerState = await consumerOrleansActor.GetStateFromGrainAsync<KafkaConsumerState>();
        
        if (consumerState != null)
        {
            Console.WriteLine($"   • Messages Consumed: {consumerState.MessagesConsumed}");
            Console.WriteLine($"   • Total Bytes Received: {consumerState.TotalBytesReceived}");
            Console.WriteLine($"   • Subscription Status: {consumerState.SubscriptionStatus}");
            Console.WriteLine($"   • Last Consume: {consumerState.LastConsumeTime?.ToDateTime():HH:mm:ss}\n");
        }
        else
        {
            Console.WriteLine("   ⚠️ Unable to retrieve consumer state\n");
        }
        
        // Publish metrics
        Console.WriteLine("8. Publishing metrics...");
        var metrics = producer.CreateMetrics();
        await producerActor.PublishEventAsync(metrics);
        
        await Task.Delay(2000);
        
               Console.WriteLine("\n=== Demo Completed Successfully ===");
               Console.WriteLine("\nKey Takeaways:");
               Console.WriteLine("✓ Orleans Kafka Stream integration works");
               Console.WriteLine("✓ Messages are persisted to Kafka topics");
               Console.WriteLine("✓ Agents use event handlers to process Kafka messages");
               Console.WriteLine("✓ Stream subscription enables automatic message routing");
               Console.WriteLine("✓ State is maintained across message processing");
               Console.WriteLine("✓ Supports batch publishing and metrics tracking");
               Console.WriteLine("\n📝 Kafka topic: aevatar-agents-events");
               Console.WriteLine("📝 Consumer group: aevatar-consumer-group");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n❌ Error during demo: {ex.Message}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
    }
}

