using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.MEAI;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Runtime.Local;
using AIEventSourcingDemo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

// Build host with DI
var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.secrets.json", optional: true);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;
        services.Configure<LLMProvidersConfig>(config.GetSection("LLMProviders"));

        // Configure logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Register MEAI LLM Provider Factory
        services.AddSingleton<ILLMProviderFactory, MEAILLMProviderFactory>();

        // Configure MongoDB for Event Sourcing
        services.AddSingleton<IMongoClient>(sp =>
        {
            var connectionString = context.Configuration.GetConnectionString("MongoDB") 
                ?? "mongodb://localhost:27017";
            return new MongoClient(connectionString);
        });

        // For demo purposes, we'll use InMemoryEventStore
        // In production, implement MongoDB-based IEventStore
        services.AddSingleton<IEventStore, InMemoryEventStore>();

        services.AddSingleton<IGAgentFactory, AIGAgentFactory>();
        services.AddSingleton<IGAgentActorFactory, LocalGAgentActorFactory>();
    })
    .Build();

// Run the demo
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var eventStore = host.Services.GetRequiredService<IEventStore>();
var llmProviderFactory = host.Services.GetRequiredService<ILLMProviderFactory>();
var actorFactory = host.Services.GetRequiredService<IGAgentActorFactory>();

logger.LogInformation("=== AI Agent with Event Sourcing Demo ===");
logger.LogInformation("");

try
{
    // Create AI Assistant Agent
    var assistantId = Guid.NewGuid();
    var assistantActor = await actorFactory.CreateGAgentActorAsync<AIAssistantAgent>(assistantId);
    var assistant = (AIAssistantAgent)assistantActor.GetAgent();

    // Initialize assistant through event (not direct state modification)
    await assistant.InitializeAssistantAsync("HyperAssistant");

    // Initialize AI with OpenAI provider
    await assistant.InitializeAsync(
        "deepseek", // Use the provider configured in appsettings.json
        config =>
        {
            config.Model = "deepseek-chat";
            config.Temperature = 0.8;
            config.MaxTokens = 1500;
        });

    logger.LogInformation("AI Assistant initialized: {Name} ({Id})", 
        assistant.GetCurrentState().Name, assistantId);

    // === Conversation 1: General Chat ===
    logger.LogInformation("\n--- Conversation 1: General Chat ---");
    
    var response1 = await assistant.HandleUserMessageAsync(
        "user123",
        "Hello! Can you tell me about event sourcing?");
    logger.LogInformation("User: Hello! Can you tell me about event sourcing?");
    logger.LogInformation("Assistant: {Response}", response1);

    var response2 = await assistant.HandleUserMessageAsync(
        "user123",
        "How does it relate to AI agents?");
    logger.LogInformation("\nUser: How does it relate to AI agents?");
    logger.LogInformation("Assistant: {Response}", response2);

    // Provide feedback
    await assistant.ProvideFeedbackAsync(4.5, "Very helpful explanation!");
    logger.LogInformation("User provided feedback: 4.5/5.0 - Very helpful!");

    // Complete conversation
    await assistant.CompleteConversationAsync("Event Sourcing", 4.5);
    logger.LogInformation("Conversation completed.\n");

    // === Conversation 2: Code Assistance ===
    logger.LogInformation("--- Conversation 2: Code Assistance ---");

    var response3 = await assistant.HandleUserMessageAsync(
        "user456",
        "Can you write a simple C# class that implements a counter with events?");
    logger.LogInformation("User: Can you write a simple C# class that implements a counter with events?");
    logger.LogInformation("Assistant: {Response}", response3);

    await assistant.ProvideFeedbackAsync(5.0, "Perfect code example!");
    await assistant.CompleteConversationAsync("C# Code Generation", 5.0);
    logger.LogInformation("Conversation completed.\n");

    // === Display Agent State ===
    logger.LogInformation("--- Agent State Summary ---");
    var state = assistant.GetCurrentState();
    logger.LogInformation("Total Interactions: {Count}", state.TotalInteractions);
    logger.LogInformation("Average Satisfaction: {Score:F2}/5.0", state.AverageSatisfaction);
    logger.LogInformation("Conversation History: {Count} conversations", state.ConversationHistory.Count);

    // === Demonstrate Event Replay ===
    logger.LogInformation("\n--- Demonstrating Event Replay ---");
    
    // Create a new agent instance with the same ID
    var replayedAssistantActor = await actorFactory.CreateGAgentActorAsync<AIAssistantAgent>(assistantId);
    var replayedAssistant = (AIAssistantAgent)replayedAssistantActor.GetAgent();
    
    // Replay events from the event store (this will reconstruct state from events)
    await replayedAssistant.ReplayEventsAsync();
    
    // Initialize AI after replay (AI config is not part of event sourcing)
    await replayedAssistant.InitializeAsync(
        "deepseek",
        config =>
        {
            config.Model = "deepseek-chat";
            config.Temperature = 0.8;
            config.MaxTokens = 1500;
        });

    logger.LogInformation("Replayed agent state:");
    var replayedState = replayedAssistant.GetCurrentState();
    logger.LogInformation("Total Interactions: {Count}", replayedState.TotalInteractions);
    logger.LogInformation("Average Satisfaction: {Score:F2}/5.0", replayedState.AverageSatisfaction);
    logger.LogInformation("Conversation History: {Count} conversations", replayedState.ConversationHistory.Count);

    // Verify state matches
    var originalState = assistant.GetCurrentState();
    if (replayedState.TotalInteractions == originalState.TotalInteractions &&
        Math.Abs(replayedState.AverageSatisfaction - originalState.AverageSatisfaction) < 0.01)
    {
        logger.LogInformation("✅ Event replay successful - state matches original!");
    }

    // === Query Event History ===
    logger.LogInformation("\n--- Event History ---");
    
    var events = await eventStore.GetEventsAsync(assistantId);
    var eventTypes = events
        .GroupBy(e => e.EventType)
        .Select(g => new { Type = g.Key.Split('.').Last(), Count = g.Count() });

    foreach (var eventType in eventTypes)
    {
        logger.LogInformation("  {Type}: {Count} events", eventType.Type, eventType.Count);
    }

    logger.LogInformation("\n--- Performance Metrics ---");
    logger.LogInformation("Current Version: {Version}", assistant.GetCurrentVersion());
    logger.LogInformation("Cached Event Types: {Count}", AIGAgentBaseWithEventSourcing<AIAssistantState, AIAssistantConfig>.CachedTypeCount);
    logger.LogInformation("Pending Events: {Count}", assistant.GetPendingEventCount());

    // Create a manual snapshot
    await assistant.CreateSnapshotAsync();
    logger.LogInformation("Manual snapshot created.");
}
catch (Exception ex)
{
    logger.LogError(ex, "Error running AI Event Sourcing demo");
}

logger.LogInformation("\n=== Demo Complete ===");