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
/// Demonstrates switching between different runtimes with the same agent code.
/// Shows how agents behave identically across Local, Orleans, and ProtoActor runtimes.
/// </summary>
public class RuntimeSwitchingDemo
{
    private readonly ILogger<RuntimeSwitchingDemo> _logger;

    public RuntimeSwitchingDemo(ILogger<RuntimeSwitchingDemo>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RuntimeSwitchingDemo>.Instance;
    }

    public async Task RunAllRuntimes()
    {
        Console.WriteLine("========================================");
        Console.WriteLine("Runtime Switching Demo");
        Console.WriteLine("========================================");
        Console.WriteLine("Running the same agent code on different runtimes...");
        Console.WriteLine();

        // Test on Local runtime
        await TestRuntime("Local", services =>
        {
            services.AddLocalAgentRuntime(config =>
            {
                config.HostName = "LocalTestHost";
            });
        });

        Console.WriteLine();
        Console.WriteLine("----------------------------------------");
        Console.WriteLine();

        // Test on ProtoActor runtime
        await TestRuntime("ProtoActor", services =>
        {
            services.AddProtoActorAgentRuntime(config =>
            {
                // Note: ProtoActor config does not have HostName property
                // config.HostName = "ProtoActorTestHost";
            });
        });

        // Note: Orleans requires more setup and is commented out for simplicity
        // Uncomment if Orleans is properly configured in your environment
        /*
        Console.WriteLine();
        Console.WriteLine("----------------------------------------");
        Console.WriteLine();

        // Test on Orleans runtime
        await TestRuntime("Orleans", services =>
        {
            services.AddOrleansAgentRuntime(
                siloBuilder =>
                {
                    siloBuilder.UseLocalhostClustering();
                    siloBuilder.AddMemoryGrainStorage("Default");
                },
                config =>
                {
                    config.HostName = "OrleansTestHost";
                });
        });
        */

        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("All runtime tests completed!");
    }

    private async Task TestRuntime(string runtimeName, Action<IServiceCollection> configureRuntime)
    {
        Console.WriteLine($"=== Testing {runtimeName} Runtime ===");

        // Create a host with the specified runtime
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Warning);
                });

                // Configure the specific runtime
                configureRuntime(services);
            })
            .Build();

        try
        {
            // Get the runtime
            var runtime = host.Services.GetRequiredService<IAgentRuntime>();
            _logger.LogInformation("Runtime type: {RuntimeType}", runtime.RuntimeType);

            // Create a host
            var hostConfig = new AgentHostConfiguration
            {
                HostName = $"{runtimeName}-benchmark-host"
            };

            var agentHost = await runtime.CreateHostAsync(hostConfig);
            await agentHost.StartAsync();

            // Measure agent creation time
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            // Create test agents
            var agents = new List<IAgentInstance>();
            for (int i = 0; i < 5; i++)
            {
                var options = new AgentSpawnOptions
                {
                    AgentId = Guid.NewGuid().ToString()
                };
                
                var agent = await runtime.SpawnAgentAsync<GreeterAgent>(options);
                agents.Add(agent);
            }
            
            sw.Stop();
            Console.WriteLine($"  Created 5 agents in {sw.ElapsedMilliseconds}ms");

            // Test event processing
            sw.Restart();
            var tasks = new List<Task>();
            
            foreach (var agent in agents)
            {
                var helloEvent = new HelloEvent
                {
                    Sender = "Benchmark",
                    Message = "Test message",
                    Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
                };
                
                var envelope = new EventEnvelope
                {
                    Id = Guid.NewGuid().ToString(),
                    Payload = Google.Protobuf.WellKnownTypes.Any.Pack(helloEvent),
                    PublisherId = "benchmark",
                    Direction = EventDirection.Down,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                
                tasks.Add(agent.PublishEventAsync(envelope));
            }
            
            await Task.WhenAll(tasks);
            sw.Stop();
            Console.WriteLine($"  Processed 5 events in {sw.ElapsedMilliseconds}ms");

            // Test hierarchical setup
            sw.Restart();
            
            var managerOptions = new AgentSpawnOptions { AgentId = Guid.NewGuid().ToString() };
            var manager = await runtime.SpawnAgentAsync<ManagerAgent>(managerOptions);
            
            var workerOptions = new AgentSpawnOptions
            {
                AgentId = Guid.NewGuid().ToString(),
                ParentAgentId = manager.AgentId.ToString(),
                AutoSubscribeToParent = true
            };
            var worker = await runtime.SpawnAgentAsync<GreeterAgent>(workerOptions);
            
            sw.Stop();
            Console.WriteLine($"  Created parent-child hierarchy in {sw.ElapsedMilliseconds}ms");

            // Check health
            var isHealthy = await runtime.IsHealthyAsync();
            Console.WriteLine($"  Runtime health: {(isHealthy ? "✓ Healthy" : "✗ Unhealthy")}");

            // Cleanup
            foreach (var agent in agents)
            {
                await agent.DeactivateAsync();
            }
            await worker.DeactivateAsync();
            await manager.DeactivateAsync();
            await agentHost.StopAsync();
            await runtime.ShutdownAsync();

            Console.WriteLine($"  {runtimeName} test completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error in {runtimeName}: {ex.Message}");
            _logger.LogError(ex, "Error testing {RuntimeName}", runtimeName);
        }
        finally
        {
            if (host is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (host is IDisposable disposable)
                disposable.Dispose();
        }
    }

    // Entry point - to run this demo, call from Program.cs or use command line args
    public static async Task RunMain(string[] args)
    {
        var demo = new RuntimeSwitchingDemo();
        await demo.RunAllRuntimes();
        
        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}
