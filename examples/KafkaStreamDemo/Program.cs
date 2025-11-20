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

// Delay time constants (in milliseconds)
// These delays allow Orleans/Kafka infrastructure to initialize and process messages
const int SiloReadyDelayMs = 3000;           // Time for Orleans Silo to fully initialize
const int AgentInitializationDelayMs = 2000; // Time for agents and subscriptions to initialize
const int MessageProcessingDelayMs = 3000;    // Time for messages to be consumed and processed
const int BatchProcessingDelayMs = 3000;     // Time for batch messages to be processed
const int MetricsProcessingDelayMs = 2000;   // Time for metrics to be processed
const int ShutdownFlushDelayMs = 2000;       // Time to allow final logs to flush

Console.WriteLine("=== Orleans Kafka Stream Demo ===");
Console.WriteLine("Configuration loaded from appsettings.json\n");

// Build and run Orleans host
var host = CreateHostBuilder(args).Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Starting Orleans Kafka Stream Demo");

Console.WriteLine("Starting Orleans Silo...");
logger.LogDebug("Initializing Orleans Silo...");
await host.StartAsync();

Console.WriteLine("Orleans Silo started successfully!");
logger.LogInformation("Orleans Silo started successfully");
Console.WriteLine("Waiting for silo to be ready...\n");
logger.LogDebug("Waiting {DelayMs}ms for silo to be ready", SiloReadyDelayMs);
await Task.Delay(SiloReadyDelayMs);

// Get the actor manager
var actorManager = host.Services.GetRequiredService<IGAgentActorManager>();

// Demo scenario
await RunDemoAsync(actorManager, logger);

Console.WriteLine("\n=== Demo execution completed ===");
logger.LogInformation("Demo execution completed");
Console.WriteLine("Shutting down...");
logger.LogDebug("Shutting down, allowing {DelayMs}ms for logs to flush", ShutdownFlushDelayMs);

await Task.Delay(ShutdownFlushDelayMs);

await host.StopAsync();
await host.WaitForShutdownAsync();
logger.LogInformation("Orleans host stopped");

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
            var loggerFactory = context.Services.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("OrleansConfiguration");
            ConfigureOrleans(siloBuilder, context.Configuration, logger);
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

static void ConfigureOrleans(ISiloBuilder siloBuilder, IConfiguration configuration, ILogger logger)
{
    // Load Kafka configuration from appsettings.json
    var kafkaConfig = configuration.GetSection("Kafka").Get<KafkaConfiguration>() 
        ?? new KafkaConfiguration();
    
    var streamingOptions = configuration.GetSection("StreamingOptions").Get<StreamingOptions>()
        ?? new StreamingOptions();
    
    // User-friendly console output for demo
    Console.WriteLine($"Kafka Brokers: {string.Join(", ", kafkaConfig.Brokers)}");
    Console.WriteLine($"Stream Namespace: {streamingOptions.DefaultStreamNamespace}");
    Console.WriteLine($"Consumer Group: {kafkaConfig.ConsumerGroupId}");
    
    // Technical logging
    logger.LogInformation("Configuring Orleans with Kafka Stream");
    logger.LogDebug("Kafka Brokers: {Brokers}", string.Join(", ", kafkaConfig.Brokers));
    logger.LogDebug("Stream Namespace: {Namespace}", streamingOptions.DefaultStreamNamespace);
    logger.LogDebug("Consumer Group: {ConsumerGroup}", kafkaConfig.ConsumerGroupId);
    
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
                    // User-friendly console output
                    Console.WriteLine($"  Adding topic: {topic.Name} (Partitions: {topic.Partitions}, RF: {topic.ReplicationFactor})");
                    // Technical logging
                    logger.LogDebug("Adding Kafka topic: {TopicName}, Partitions: {Partitions}, ReplicationFactor: {ReplicationFactor}, AutoCreate: {AutoCreate}",
                        topic.Name, topic.Partitions, topic.ReplicationFactor, topic.AutoCreate);
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
    
    // User-friendly console output
    Console.WriteLine("✓ Orleans configured with Kafka Stream support");
    // Technical logging
    logger.LogInformation("Orleans configured with Kafka Stream support");
}

