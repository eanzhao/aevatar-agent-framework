using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime;
using Aevatar.Agents.Runtime.Local.Extensions;
using Aevatar.Agents.Runtime.Orleans.Extensions;
using Aevatar.Agents.Runtime.ProtoActor.Extensions;
using ChatRoomDemo.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChatRoomDemo;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("====================================");
        Console.WriteLine("     Chat Room Demo");
        Console.WriteLine("====================================");
        Console.WriteLine();

        // Select runtime from command line or default to local
        var runtimeType = args.Length > 0 ? args[0].ToLower() : "local";
        Console.WriteLine($"Selected Runtime: {runtimeType}");
        Console.WriteLine();

        // Create host with selected runtime
        var host = CreateHostBuilder(runtimeType).Build();
        await host.StartAsync();

        var runtime = host.Services.GetRequiredService<IAgentRuntime>();

        try
        {
            await RunChatRoomDemo(runtime, runtimeType);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
        }
        finally
        {
            await host.StopAsync();
        }

        Console.WriteLine();
        Console.WriteLine("Chat room demo completed.");
    }

    static IHostBuilder CreateHostBuilder(string runtimeType)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddFilter("Aevatar.Agents", LogLevel.Debug);
            })
            .ConfigureServices((context, services) =>
            {
                // Configure runtime based on selection
                switch (runtimeType)
                {
                    case "orleans":
                        services.AddOrleansAgentRuntime(
                            siloBuilder =>
                            {
                                siloBuilder.UseLocalhostClustering();
                                siloBuilder.AddMemoryStreams("AevatarStreams");
                            },
                            config =>
                            {
                                config.HostName = "orleans-chat-host";
                                config.MaxConcurrency = 100;
                            });
                        break;
                        
                    case "protoactor":
                        services.AddProtoActorAgentRuntime(
                            config =>
                            {
                                // ProtoActor configuration
                            },
                            configureHostConfig: config =>
                            {
                                config.HostName = "protoactor-chat-host";
                                config.MaxConcurrency = 100;
                            });
                        break;
                        
                    case "local":
                    default:
                        services.AddLocalAgentRuntime(config =>
                        {
                            config.HostName = "local-chat-host";
                            config.MaxConcurrency = 10;
                        });
                        break;
                }
            });
    }

    static async Task RunChatRoomDemo(IAgentRuntime runtime, string runtimeType)
    {
        Console.WriteLine("=== Setting up Chat Room ===");
        
        // Create or get default host
        var host = await runtime.GetOrCreateHostAsync("chat-host", new AgentHostConfiguration
        {
            HostName = $"{runtimeType}-chat-host",
            MaxConcurrency = 20
        });
        
        await host.StartAsync();
        Console.WriteLine($"Chat host started: {host.HostId}");
        Console.WriteLine();
        
        // Create chat room
        Console.WriteLine("=== Creating Chat Room ===");
        var roomOptions = new AgentSpawnOptions
        {
            AgentId = "main-room",
            HostId = host.HostId.ToString()
        };
        
        var roomInstance = await runtime.SpawnAgentAsync<ChatRoomAgent>(roomOptions);
        Console.WriteLine($"Created chat room: {roomInstance.AgentId}");
        
        // Create users
        Console.WriteLine();
        Console.WriteLine("=== Creating Users ===");
        
        var userInstances = new List<IAgentInstance>();
        var userNames = new[] { "Alice", "Bob", "Charlie", "Diana", "Eve" };
        
        for (int i = 0; i < userNames.Length; i++)
        {
            var userOptions = new AgentSpawnOptions
            {
                AgentId = $"user-{userNames[i].ToLower()}",
                HostId = host.HostId.ToString(),
                ParentAgentId = roomInstance.AgentId.ToString(),
                AutoSubscribeToParent = true
            };
            
            var userInstance = await runtime.SpawnAgentAsync<ChatUserAgent>(userOptions);
            userInstances.Add(userInstance);
            
            // Set custom user name (in a real system, this would be done through initialization)
            var userAgent = await runtime.GetAgentAsync<ChatUserAgent>(userInstance.AgentId.ToString());
            if (userAgent != null)
            {
                userAgent.State.UserName = userNames[i];
                userAgent.State.IsModerator = (i == 0); // Make Alice a moderator
            }
            
            Console.WriteLine($"Created user: {userNames[i]} ({userInstance.AgentId})");
        }
        
        // Users join the room
        Console.WriteLine();
        Console.WriteLine("=== Users Joining Room ===");
        
        foreach (var (user, name) in userInstances.Zip(userNames))
        {
            var userAgent = await runtime.GetAgentAsync<ChatUserAgent>(user.AgentId.ToString());
            if (userAgent != null)
            {
                await userAgent.JoinRoomAsync("main-room");
            }
            await Task.Delay(500); // Stagger joins
        }
        
        // Simulate chat activity
        Console.WriteLine();
        Console.WriteLine("=== Chat Activity ===");
        
        // Alice sends a welcome message
        var aliceAgent = await runtime.GetAgentAsync<ChatUserAgent>(userInstances[0].AgentId.ToString());
        if (aliceAgent != null)
        {
            await aliceAgent.SendMessageAsync("Welcome everyone to the chat room!");
            await Task.Delay(1000);
        }
        
        // Bob responds
        var bobAgent = await runtime.GetAgentAsync<ChatUserAgent>(userInstances[1].AgentId.ToString());
        if (bobAgent != null)
        {
            await bobAgent.SetTypingAsync(true);
            await Task.Delay(500);
            await bobAgent.SendMessageAsync("Thanks Alice! Happy to be here.");
            await bobAgent.SetTypingAsync(false);
            await Task.Delay(1000);
        }
        
        // Charlie sends a private message to Diana
        var charlieAgent = await runtime.GetAgentAsync<ChatUserAgent>(userInstances[2].AgentId.ToString());
        if (charlieAgent != null)
        {
            await charlieAgent.SendMessageAsync("Hey Diana, this is a private message!", true, userInstances[3].AgentId.ToString());
            await Task.Delay(1000);
        }
        
        // Diana responds publicly
        var dianaAgent = await runtime.GetAgentAsync<ChatUserAgent>(userInstances[3].AgentId.ToString());
        if (dianaAgent != null)
        {
            await dianaAgent.SendMessageAsync("This chat room is really cool!");
            await Task.Delay(1000);
        }
        
        // Eve tries to spam (will trigger auto-moderation)
        var eveAgent = await runtime.GetAgentAsync<ChatUserAgent>(userInstances[4].AgentId.ToString());
        if (eveAgent != null)
        {
            await eveAgent.SendMessageAsync("Buy cheap spam spam spam!");
            await Task.Delay(1000);
        }
        
        // Room changes topic
        var room = await runtime.GetAgentAsync<ChatRoomAgent>(roomInstance.AgentId.ToString());
        if (room != null)
        {
            await room.SetTopicAsync("Discussion about Agent Framework", "Alice");
            await Task.Delay(1000);
        }
        
        // Alice (moderator) bans Eve for spamming
        if (aliceAgent != null)
        {
            await aliceAgent.BanUserAsync(userInstances[4].AgentId.ToString(), "Eve", "Spamming");
            await Task.Delay(1000);
        }
        
        // Bob leaves the room
        if (bobAgent != null)
        {
            await bobAgent.LeaveRoomAsync();
            await Task.Delay(1000);
        }
        
        // Get final room state
        Console.WriteLine();
        Console.WriteLine("=== Final Room State ===");
        
        if (room != null)
        {
            var roomState = await room.GetRoomStateAsync();
            Console.WriteLine($"Room: {roomState.RoomName}");
            Console.WriteLine($"Topic: {roomState.Topic}");
            Console.WriteLine($"Active Participants: {roomState.ParticipantCount}");
            Console.WriteLine($"Total Messages: {roomState.TotalMessages}");
            Console.WriteLine($"Banned Users: {roomState.BannedUsers.Count}");
        }
        
        // Get agent metrics
        Console.WriteLine();
        Console.WriteLine("=== Agent Metrics ===");
        
        var roomMetadata = await roomInstance.GetMetadataAsync();
        Console.WriteLine($"Room Agent:");
        Console.WriteLine($"  - Events Processed: {roomMetadata.EventsProcessed}");
        Console.WriteLine($"  - Children (Users): {roomMetadata.ChildAgentIds.Count}");
        
        foreach (var (user, name) in userInstances.Take(3).Zip(userNames.Take(3)))
        {
            var metadata = await user.GetMetadataAsync();
            Console.WriteLine($"{name}:");
            Console.WriteLine($"  - Events Processed: {metadata.EventsProcessed}");
            Console.WriteLine($"  - Active: {metadata.IsActive}");
        }
        
        // Cleanup
        Console.WriteLine();
        Console.WriteLine("=== Cleanup ===");
        
        foreach (var user in userInstances)
        {
            await runtime.DespawnAgentAsync(user.AgentId.ToString());
        }
        
        await runtime.DespawnAgentAsync(roomInstance.AgentId.ToString());
        await host.StopAsync();
        
        Console.WriteLine("Chat room closed and all users disconnected.");
    }
}
