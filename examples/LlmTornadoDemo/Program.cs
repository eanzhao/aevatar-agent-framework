using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.LLMTornado;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Runtime.Local;
using LlmTornadoDemo;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

// ============================================================================
// Build Host with Dependency Injection
// ============================================================================
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
        
        // Configure logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Configure LLM Providers
        services.Configure<LLMProvidersConfig>(config.GetSection("LLMProviders"));
        
        // Register LlmTornado Factory
        services.AddAevatarLLMTornado();

        // Configure Event Store (InMemory for demo)
        services.AddSingleton<IEventStore, InMemoryEventStore>();

        // Register Agent Factories
        services.AddAevatarLocalRuntime();
    })
    .Build();

// ============================================================================
// Main Demo Execution
// ============================================================================
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var eventStore = host.Services.GetRequiredService<IEventStore>();
var actorFactory = host.Services.GetRequiredService<IGAgentActorFactory>();

logger.LogInformation("╔════════════════════════════════════════════╗");
logger.LogInformation("║   AI Agent with LlmTornado Demo           ║");
logger.LogInformation("║   Pure Event-Driven Architecture          ║");
logger.LogInformation("╚════════════════════════════════════════════╝\n");

try
{
    // ========================================================================
    // 1. Create AI Assistant Actor
    // ========================================================================
    logger.LogInformation("▶ Creating AI Assistant Agent...");
    var assistantId = Guid.NewGuid();
    var assistantActor = await actorFactory.CreateGAgentActorAsync<AIAssistantAgent>(assistantId);
    var assistant = (AIAssistantAgent)assistantActor.GetAgent();

    // ========================================================================
    // 2. Note: Event Handlers Process Everything Internally
    // ========================================================================
    logger.LogInformation("▶ Event handlers will process all events internally...");
    logger.LogInformation("  - HandleInitialization: Initialize AI capabilities");
    logger.LogInformation("  - HandleUserMessage: Generate AI responses");
    logger.LogInformation("  - HandleAssistantResponse: Analyze quality");
    logger.LogInformation("  - HandleFeedback: Adjust parameters");
    logger.LogInformation("  - HandleConversationComplete: Analytics & maintenance");

    // ========================================================================
    // 3. Initialize Assistant via Event
    // ========================================================================
    logger.LogInformation("▶ Initializing assistant via event...");
    await assistantActor.PublishEventAsync(new AssistantInitializedEvent
    {
        AssistantId = assistantId.ToString(),
        Name = "HyperAssistant",
        CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
    });
    
    // Wait for initialization to complete
    Console.WriteLine("[DEBUG] Waiting for initialization to complete...");
    await Task.Delay(3000); // Give more time for initialization
    Console.WriteLine("[DEBUG] Initialization wait complete");
    logger.LogInformation("✅ Assistant '{Name}' initialized (ID: {Id})", 
        assistant.GetCustomState().Name, assistantId);

    // ========================================================================
    // 4. Conversation 1: General Chat about Event Sourcing
    // ========================================================================
    logger.LogInformation("\n╔════ Conversation 1: Event Sourcing ════╗");
    
    var conversationId = Guid.NewGuid().ToString();
    
    // First message
    await SendUserMessage(assistantActor, assistant,
        "user123", conversationId,
        "Hello! Can you tell me about event sourcing?",
        logger);
    
    // Follow-up question  
    await SendUserMessage(assistantActor, assistant,
        "user123", conversationId,
        "How does it relate to AI agents?",
        logger);
    
    // Provide feedback
    await assistantActor.PublishEventAsync(new FeedbackReceived
    {
        ConversationId = conversationId,
        SatisfactionScore = 4.5,
        FeedbackText = "Very helpful explanation!",
        Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
    });
    logger.LogInformation("⭐ Feedback: 4.5/5.0 - Very helpful!");
    
    // Complete conversation
    await assistantActor.PublishEventAsync(new ConversationCompleted
    {
        ConversationId = conversationId,
        TotalMessages = 2,
        TotalTokens = assistant.GetCustomState().TotalInteractions * 50, // Estimate
        FinalSatisfaction = 4.5,
        Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
    });
    logger.LogInformation("✅ Conversation 1 completed\n");

    // ========================================================================
    // 5. Conversation 2: Code Assistance
    // ========================================================================
    logger.LogInformation("╔════ Conversation 2: Code Generation ════╗");
    
    var conversationId2 = Guid.NewGuid().ToString();
    
    // Ask for code example
    await SendUserMessage(assistantActor, assistant,
        "user456", conversationId2,
        "Can you write a simple C# class that implements a counter with events?",
        logger);
    
    // Provide perfect feedback
    await assistantActor.PublishEventAsync(new FeedbackReceived
    {
        ConversationId = conversationId2,
        SatisfactionScore = 5.0,
        FeedbackText = "Perfect code example!",
        Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
    });
    logger.LogInformation("⭐ Feedback: 5.0/5.0 - Perfect!");
    
    // Complete conversation
    await assistantActor.PublishEventAsync(new ConversationCompleted
    {
        ConversationId = conversationId2,
        TotalMessages = 1,
        TotalTokens = assistant.GetCustomState().TotalInteractions * 75, // Estimate
        FinalSatisfaction = 5.0,
        Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
    });
    logger.LogInformation("✅ Conversation 2 completed\n");

    // ========================================================================
    // 6. Display Current State
    // ========================================================================
    logger.LogInformation("╔════ Agent State Summary ════╗");
    var state = assistant.GetCustomState();
    logger.LogInformation("📊 Total Interactions: {Count}", state.TotalInteractions);
    logger.LogInformation("⭐ Average Satisfaction: {Score:F2}/5.0", state.AverageSatisfaction);
    logger.LogInformation("💬 Conversation History: {Count} conversations", state.ConversationHistory.Count);
    
    foreach (var conv in state.ConversationHistory)
    {
        logger.LogInformation("  └─ {Id}: {Topic} (Score: {Score:F1})", 
            conv.ConversationId.Substring(0, 8), 
            conv.Topic ?? "General", 
            conv.SatisfactionScore);
    }

    // ========================================================================
    // 7. Demonstrate Event Replay
    // ========================================================================
    logger.LogInformation("\n╔════ Event Replay Demo ════╗");
    logger.LogInformation("🔄 Creating new agent instance with same ID...");
    
    var replayedAssistantActor = await actorFactory.CreateGAgentActorAsync<AIAssistantAgent>(assistantId);
    var replayedAssistant = (AIAssistantAgent)replayedAssistantActor.GetAgent();
    
    logger.LogInformation("📼 Replaying events from event store...");
    await replayedAssistant.ReplayEventsAsync();
    await Task.Delay(500); // Allow event handlers to process
    
    var replayedState = replayedAssistant.GetCustomState();
    logger.LogInformation("✅ Replay complete!");
    logger.LogInformation("📊 Replayed Interactions: {Count}", replayedState.TotalInteractions);
    logger.LogInformation("⭐ Replayed Satisfaction: {Score:F2}/5.0", replayedState.AverageSatisfaction);
    
    // Verify state matches
    if (replayedState.TotalInteractions == state.TotalInteractions &&
        Math.Abs(replayedState.AverageSatisfaction - state.AverageSatisfaction) < 0.01)
    {
        logger.LogInformation("✅ State validation: PERFECT MATCH!");
    }
    else
    {
        logger.LogWarning("⚠️ State mismatch detected");
    }

    // ========================================================================
    // 8. Event History Analysis
    // ========================================================================
    logger.LogInformation("\n╔════ Event History Analysis ════╗");
    
    var events = await eventStore.GetEventsAsync(assistantId);
    var eventAnalysis = events
        .GroupBy(e => e.EventType)
        .Select(g => new { 
            Type = g.Key.Split('.').Last(), 
            Count = g.Count(),
            FirstOccurred = g.Min(e => e.Timestamp),
            LastOccurred = g.Max(e => e.Timestamp)
        })
        .OrderByDescending(e => e.Count);

    foreach (var evt in eventAnalysis)
    {
        logger.LogInformation("📌 {Type}: {Count} events", evt.Type, evt.Count);
    }
    
    logger.LogInformation("\n📊 Total Events: {Count}", events.Count);

    // ========================================================================
    // 9. Create Manual Snapshot
    // ========================================================================
    logger.LogInformation("\n💾 Creating manual snapshot for optimization...");
    await assistant.CreateSnapshotAsync();
    logger.LogInformation("✅ Snapshot created successfully");
}
catch (Exception ex)
{
    logger.LogError(ex, "❌ Error in AI Event Sourcing demo");
}