static async Task RunDemoAsync(IGAgentActorManager actorManager, ILogger logger)
{
    Console.WriteLine("\n=== Starting Kafka Stream Demo ===\n");
    logger.LogInformation("Starting Kafka Stream Demo");
    
    try
    {
        // Create producer agent
        Console.WriteLine("1. Creating Kafka Producer Agent...");
        logger.LogDebug("Creating Kafka Producer Agent");
        var producerId = Guid.NewGuid();
        var producerActor = await actorManager.CreateAndRegisterAsync<KafkaProducerAgent>(producerId);
        Console.WriteLine($"   ✓ Producer created: {producerId}\n");
        logger.LogInformation("Producer Agent created: {ProducerId}", producerId);
        
        // Create consumer agent  
        Console.WriteLine("2. Creating Kafka Consumer Agent...");
        logger.LogDebug("Creating Kafka Consumer Agent");
        var consumerId = Guid.NewGuid();
        var consumerActor = await actorManager.CreateAndRegisterAsync<KafkaConsumerAgent>(consumerId);
        Console.WriteLine($"   ✓ Consumer created: {consumerId}\n");
        logger.LogInformation("Consumer Agent created: {ConsumerId}", consumerId);
        
        // Set up stream subscription: Consumer subscribes to Producer's stream
        Console.WriteLine("3. Setting up stream subscription...");
        logger.LogDebug("Setting up stream subscription: Consumer {ConsumerId} -> Producer {ProducerId}", consumerId, producerId);
        await consumerActor.SetParentAsync(producerId);
        Console.WriteLine($"   ✓ Consumer subscribed to Producer's stream (Orleans Kafka Stream)\n");
        logger.LogInformation("Consumer {ConsumerId} subscribed to Producer {ProducerId} stream", consumerId, producerId);
        
        // Wait for agents and subscriptions to be fully initialized
        logger.LogDebug("Waiting {DelayMs}ms for agents and subscriptions to initialize", AgentInitializationDelayMs);
        await Task.Delay(AgentInitializationDelayMs);
        
        // Publish messages
        Console.WriteLine("4. Publishing messages to Orleans Stream...");
        logger.LogDebug("Publishing messages to Orleans Stream");
        var producer = (KafkaProducerAgent)producerActor.GetAgent();
        
        // Create messages through Agent, publish through Actor
        var msg1 = producer.CreateMessage("Hello from Orleans Stream!");
        await producerActor.PublishEventAsync(msg1);
        logger.LogDebug("Published message 1: {MessageId}", msg1.MessageId);
        
        var msg2 = producer.CreateMessage("This message goes through Kafka");
        await producerActor.PublishEventAsync(msg2);
        logger.LogDebug("Published message 2: {MessageId}", msg2.MessageId);
        
        var msg3 = producer.CreateMessage("Event-driven architecture in action!");
        await producerActor.PublishEventAsync(msg3);
        logger.LogDebug("Published message 3: {MessageId}", msg3.MessageId);
        
        Console.WriteLine("   ✓ Messages published\n");
        logger.LogInformation("Published 3 messages to Orleans Stream");
        
        // Wait for messages to be consumed
        logger.LogDebug("Waiting {DelayMs}ms for messages to be consumed and processed", MessageProcessingDelayMs);
        await Task.Delay(MessageProcessingDelayMs);
        
        // Publish a batch of messages
        Console.WriteLine("5. Publishing batch of messages...");
        logger.LogDebug("Publishing batch of messages");
        var batchContents = Enumerable.Range(1, 5)
            .Select(i => $"Batch message #{i} - {DateTime.UtcNow:HH:mm:ss.fff}")
            .ToList();
        
        var batchMessages = producer.CreateBatch(batchContents);
        logger.LogDebug("Created batch of {Count} messages", batchMessages.Count);
        foreach (var msg in batchMessages)
        {
            await producerActor.PublishEventAsync(msg);
        }
        
        Console.WriteLine("   ✓ Batch published\n");
        logger.LogInformation("Published batch of {Count} messages", batchMessages.Count);
        
        // Wait for batch processing
        logger.LogDebug("Waiting {DelayMs}ms for batch messages to be processed", BatchProcessingDelayMs);
        await Task.Delay(BatchProcessingDelayMs);
        
        // Get producer state from Grain (not from local actor instance)
        Console.WriteLine("6. Checking Producer State...");
        logger.LogDebug("Retrieving producer state from Grain");
        var producerOrleansActor = (Aevatar.Agents.Runtime.Orleans.OrleansGAgentActor)producerActor;
        var producerState = await producerOrleansActor.GetStateFromGrainAsync<KafkaProducerState>();
        
        if (producerState != null)
        {
            Console.WriteLine($"   • Messages Published: {producerState.MessagesPublished}");
            Console.WriteLine($"   • Total Bytes Sent: {producerState.TotalBytesSent}");
            Console.WriteLine($"   • Last Publish: {producerState.LastPublishTime?.ToDateTime():HH:mm:ss}\n");
            logger.LogInformation("Producer State - Messages: {Count}, Bytes: {Bytes}",
                producerState.MessagesPublished, producerState.TotalBytesSent);
        }
        else
        {
            logger.LogWarning("Unable to retrieve producer state from Grain");
        }
        
        // Get consumer state from Grain (not from local actor instance)
        Console.WriteLine("7. Checking Consumer State...");
        logger.LogDebug("Retrieving consumer state from Grain");
        var consumerOrleansActor = (Aevatar.Agents.Runtime.Orleans.OrleansGAgentActor)consumerActor;
        var consumerState = await consumerOrleansActor.GetStateFromGrainAsync<KafkaConsumerState>();
        
        if (consumerState != null)
        {
            Console.WriteLine($"   • Messages Consumed: {consumerState.MessagesConsumed}");
            Console.WriteLine($"   • Total Bytes Received: {consumerState.TotalBytesReceived}");
            Console.WriteLine($"   • Subscription Status: {consumerState.SubscriptionStatus}");
            Console.WriteLine($"   • Last Consume: {consumerState.LastConsumeTime?.ToDateTime():HH:mm:ss}\n");
            logger.LogInformation("Consumer State - Messages: {Count}, Bytes: {Bytes}, Status: {Status}",
                consumerState.MessagesConsumed, consumerState.TotalBytesReceived, consumerState.SubscriptionStatus);
        }
        else
        {
            Console.WriteLine("   ⚠️ Unable to retrieve consumer state\n");
            logger.LogWarning("Unable to retrieve consumer state from Grain");
        }
        
        // Publish metrics
        Console.WriteLine("8. Publishing metrics...");
        logger.LogDebug("Publishing metrics");
        var metrics = producer.CreateMetrics();
        await producerActor.PublishEventAsync(metrics);
        logger.LogInformation("Published metrics event");
        
        logger.LogDebug("Waiting {DelayMs}ms for metrics to be processed", MetricsProcessingDelayMs);
        await Task.Delay(MetricsProcessingDelayMs);
        
        Console.WriteLine("\n=== Demo Completed Successfully ===");
        logger.LogInformation("Demo completed successfully");
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
        logger.LogError(ex, "Error during demo execution");
        throw; // Re-throw to allow caller to handle
    }
}

