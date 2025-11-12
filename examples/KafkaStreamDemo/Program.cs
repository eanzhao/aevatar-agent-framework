using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Orleans;
using Google.Protobuf;
using Kafka.Demo;
using KafkaStreamDemo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Streams;
using Orleans.Streams.Kafka.Config;
using Kafka = Orleans.Streams.Kafka.Config;

// ========== Orleans Kafka Stream Demo ==========
// This demo shows how to integrate Orleans Stream with Kafka
// based on the Aevatar Station Orleans configuration pattern

Console.WriteLine("=== Orleans Kafka Stream Demo ===\n");

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

Console.WriteLine("\nPress any key to shutdown...");
Console.ReadKey();

await host.StopAsync();
await host.WaitForShutdownAsync();

static IHostBuilder CreateHostBuilder(string[] args)
{
    return Host.CreateDefaultBuilder(args)
        .UseOrleans((context, siloBuilder) =>
        {
            // Configure Orleans with Kafka Stream
            ConfigureOrleans(siloBuilder);
        })
        .ConfigureServices((context, services) =>
        {
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

static void ConfigureOrleans(ISiloBuilder siloBuilder)
{
    var kafkaBrokers = Environment.GetEnvironmentVariable("KAFKA_BROKERS") 
        ?? "localhost:9092";
    
    Console.WriteLine($"Kafka Brokers: {kafkaBrokers}");
    
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
        
        // Configure Kafka Persistent Streams
        .AddPersistentStreams("AevatarKafka", Kafka.KafkaAdapterFactory.Create, builder =>
        {
            builder.Configure<KafkaStreamOptions>(options =>
            {
                options.BrokerList = kafkaBrokers.Split(',').ToList();
                options.ConsumerGroupId = "aevatar-demo-group";
                options.ConsumeMode = ConsumeMode.StreamEnd; // Start from latest
                
                // Configure topics
                options.AddTopic("demo-topic", new TopicCreationConfig
                {
                    AutoCreate = true,
                    Partitions = 3,
                    ReplicationFactor = 1
                });
                
                options.AddTopic("metrics-topic", new TopicCreationConfig
                {
                    AutoCreate = true,
                    Partitions = 1,
                    ReplicationFactor = 1
                });
            });
            
            builder.ConfigureStreamPubSub(StreamPubSubType.ExplicitGrainBasedAndImplicit);
        })
        
        // Configure Stream PubSub
        .ConfigureApplicationParts(parts =>
        {
            parts.AddFromApplicationBaseDirectory();
        })
        
        // Configure messaging options
        .Configure<SiloMessagingOptions>(options =>
        {
            options.ResponseTimeout = TimeSpan.FromMinutes(5);
            options.SystemResponseTimeout = TimeSpan.FromMinutes(5);
        });
    
    Console.WriteLine("Orleans configured with Kafka Stream support");
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
        
        // Subscribe producer to Kafka stream
        await producerActor.SubscribeToStreamAsync(
            "AevatarKafka", 
            "demo-topic", 
            "demo-topic"
        );
        
        Console.WriteLine($"   ✓ Producer created: {producerId}\n");
        
        // Create consumer agent
        Console.WriteLine("2. Creating Kafka Consumer Agent...");
        var consumerId = Guid.NewGuid();
        var consumerActor = await actorManager.CreateAndRegisterAsync<KafkaConsumerAgent>(consumerId);
        
        // Subscribe consumer to Kafka stream
        await consumerActor.SubscribeToStreamAsync(
            "AevatarKafka",
            "demo-topic",
            "demo-topic"
        );
        
        Console.WriteLine($"   ✓ Consumer created: {consumerId}\n");
        
        // Wait for subscriptions to be established
        await Task.Delay(2000);
        
        // Publish messages
        Console.WriteLine("3. Publishing messages to Kafka...");
        var producer = (KafkaProducerAgent)producerActor.GetAgent();
        
        await producer.PublishMessageAsync("Hello from Orleans Stream!");
        await producer.PublishMessageAsync("This message goes through Kafka");
        await producer.PublishMessageAsync("Event-driven architecture in action!");
        
        Console.WriteLine("   ✓ Messages published\n");
        
        // Wait for messages to be consumed
        await Task.Delay(3000);
        
        // Publish a batch of messages
        Console.WriteLine("4. Publishing batch of messages...");
        var batchMessages = Enumerable.Range(1, 5)
            .Select(i => $"Batch message #{i} - {DateTime.UtcNow:HH:mm:ss.fff}")
            .ToList();
        
        await producer.PublishBatchAsync(batchMessages);
        
        Console.WriteLine("   ✓ Batch published\n");
        
        // Wait for batch processing
        await Task.Delay(3000);
        
        // Get producer state
        Console.WriteLine("5. Checking Producer State...");
        var producerState = await producer.GetStateAsync();
        
        Console.WriteLine($"   • Messages Published: {producerState.MessagesPublished}");
        Console.WriteLine($"   • Total Bytes Sent: {producerState.TotalBytesSent}");
        Console.WriteLine($"   • Last Publish: {producerState.LastPublishTime?.ToDateTime():HH:mm:ss}\n");
        
        // Get consumer state
        Console.WriteLine("6. Checking Consumer State...");
        var consumer = (KafkaConsumerAgent)consumerActor.GetAgent();
        var consumerState = await consumer.GetStateAsync();
        
        Console.WriteLine($"   • Messages Consumed: {consumerState.MessagesConsumed}");
        Console.WriteLine($"   • Total Bytes Received: {consumerState.TotalBytesReceived}");
        Console.WriteLine($"   • Subscription Status: {consumerState.SubscriptionStatus}");
        Console.WriteLine($"   • Last Consume: {consumerState.LastConsumeTime?.ToDateTime():HH:mm:ss}\n");
        
        // Publish metrics
        Console.WriteLine("7. Publishing metrics...");
        await producer.PublishMetricsAsync();
        
        await Task.Delay(2000);
        
        Console.WriteLine("\n=== Demo Completed Successfully ===");
        Console.WriteLine("\nKey Takeaways:");
        Console.WriteLine("✓ Orleans Stream seamlessly integrates with Kafka");
        Console.WriteLine("✓ Agents use event handlers to process Kafka messages");
        Console.WriteLine("✓ Stream subscription enables automatic message routing");
        Console.WriteLine("✓ State is maintained across message processing");
        Console.WriteLine("✓ Supports batch publishing and metrics tracking");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n❌ Error during demo: {ex.Message}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
    }
}

