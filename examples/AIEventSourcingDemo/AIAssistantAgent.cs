using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace AIEventSourcingDemo;

/// <summary>
/// AI Assistant Agent with Event Sourcing
/// Demonstrates how AI decisions and state changes are captured as events
/// </summary>
public class AIAssistantAgent : AIGAgentBaseWithEventSourcing<AIAssistantState, AIAssistantConfig>
{
    // Current conversation context
    private string? _currentConversationId;
    private int _currentMessageCount;
    private int _currentTokenCount;

    public AIAssistantAgent() : base()
    {
    }

    public AIAssistantAgent(Guid id) : base(id)
    {
    }

    /// <summary>
    /// Initialize the assistant by raising an initialization event
    /// </summary>
    public async Task InitializeAssistantAsync(string name)
    {
        // Raise initialization event - state will be updated through event handler and TransitionState
        RaiseEvent(new AssistantInitializedEvent
        {
            AssistantId = Id.ToString(),
            Name = name,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });
        
        // Commit the initialization event
        await ConfirmEventsAsync();
    }
    
    /// <summary>
    /// Get the current state for external access
    /// </summary>
    public AIAssistantState GetCurrentState() => State;
    
    /// <summary>
    /// Commit pending events (public wrapper)
    /// </summary>
    public async Task CommitEventsAsync()
    {
        await ConfirmEventsAsync();
    }

    /// <summary>
    /// Configure assistant-specific settings
    /// </summary>
    protected override void ConfigureCustom(AIAssistantConfig config)
    {
        config.Personality = "Friendly and helpful AI assistant";
        config.Capabilities.Add("General knowledge");
        config.Capabilities.Add("Code assistance");
        config.Capabilities.Add("Creative writing");
        config.CreativityLevel = 0.7;
        config.EnableLearning = true;
    }

    /// <summary>
    /// System prompt for the AI assistant
    /// </summary>
    public override string SystemPrompt => $@"
You are {Config.Personality}.

Your capabilities include:
{string.Join("\n", Config.Capabilities.Select(c => $"- {c}"))}

Creativity level: {Config.CreativityLevel}
Learning enabled: {Config.EnableLearning}

Provide helpful, accurate, and engaging responses.
Be concise but thorough.
";

    /// <summary>
    /// Event handler for initialization event
    /// </summary>
    [EventHandler(Priority = 1)]
    public async Task HandleInitialization(AssistantInitializedEvent evt)
    {
        Logger?.LogInformation("Assistant {Name} initialized with ID {Id}", evt.Name, evt.AssistantId);
        // State transition is handled by TransitionState method
        // This handler can perform additional async operations if needed
        await Task.CompletedTask;
    }

    /// <summary>
    /// Event handler for user messages
    /// </summary>
    [EventHandler]
    public async Task HandleUserMessage(UserMessageReceived evt)
    {
        Logger?.LogDebug("Processing user message in conversation {ConversationId}", evt.ConversationId);
        // State updates happen through TransitionState
        await Task.CompletedTask;
    }

