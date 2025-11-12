using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime;
using Aevatar.Agents.Runtime.Local.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RuntimeAbstractionDemo.Agents;

namespace RuntimeAbstractionDemo;

/// <summary>
/// Demonstrates host management capabilities across different runtimes.
/// Shows how to create, manage, and monitor multiple hosts within a runtime.
/// </summary>
public class HostManagementDemo
{
    private readonly ILogger<HostManagementDemo> _logger;

    public HostManagementDemo(ILogger<HostManagementDemo>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HostManagementDemo>.Instance;
    }

    public async Task RunDemo()
    {
        Console.WriteLine("========================================");
        Console.WriteLine("Host Management Demo");
        Console.WriteLine("========================================");
        Console.WriteLine();

        // Create service host
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });

                // Use Local runtime for this demo
                services.AddLocalAgentRuntime();
            })
            .Build();

        var runtime = host.Services.GetRequiredService<IAgentRuntime>();

        try
        {
            await DemonstrateMultipleHosts(runtime);
            await DemonstrateHostHealthMonitoring(runtime);
            await DemonstrateAgentDistribution(runtime);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            _logger.LogError(ex, "Error in host management demo");
        }
        finally
        {
            await runtime.ShutdownAsync();
        }

        Console.WriteLine();
        Console.WriteLine("Host management demo completed!");
    }

    private async Task DemonstrateMultipleHosts(IAgentRuntime runtime)
    {
        Console.WriteLine("=== Creating Multiple Hosts ===");

        var hosts = new List<IAgentHost>();

        // Create multiple hosts with different configurations
        for (int i = 1; i <= 3; i++)
        {
            var config = new AgentHostConfiguration
            {
                HostName = $"Host-{i}",
                Port = 10000 + i,  // Different ports for network-based runtimes
                MaxAgents = 10,
                EnableHealthChecks = true,
                EnableMetrics = true,
                RuntimeSpecificSettings = new Dictionary<string, object>
                {
                    ["zone"] = $"zone-{(char)('a' + i - 1)}",
                    ["region"] = "us-east"
                }
            };

            var agentHost = await runtime.CreateHostAsync(config);
            await agentHost.StartAsync();
            hosts.Add(agentHost);

            Console.WriteLine($"Created host: {agentHost.HostName}");
            Console.WriteLine($"  - Host ID: {agentHost.HostId}");
            Console.WriteLine($"  - Runtime Type: {agentHost.RuntimeType}");
            Console.WriteLine($"  - Port: {agentHost.Port?.ToString() ?? "N/A"}");
        }

        Console.WriteLine($"Total hosts created: {hosts.Count}");
        Console.WriteLine();

        // Cleanup
        foreach (var h in hosts)
        {
            await h.StopAsync();
        }
    }

    private async Task DemonstrateHostHealthMonitoring(IAgentRuntime runtime)
    {
        Console.WriteLine("=== Host Health Monitoring ===");

        // Create hosts
        var healthyHost = await runtime.CreateHostAsync(new AgentHostConfiguration
        {
            HostName = "HealthyHost",
            EnableHealthChecks = true
        });
        await healthyHost.StartAsync();

        var unhealthyHost = await runtime.CreateHostAsync(new AgentHostConfiguration
        {
            HostName = "UnhealthyHost",
            EnableHealthChecks = true
        });
        // Intentionally not starting this host to simulate unhealthy state

        // Check health
        var healthyStatus = await healthyHost.IsHealthyAsync();
        var unhealthyStatus = await unhealthyHost.IsHealthyAsync();

        Console.WriteLine($"HealthyHost status: {(healthyStatus ? "✓ Healthy" : "✗ Unhealthy")}");
        Console.WriteLine($"UnhealthyHost status: {(unhealthyStatus ? "✓ Healthy" : "✗ Unhealthy")}");

        // Check runtime overall health
        var runtimeHealth = await runtime.IsHealthyAsync();
        Console.WriteLine($"Runtime overall health: {(runtimeHealth ? "✓ Healthy" : "✗ Unhealthy")}");
        Console.WriteLine();

        // Cleanup
        await healthyHost.StopAsync();
    }

    private async Task DemonstrateAgentDistribution(IAgentRuntime runtime)
    {
        Console.WriteLine("=== Agent Distribution Across Hosts ===");

        // Create hosts with different capacities
        var hosts = new List<IAgentHost>();

        var primaryHost = await runtime.CreateHostAsync(new AgentHostConfiguration
        {
            HostName = "PrimaryHost",
            MaxAgents = 5
        });
        await primaryHost.StartAsync();
        hosts.Add(primaryHost);

        var secondaryHost = await runtime.CreateHostAsync(new AgentHostConfiguration
        {
            HostName = "SecondaryHost",
            MaxAgents = 3
        });
        await secondaryHost.StartAsync();
        hosts.Add(secondaryHost);

        Console.WriteLine("Created 2 hosts with different capacities");

        // Distribute agents across hosts
        var agentDistribution = new Dictionary<IAgentHost, List<IAgentInstance>>();
        foreach (var h in hosts)
        {
            agentDistribution[h] = new List<IAgentInstance>();
        }

        // Create agents and register them with different hosts
        for (int i = 0; i < 6; i++)
        {
            var targetHost = i < 3 ? primaryHost : secondaryHost;
            
            var agent = await CreateAndRegisterAgent(runtime, targetHost, $"Agent-{i + 1}");
            agentDistribution[targetHost].Add(agent);
        }

        // Display distribution
        foreach (var kvp in agentDistribution)
        {
            var h = kvp.Key;
            var agents = kvp.Value;
            
            Console.WriteLine($"\nHost: {h.HostName} ({h.HostId})");
            Console.WriteLine($"  Agents registered: {agents.Count}");
            
            var agentIds = await h.GetAgentIdsAsync();
            foreach (var id in agentIds)
            {
                var agent = await h.GetAgentAsync(id);
                if (agent != null)
                {
                    Console.WriteLine($"    - {agent.AgentTypeName} ({id})");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== Testing Cross-Host Communication ===");

        // Create manager on primary host
        var managerOptions = new AgentSpawnOptions { AgentId = Guid.NewGuid().ToString() };
        var manager = await runtime.SpawnAgentAsync<ManagerAgent>(managerOptions);
        await primaryHost.RegisterAgentAsync(manager.AgentId.ToString(), manager);

        // Create workers on secondary host
        for (int i = 0; i < 2; i++)
        {
            var workerOptions = new AgentSpawnOptions
            {
                AgentId = Guid.NewGuid().ToString(),
                ParentAgentId = manager.AgentId.ToString(),
                AutoSubscribeToParent = true
            };
            
            var worker = await runtime.SpawnAgentAsync<GreeterAgent>(workerOptions);
            await secondaryHost.RegisterAgentAsync(worker.AgentId.ToString(), worker);
        }

        Console.WriteLine("Manager on PrimaryHost can communicate with Workers on SecondaryHost");

        // Send test event
        var testEvent = new WorkRequestEvent
        {
            TaskId = "cross-host-task",
            Description = "Test cross-host communication",
            Priority = 1
        };

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Google.Protobuf.WellKnownTypes.Any.Pack(testEvent),
            PublisherId = "host-manager",
            Direction = EventDirection.Down,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await manager.PublishEventAsync(envelope);
        await Task.Delay(1000); // Let event propagate

        Console.WriteLine("Cross-host event propagation successful!");
        Console.WriteLine();

        // Cleanup
        foreach (var h in hosts)
        {
            var agentIds = await h.GetAgentIdsAsync();
            foreach (var id in agentIds)
            {
                var agent = await h.GetAgentAsync(id);
                if (agent != null)
                {
                    await agent.DeactivateAsync();
                }
            }
            await h.StopAsync();
        }
    }

    private async Task<IAgentInstance> CreateAndRegisterAgent(
        IAgentRuntime runtime, 
        IAgentHost host, 
        string agentName)
    {
        var options = new AgentSpawnOptions
        {
            AgentId = Guid.NewGuid().ToString(),
            Tags = new Dictionary<string, string>
            {
                ["name"] = agentName,
                ["host"] = host.HostName
            }
        };

        var agent = await runtime.SpawnAgentAsync<GreeterAgent>(options);
        await host.RegisterAgentAsync(agent.AgentId.ToString(), agent);
        
        return agent;
    }

    // Entry point - to run this demo, call from Program.cs or use command line args
    public static async Task RunMain(string[] args)
    {
        var demo = new HostManagementDemo();
        await demo.RunDemo();
        
        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}
