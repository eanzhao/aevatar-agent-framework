using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime;
using Aevatar.Agents.Runtime.Local.Extensions;
using Aevatar.Agents.Runtime.Orleans.Extensions;
using Aevatar.Agents.Runtime.ProtoActor.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RuntimeAbstractionDemo.Agents;

namespace RuntimeAbstractionDemo;

/// <summary>
/// Demonstrates the use of runtime abstractions in the Aevatar Agent Framework.
/// Shows how the same agent code can run on different runtimes (Local, Orleans, ProtoActor).
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("====================================");
        Console.WriteLine("Runtime Abstraction Demo");
        Console.WriteLine("====================================");
        Console.WriteLine();

        // Get runtime type from command line or default to Local
        var runtimeType = args.Length > 0 ? args[0].ToLower() : "local";
        
        Console.WriteLine($"Selected Runtime: {runtimeType}");
        Console.WriteLine();

        try
        {
            // Build the host with the selected runtime
            var host = CreateHostBuilder(runtimeType).Build();
            
            // Get the runtime from DI
            var runtime = host.Services.GetRequiredService<IAgentRuntime>();
            
            // Run the demo
            await RunDemo(runtime, runtimeType);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
        }

        Console.WriteLine();
        Console.WriteLine("Demo completed. Press any key to exit.");
        Console.ReadKey();
    }

    static IHostBuilder CreateHostBuilder(string runtimeType)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Configure logging
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });

                // Add the selected runtime
                switch (runtimeType)
                {
                    case "local":
                        services.AddLocalAgentRuntime(config =>
                        {
                            config.HostName = "LocalDemoHost";
                            config.EnableMetrics = true;
                        });
                        break;
                        
                    case "orleans":
                        services.AddOrleansAgentRuntime(
                            siloBuilder =>
                            {
                                siloBuilder.UseLocalhostClustering();
                                // Note: AddMemoryGrainStorage is available in Orleans.Storage.Memory package
                // For now, using basic configuration
                                siloBuilder.AddMemoryStreams("AevatarStreams");
                            },
                            config =>
                            {
                                config.HostName = "OrleansDemoHost";
                                config.Port = 11111;
                            });
                        break;
                        
                    case "protoactor":
                        services.AddProtoActorAgentRuntime(
                            actorConfig =>
                            {
                                actorConfig.WithDeadLetterThrottleCount(10);
                                actorConfig.WithDeadLetterThrottleInterval(TimeSpan.FromSeconds(1));
                            },
                            config =>
                            {
                                config.HostName = "ProtoActorDemoHost";
                            });
                        break;
                        
                    default:
                        throw new ArgumentException($"Unknown runtime type: {runtimeType}");
                }
            });
    }

    static async Task RunDemo(IAgentRuntime runtime, string runtimeType)
    {
        Console.WriteLine("=== Setting up Agent Host ===");
        
        // Create a host
        var hostConfig = new AgentHostConfiguration
        {
            HostName = $"{runtimeType}-demo-host",
            EnableHealthChecks = true,
            EnableMetrics = true
        };
        
        var host = await runtime.CreateHostAsync(hostConfig);
        Console.WriteLine($"Created host: {host.HostName} (Type: {host.RuntimeType})");
        
        // Start the host
        await host.StartAsync();
        Console.WriteLine("Host started successfully");
        Console.WriteLine();
        
        Console.WriteLine("=== Creating Agents ===");
        
        // Spawn a manager agent
        var managerOptions = new AgentSpawnOptions
        {
            AgentId = Guid.NewGuid().ToString(),
            EnableStreaming = true,
            EnablePersistence = false
        };
        
        var managerInstance = await runtime.SpawnAgentAsync<ManagerAgent>(managerOptions);
        Console.WriteLine($"Created Manager Agent: {managerInstance.AgentId}");
        
        // Spawn worker agents
        var workerInstances = new List<IAgentInstance>();
        for (int i = 0; i < 3; i++)
        {
            var workerOptions = new AgentSpawnOptions
            {
                AgentId = Guid.NewGuid().ToString(),
                ParentAgentId = managerInstance.AgentId.ToString(),
                AutoSubscribeToParent = true,
                EnableStreaming = true
            };
            
            var workerInstance = await runtime.SpawnAgentAsync<GreeterAgent>(workerOptions);
            workerInstances.Add(workerInstance);
            Console.WriteLine($"Created Worker Agent {i + 1}: {workerInstance.AgentId}");
        }
        
        Console.WriteLine();
        Console.WriteLine("=== Agent Hierarchy ===");
        Console.WriteLine($"Manager: {managerInstance.AgentTypeName} ({managerInstance.AgentId})");
        foreach (var worker in workerInstances)
        {
            var metadata = await worker.GetMetadataAsync();
            Console.WriteLine($"  └─ Worker: {worker.AgentTypeName} ({worker.AgentId}) - Parent: {metadata.ParentAgentId}");
        }
        
        Console.WriteLine();
        Console.WriteLine("=== Testing Agent Communication ===");
        
        // Send hello messages
        for (int i = 0; i < workerInstances.Count; i++)
        {
            var helloEvent = new HelloEvent
            {
                Sender = $"System",
                Message = $"Hello Worker {i + 1}!",
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            };
            
            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Payload = Google.Protobuf.WellKnownTypes.Any.Pack(helloEvent),
                PublisherId = "system",
                Direction = EventDirection.Down,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            
            await workerInstances[i].PublishEventAsync(envelope);
        }
        
        // Let messages propagate
        await Task.Delay(1000);
        
        Console.WriteLine();
        Console.WriteLine("=== Distributing Work ===");
        
        // Create work request through manager
        var workRequest = new WorkRequestEvent
        {
            TaskId = "batch-001",
            Description = "Process data batch",
            Priority = 1
        };
        
        var workEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Google.Protobuf.WellKnownTypes.Any.Pack(workRequest),
            PublisherId = "system",
            Direction = EventDirection.Down,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        
        // Send work to manager, which will distribute to workers
        await managerInstance.PublishEventAsync(workEnvelope);
        
        // Let work complete
        await Task.Delay(2000);
        
        Console.WriteLine();
        Console.WriteLine("=== Agent Metrics ===");
        
        // Get metrics from all agents
        var managerMetadata = await managerInstance.GetMetadataAsync();
        Console.WriteLine($"Manager Agent:");
        Console.WriteLine($"  - Events Processed: {managerMetadata.EventsProcessed}");
        Console.WriteLine($"  - Active: {managerMetadata.IsActive}");
        Console.WriteLine($"  - Children: {managerMetadata.ChildAgentIds.Count}");
        
        foreach (var worker in workerInstances)
        {
            var workerMetadata = await worker.GetMetadataAsync();
            Console.WriteLine($"Worker {worker.AgentId}:");
            Console.WriteLine($"  - Events Processed: {workerMetadata.EventsProcessed}");
            Console.WriteLine($"  - Active: {workerMetadata.IsActive}");
        }
        
        Console.WriteLine();
        Console.WriteLine("=== Runtime Health Check ===");
        
        var isHealthy = await runtime.IsHealthyAsync();
        Console.WriteLine($"Runtime Health: {(isHealthy ? "Healthy" : "Unhealthy")}");
        
        var hostHealth = await host.IsHealthyAsync();
        Console.WriteLine($"Host Health: {(hostHealth ? "Healthy" : "Unhealthy")}");
        
        Console.WriteLine();
        Console.WriteLine("=== Cleanup ===");
        
        // Deactivate agents
        foreach (var worker in workerInstances)
        {
            await worker.DeactivateAsync();
            Console.WriteLine($"Deactivated Worker: {worker.AgentId}");
        }
        
        await managerInstance.DeactivateAsync();
        Console.WriteLine($"Deactivated Manager: {managerInstance.AgentId}");
        
        // Stop host
        await host.StopAsync();
        Console.WriteLine("Host stopped");
        
        // Shutdown runtime
        await runtime.ShutdownAsync();
        Console.WriteLine("Runtime shutdown complete");
    }
}
