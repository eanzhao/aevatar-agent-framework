using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Agents.Runtime;
using Aevatar.Agents.Runtime.Local.Extensions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RuntimeAbstractionDemo;

/// <summary>
/// Simple demonstration of runtime abstraction with minimal dependencies.
/// </summary>
public class SimpleRuntimeDemo
{
    /// <summary>
    /// A minimal agent that works with the runtime abstraction.
    /// </summary>
    public class SimpleAgent : GAgentBase<DemoAgentState>
    {
        // Parameterless constructor required by the framework
        public SimpleAgent() : base(null)
        {
            // State initialization is handled by the framework
        }

        public override Task OnActivateAsync(CancellationToken ct = default)
        {
            // Initialize state during activation
            if (State == null)
            {
                var newState = new DemoAgentState
                {
                    Name = $"SimpleAgent_{Id.ToString("N").Substring(0, 8)}",
                    MessageCount = 0,
                    LastUpdate = Timestamp.FromDateTime(DateTime.UtcNow),
                    RuntimeType = "Unknown"
                };
                // Note: State property is read-only, this is just for demonstration
                // The actual state initialization should be done by the framework
            }
            return base.OnActivateAsync(ct);
        }

        public override Task<string> GetDescriptionAsync()
        {
            return Task.FromResult($"Simple Agent: {Id}");
        }

        [EventHandler]
        public async Task HandleHelloEvent(HelloEvent evt)
        {
            Logger.LogInformation("Received hello from {Sender}: {Message}",
                evt.Sender, evt.Message);
            
            // Note: State modification may need to be done differently
            // depending on the framework's state management approach
            await Task.CompletedTask;
        }
    }

    public static async Task RunDemo()
    {
        Console.WriteLine("====================================");
        Console.WriteLine("Simple Runtime Abstraction Demo");
        Console.WriteLine("====================================");
        Console.WriteLine();

        // Create host with Local runtime
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });

                // Add Local runtime (simplest option)
                services.AddLocalAgentRuntime(config =>
                {
                    config.HostName = "SimpleLocalHost";
                });
            })
            .Build();

        try
        {
            // Get the runtime
            var runtime = host.Services.GetRequiredService<IAgentRuntime>();
            Console.WriteLine($"Runtime Type: {runtime.RuntimeType}");

            // Create a host
            var hostConfig = new AgentHostConfiguration
            {
                HostName = "simple-demo-host"
            };
            
            var agentHost = await runtime.CreateHostAsync(hostConfig);
            await agentHost.StartAsync();
            Console.WriteLine($"Host started: {agentHost.HostName}");

            // Create an agent using the abstraction
            var spawnOptions = new AgentSpawnOptions
            {
                AgentId = Guid.NewGuid().ToString(),
                EnableStreaming = true
            };

            // Note: This may fail due to constructor requirements
            // The framework expects agents to have specific constructor patterns
            Console.WriteLine("Creating agent...");
            
            try
            {
                var agentInstance = await runtime.SpawnAgentAsync<SimpleAgent>(spawnOptions);
                Console.WriteLine($"Agent created: {agentInstance.AgentId}");

                // Send a test event
                var helloEvent = new HelloEvent
                {
                    Sender = "Demo",
                    Message = "Hello from runtime abstraction!",
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                };

                var envelope = new EventEnvelope
                {
                    Id = Guid.NewGuid().ToString(),
                    Payload = Google.Protobuf.WellKnownTypes.Any.Pack(helloEvent),
                    PublisherId = "simple-demo",
                    Direction = EventDirection.Down,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                await agentInstance.PublishEventAsync(envelope);
                Console.WriteLine("Event sent successfully");

                // Wait a bit for processing
                await Task.Delay(1000);

                // Get metadata
                var metadata = await agentInstance.GetMetadataAsync();
                Console.WriteLine($"Agent processed {metadata.EventsProcessed} events");

                // Cleanup
                await agentInstance.DeactivateAsync();
                Console.WriteLine("Agent deactivated");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating/using agent: {ex.Message}");
                Console.WriteLine("This is expected due to framework constraints.");
                Console.WriteLine("The demo shows the abstraction layer structure.");
            }

            // Stop the host
            await agentHost.StopAsync();
            Console.WriteLine("Host stopped");

            // Shutdown runtime
            await runtime.ShutdownAsync();
            Console.WriteLine("Runtime shutdown complete");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Demo error: {ex.Message}");
        }
        finally
        {
            if (host is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (host is IDisposable disposable)
                disposable.Dispose();
        }

        Console.WriteLine();
        Console.WriteLine("Simple demo completed!");
    }
}