    /// <summary>
    /// Event handler for assistant responses
    /// </summary>
    [EventHandler]
    public async Task HandleAssistantResponse(AssistantResponseGenerated evt)
    {
        Logger?.LogDebug("Assistant generated response with {Tokens} tokens", evt.TokensUsed);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Event handler for feedback
    /// </summary>
    [EventHandler]
    public async Task HandleFeedback(FeedbackReceived evt)
    {
        Logger?.LogInformation("Received feedback for conversation {ConversationId}: {Score}/5.0", 
            evt.ConversationId, evt.SatisfactionScore);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Event handler for conversation completion
    /// </summary>
    [EventHandler]
    public async Task HandleConversationComplete(ConversationCompleted evt)
    {
        Logger?.LogInformation("Conversation {ConversationId} completed with {Messages} messages", 
            evt.ConversationId, evt.TotalMessages);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handle incoming user messages (public API)
    /// </summary>
    public async Task<string> HandleUserMessageAsync(
        string userId,
        string message,
        CancellationToken cancellationToken = default)
    {
        // Start new conversation if needed
        if (string.IsNullOrEmpty(_currentConversationId))
        {
            _currentConversationId = Guid.NewGuid().ToString();
            _currentMessageCount = 0;
            _currentTokenCount = 0;
        }

        // Raise event for user message
        RaiseEvent(new UserMessageReceived
        {
            UserId = userId,
            Message = message,
            ConversationId = _currentConversationId,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        _currentMessageCount++;

        // Generate AI response
        var chatRequest = CreateChatRequest(message);
        var response = await ChatAsync(chatRequest, cancellationToken);

        // Raise event for assistant response
        var tokensUsed = response.Usage?.TotalTokens ?? 0;
        _currentTokenCount += tokensUsed;

        RaiseEvent(new AssistantResponseGenerated
        {
            Response = response.Content,
            TokensUsed = tokensUsed,
            ConfidenceScore = 0.95, // Could be calculated based on model metrics
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        // Commit events
        await ConfirmEventsAsync(cancellationToken);

        Logger.LogInformation(
            "Processed message in conversation {ConversationId}. Messages: {MessageCount}, Tokens: {TokenCount}",
            _currentConversationId, _currentMessageCount, _currentTokenCount);

        return response.Content;
    }

    /// <summary>
    /// Handle user feedback
    /// </summary>
    public async Task ProvideFeedbackAsync(
        double satisfactionScore,
        string? feedbackText = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_currentConversationId))
        {
            Logger?.LogWarning("No active conversation to provide feedback for");
            return;
        }

        // Raise feedback event
        RaiseEvent(new FeedbackReceived
        {
            ConversationId = _currentConversationId,
            SatisfactionScore = satisfactionScore,
            FeedbackText = feedbackText ?? "",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        await ConfirmEventsAsync(cancellationToken);

        Logger?.LogInformation(
            "Received feedback for conversation {ConversationId}: Score {Score}",
            _currentConversationId, satisfactionScore);
    }

    /// <summary>
    /// Complete the current conversation
    /// </summary>
    public async Task CompleteConversationAsync(
        string topic,
        double finalSatisfaction,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_currentConversationId))
        {
            Logger?.LogWarning("No active conversation to complete");
            return;
        }

        // Raise conversation completed event
        RaiseEvent(new ConversationCompleted
        {
            ConversationId = _currentConversationId,
            TotalMessages = _currentMessageCount,
            TotalTokens = _currentTokenCount,
            FinalSatisfaction = finalSatisfaction,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        await ConfirmEventsAsync(cancellationToken);

        Logger?.LogInformation(
            "Completed conversation {ConversationId}: {Messages} messages, {Tokens} tokens, Satisfaction: {Satisfaction}",
            _currentConversationId, _currentMessageCount, _currentTokenCount, finalSatisfaction);

        // Reset conversation context
        _currentConversationId = null;
        _currentMessageCount = 0;
        _currentTokenCount = 0;
    }

    /// <summary>
    /// Pure functional state transitions for event sourcing
    /// </summary>
    protected override void TransitionState(AIAssistantState state, IMessage evt)
    {
        switch (evt)
        {
            case AssistantInitializedEvent initEvent:
                state.AssistantId = initEvent.AssistantId;
                state.Name = initEvent.Name;
                state.CreatedAt = initEvent.CreatedAt;
                state.TotalInteractions = 0;
                state.AverageSatisfaction = 0;
                break;

            case UserMessageReceived userMessage:
                state.TotalInteractions++;
                state.LastInteraction = Timestamp.FromDateTime(DateTime.UtcNow);
                break;

            case AssistantResponseGenerated response:
                // Could track response metrics here
                break;

            case FeedbackReceived feedback:
                // Update average satisfaction
                var currentTotal = state.AverageSatisfaction * (state.TotalInteractions - 1);
                state.AverageSatisfaction = (currentTotal + feedback.SatisfactionScore) / state.TotalInteractions;
                break;

            case ConversationCompleted completed:
                // Add to conversation history
                state.ConversationHistory.Add(new ConversationSummary
                {
                    ConversationId = completed.ConversationId,
                    UserId = "user", // Would be from actual user context
                    Topic = "General", // Would be determined by AI
                    MessageCount = completed.TotalMessages,
                    TokensUsed = completed.TotalTokens,
                    SatisfactionScore = completed.FinalSatisfaction,
                    Timestamp = completed.Timestamp
                });

                // Keep only last 100 conversations
                while (state.ConversationHistory.Count > 100)
                {
                    state.ConversationHistory.RemoveAt(0);
                }

                break;
        }
    }

    /// <summary>
    /// Auto-confirm events after AI operations
    /// </summary>
    protected override bool AutoConfirmEvents => true;

    /// <summary>
    /// Snapshot every 50 events
    /// </summary>
    protected override Aevatar.Agents.Core.EventSourcing.ISnapshotStrategy SnapshotStrategy =>
        new Aevatar.Agents.Core.EventSourcing.IntervalSnapshotStrategy(50);

    /// <summary>
    /// Get agent description
    /// </summary>
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"AI Assistant '{State.Name}' - {State.TotalInteractions} interactions, " +
                               $"Satisfaction: {State.AverageSatisfaction:F2}/5.0");
    }
}