logger.LogInformation("\n╔════════════════════════════════════════════╗");
logger.LogInformation("║           Demo Complete! 🎉               ║");
logger.LogInformation("╚════════════════════════════════════════════╝");

// ============================================================================
// Helper Methods
// ============================================================================

async Task SendUserMessage(
    IGAgentActor actor,
    AIAssistantAgent assistant,
    string userId,
    string conversationId,
    string message,
    ILogger logger)
{
    logger.LogInformation("👤 User: {Message}", message);
    
    // Record initial state
    var customState = assistant.GetCustomState();
    var initialInteractions = customState.TotalInteractions;
    
    // Publish message event - will be handled by HandleUserMessage event handler
    await actor.PublishEventAsync(new UserMessageReceived
    {
        UserId = userId,
        Message = message,
        ConversationId = conversationId,
        Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
    });
    
    // Wait for processing (event handlers are async)
    // In a real system, you'd implement proper async response handling
    Console.WriteLine("[DEBUG] Waiting for event handlers to process...");
    
    // Poll for completion with timeout
    var maxWaitTime = TimeSpan.FromSeconds(35); // 35 seconds total (30s AI timeout + buffer)
    var pollInterval = TimeSpan.FromMilliseconds(500);
    var startTime = DateTime.UtcNow;
    
    while (DateTime.UtcNow - startTime < maxWaitTime)
    {
        await Task.Delay(pollInterval);
        var currentInteractions = assistant.GetCustomState().TotalInteractions;
        
        if (currentInteractions > initialInteractions)
        {
            logger.LogInformation("🤖 Assistant: [Response generated via event handler - see logs above]");
            logger.LogInformation("   Total interactions: {Count}", currentInteractions);
            return; // Response processed successfully
        }
    }
    
    // Timeout reached
    logger.LogWarning("⚠️ Response timed out after {Seconds} seconds", maxWaitTime.TotalSeconds);
}