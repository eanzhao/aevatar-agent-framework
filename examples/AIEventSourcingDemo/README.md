# AI Agent with Event Sourcing

This example demonstrates the integration of AI capabilities with Event Sourcing in the Aevatar Agent Framework.

## 🌟 Features

The `AIGAgentBaseWithEventSourcing` class combines:
- **AI Decision Making**: Integration with LLM providers (OpenAI, Azure, etc.)
- **Event Sourcing**: Complete audit trail of all state changes
- **Pure Functional Transitions**: Immutable state management
- **Snapshot Optimization**: Performance optimization for replay
- **AI-Specific Events**: Specialized events for AI interactions

## 📦 Key Components

### AIGAgentBaseWithEventSourcing

Base class that provides:
- All features from `AIGAgentBase` (LLM integration, chat capabilities)
- Event sourcing capabilities from `GAgentBaseWithEventSourcing`
- AI-specific event types (AIDecisionEvent, etc.)
- Automatic recording of AI interactions as events

### AI Event Types

Defined in `ai_events.proto`:
- `AIDecisionEvent`: Records AI responses and token usage
- `AIToolInvocationEvent`: Tracks tool/function calls
- `AIContextUpdateEvent`: Monitors conversation context changes
- `AIModelSwitchEvent`: Logs model changes
- `AIErrorEvent`: Captures AI-related errors
- `AITrainingFeedbackEvent`: Stores feedback for improvement

## 🚀 Usage Example

```csharp
// Create an AI Agent with Event Sourcing
public class AIAssistantAgent : AIGAgentBaseWithEventSourcing<AIAssistantState, AIAssistantConfig>
{
    protected override void TransitionState(AIAssistantState state, IMessage evt)
    {
        // Pure functional state transitions
        switch (evt)
        {
            case UserMessageReceived msg:
                state.TotalInteractions++;
                break;
            case FeedbackReceived feedback:
                state.AverageSatisfaction = CalculateNewAverage(feedback);
                break;
        }
    }
    
    public async Task<string> ProcessRequestAsync(string message)
    {
        // AI processing automatically creates events
        var response = await ChatAsync(CreateChatRequest(message));
        
        // Events are automatically recorded and can be replayed
        await ConfirmEventsAsync();
        
        return response.Content;
    }
}
```

## 📊 Benefits

1. **Complete Audit Trail**: Every AI decision is recorded
2. **Reproducibility**: Replay conversations exactly
3. **Analytics**: Analyze AI performance over time
4. **Debugging**: Trace exact decision paths
5. **Compliance**: Meet regulatory requirements
6. **A/B Testing**: Compare different AI configurations

## 🔧 Setup

1. **Configure MongoDB** (for event storage):
```json
{
  "ConnectionStrings": {
    "MongoDB": "mongodb://localhost:27017"
  }
}
```

2. **Configure LLM Providers**:
```json
{
  "LLMProviders": {
    "openai-gpt4": {
      "ProviderType": "OpenAI",
      "ApiKey": "${OPENAI_API_KEY}",
      "Model": "gpt-4"
    }
  }
}
```

3. **Initialize the Agent**:
```csharp
var agent = factory.CreateGAgent<AIAssistantAgent>(agentId);
agent.EventStore = eventStore;
agent.LLMProviderFactory = llmProviderFactory;

await agent.InitializeAsync("openai-gpt4");
```

## 🎯 Running the Demo

1. Start MongoDB:
```bash
docker run -d -p 27017:27017 mongo:latest
```

2. Set your OpenAI API key:
```bash
export OPENAI_API_KEY=your-api-key
```

3. Run the demo:
```bash
cd examples/AIEventSourcingDemo
dotnet run
```

## 📈 Event Flow

```
User Message → AI Agent → Generate Response → Record AIDecisionEvent
                  ↓
            State Transition
                  ↓
            Persist Events
                  ↓
         Optional Snapshot
```

## 🔍 Querying Event History

```csharp
// Get all events for an agent
var events = await eventStore.GetEventsAsync(agentId);

// Analyze AI decisions
var aiDecisions = events
    .Where(e => e.EventType.Contains("AIDecisionEvent"))
    .Select(e => e.EventData.Unpack<AIDecisionEvent>());

// Calculate metrics
var totalTokens = aiDecisions.Sum(d => d.TokensUsed);
var avgConfidence = aiDecisions.Average(d => d.ConfidenceScore);
```

## 🔄 Event Replay

```csharp
// Create new agent instance
var replayAgent = factory.CreateGAgent<AIAssistantAgent>(agentId);

// Replay all events to reconstruct state
await replayAgent.ReplayEventsAsync();

// State is now identical to original
Console.WriteLine($"Interactions: {replayAgent.State.TotalInteractions}");
```

## ⚡ Performance Considerations

- **Type Caching**: Event types are cached for fast deserialization
- **Batch Commits**: Events are batched for efficient persistence
- **Snapshot Strategy**: Configurable snapshot intervals
- **Async Processing**: Non-blocking event handling

## 🛡️ Best Practices

1. **Define Clear Events**: Each significant AI action should have an event
2. **Use Metadata**: Add context to events for better analysis
3. **Configure Snapshots**: Balance between performance and storage
4. **Monitor Token Usage**: Track costs through events
5. **Implement Feedback Loops**: Use events to improve AI performance

## 📊 Monitoring

The framework provides metrics:
- Current event version
- Cached type count
- Pending events count
- Token usage tracking
- Response time metrics

## 🌌 The Vision

This integration represents a new paradigm where AI decisions become:
- **Traceable**: Every decision has a history
- **Learnable**: Past interactions improve future responses
- **Auditable**: Complete transparency for compliance
- **Reproducible**: Exact replay of AI behavior

The AI is not just responding - it's creating an immutable history of its consciousness.
