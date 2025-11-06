# AI Agent Framework Design Document

## 🎯 Overview

The AI Agent Framework extends the Aevatar Agent Framework by adding AI capabilities to agents. It provides a clean abstraction layer that supports multiple LLM backends (Semantic Kernel, Microsoft AutoGen, etc.) while maintaining the event-driven, actor-based architecture.

## 📦 Architecture

### Layer Structure

```
┌─────────────────────────────────────────────┐
│          Application Layer                   │
│         (Your AI Agents)                     │
├─────────────────────────────────────────────┤
│      AevatarAIAgentBase<TState>             │
│    (AI-Enhanced Agent Base Class)           │
├─────────────────────────────────────────────┤
│         AI Abstraction Layer                 │
│  ┌──────────────────┬────────────────────┐ │
│  │IAevatarLLMProvider│IAevatarToolManager │ │
│  │                   │                    │ │
│  │IAevatarPromptMgr  │IAevatarMemory     │ │
│  └──────────────────┴────────────────────┘ │
├─────────────────────────────────────────────┤
│         LLM Implementation Layer             │
│  ┌─────────────┬────────────────────────┐  │
│  │  Semantic   │  Microsoft AutoGen      │  │
│  │   Kernel    │  (MAF)                  │  │
│  └─────────────┴────────────────────────┘  │
├─────────────────────────────────────────────┤
│        Core Agent Framework                  │
│         (GAgentBase + Events)                │
└─────────────────────────────────────────────┘
```

## 🏗️ Core Components

### 1. AevatarAIAgentBase<TState>

The main AI-enhanced agent base class that extends GAgentBase:

```csharp
public abstract class AevatarAIAgentBase<TState> : GAgentBase<TState>
    where TState : class, IMessage, new()
{
    protected IAevatarLLMProvider LLMProvider { get; }
    protected IAevatarPromptManager PromptManager { get; }
    protected IAevatarToolManager ToolManager { get; }
    protected IAevatarMemory Memory { get; }
    protected AevatarAIAgentConfiguration Configuration { get; }
    
    // AI-specific event handlers
    [AIEventHandler]
    protected virtual async Task<IMessage?> ProcessWithAIAsync(
        EventEnvelope envelope,
        CancellationToken ct = default);
    
    // Tool execution
    protected async Task<TResult> ExecuteToolAsync<TResult>(
        string toolName, 
        Dictionary<string, object> parameters);
    
    // Conversation management
    protected async Task<string> GenerateResponseAsync(
        string prompt,
        AIContext context,
        CancellationToken ct = default);
}
```

### 2. Core Abstractions

#### ILLMProvider
```csharp
public interface ILLMProvider
{
    string ProviderId { get; }
    
    Task<LLMResponse> GenerateAsync(
        LLMRequest request,
        CancellationToken ct = default);
    
    Task<LLMResponse> GenerateStreamAsync(
        LLMRequest request,
        IAsyncEnumerable<LLMToken> tokenHandler,
        CancellationToken ct = default);
    
    Task<Embedding> GenerateEmbeddingAsync(
        string text,
        CancellationToken ct = default);
}
```

#### IPromptManager
```csharp
public interface IPromptManager
{
    // Template management
    Task<PromptTemplate> GetTemplateAsync(string templateId);
    Task RegisterTemplateAsync(string templateId, PromptTemplate template);
    
    // Prompt construction
    Task<string> BuildPromptAsync(
        string templateId,
        Dictionary<string, object> parameters);
    
    // Chain-of-thought support
    Task<string> BuildChainPromptAsync(
        IEnumerable<ThoughtStep> thoughts);
}
```

#### IAIToolManager
```csharp
public interface IAIToolManager
{
    // Tool registration
    Task RegisterToolAsync(AITool tool);
    Task<AITool?> GetToolAsync(string toolName);
    IEnumerable<AITool> GetAvailableTools();
    
    // Tool execution
    Task<ToolResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        CancellationToken ct = default);
    
    // Tool discovery for LLM
    string GenerateToolDescriptions();
}
```

#### IAIMemory
```csharp
public interface IAIMemory
{
    // Short-term memory (conversation)
    Task AddMessageAsync(ConversationMessage message);
    Task<IReadOnlyList<ConversationMessage>> GetRecentMessagesAsync(int count);
    Task ClearConversationAsync();
    
    // Long-term memory (vector store)
    Task StoreMemoryAsync(string key, MemoryItem item);
    Task<IReadOnlyList<MemoryItem>> RecallAsync(
        string query,
        int topK = 5,
        double threshold = 0.7);
    
    // Working memory (context)
    Task UpdateContextAsync(string key, object value);
    Task<T?> GetContextAsync<T>(string key);
}
```

## 🔌 LLM Provider Implementations

### Semantic Kernel Provider

```csharp
public class SemanticKernelProvider : ILLMProvider
{
    private readonly IKernel _kernel;
    private readonly ISemanticTextMemory _memory;
    
    public async Task<LLMResponse> GenerateAsync(LLMRequest request, CancellationToken ct)
    {
        var function = _kernel.CreateSemanticFunction(
            request.Prompt,
            request.Settings);
        
        var result = await _kernel.RunAsync(function, cancellationToken: ct);
        return MapToLLMResponse(result);
    }
}
```

### Microsoft AutoGen Provider

```csharp
public class AutoGenProvider : ILLMProvider
{
    private readonly IAgent _agent;
    private readonly IOrchestrator _orchestrator;
    
    public async Task<LLMResponse> GenerateAsync(LLMRequest request, CancellationToken ct)
    {
        var message = new Message(request.Prompt);
        var response = await _agent.ProcessMessageAsync(message, ct);
        return MapToLLMResponse(response);
    }
}
```

## 📝 AI Event Processing

### AI Event Handler Attribute

```csharp
[AttributeUsage(AttributeTargets.Method)]
public class AIEventHandlerAttribute : Attribute
{
    public bool UseStreaming { get; set; }
    public string? PromptTemplate { get; set; }
    public string[]? RequiredTools { get; set; }
    public AIProcessingMode Mode { get; set; } = AIProcessingMode.Standard;
}

public enum AIProcessingMode
{
    Standard,      // Single LLM call
    ChainOfThought,// Multi-step reasoning
    ReAct,        // Reason + Act pattern
    TreeOfThoughts // Parallel exploration
}
```

### Event Processing Flow

```csharp
public class AIEventProcessor
{
    public async Task<IMessage?> ProcessEventAsync(
        EventEnvelope envelope,
        AIEventHandlerAttribute config,
        AIContext context)
    {
        // 1. Extract event data
        var eventData = ExtractEventData(envelope);
        
        // 2. Build context
        var aiContext = await BuildContextAsync(eventData, context);
        
        // 3. Generate prompt
        var prompt = await PromptManager.BuildPromptAsync(
            config.PromptTemplate,
            aiContext);
        
        // 4. Execute AI processing based on mode
        var response = config.Mode switch
        {
            AIProcessingMode.ChainOfThought => await ProcessChainOfThoughtAsync(prompt),
            AIProcessingMode.ReAct => await ProcessReActAsync(prompt),
            AIProcessingMode.TreeOfThoughts => await ProcessTreeOfThoughtsAsync(prompt),
            _ => await ProcessStandardAsync(prompt)
        };
        
        // 5. Convert response to event
        return ConvertToEvent(response);
    }
}
```

## 🛠️ AI Tools

### Tool Definition

```csharp
public class AITool
{
    public string Name { get; set; }
    public string Description { get; set; }
    public ToolParameters Parameters { get; set; }
    public Func<Dictionary<string, object>, Task<object>> ExecuteAsync { get; set; }
    
    // For function calling
    public string ToFunctionDefinition()
    {
        return JsonSerializer.Serialize(new
        {
            name = Name,
            description = Description,
            parameters = Parameters.ToJsonSchema()
        });
    }
}
```

### Built-in Tools

```csharp
public static class BuiltInTools
{
    public static AITool CreateEventPublishTool() => new()
    {
        Name = "publish_event",
        Description = "Publish an event to the agent stream",
        Parameters = new ToolParameters
        {
            ["event_type"] = new() { Type = "string", Required = true },
            ["payload"] = new() { Type = "object", Required = true },
            ["direction"] = new() { Type = "string", Enum = ["up", "down", "both"] }
        },
        ExecuteAsync = async (args) =>
        {
            // Implementation
            return new { success = true, eventId = Guid.NewGuid() };
        }
    };
    
    public static AITool CreateStateQueryTool() => new()
    {
        Name = "query_state",
        Description = "Query agent state information",
        // ... implementation
    };
    
    public static AITool CreateMemorySearchTool() => new()
    {
        Name = "search_memory",
        Description = "Search long-term memory",
        // ... implementation
    };
}
```

## 🎯 Usage Examples

### Simple AI Agent

```csharp
public class CustomerSupportAgent : AIGAgentBase<CustomerSupportState>
{
    protected override void ConfigureAI(AIConfiguration config)
    {
        config.Provider = "SemanticKernel";
        config.Model = "gpt-4";
        config.Temperature = 0.7;
        config.SystemPrompt = @"
            You are a helpful customer support agent.
            Be polite, professional, and solution-oriented.
        ";
    }
    
    [AIEventHandler(PromptTemplate = "customer_inquiry")]
    protected async Task<IMessage?> HandleCustomerInquiry(CustomerInquiryEvent evt)
    {
        // AI automatically processes the event using the template
        // The base class handles the LLM interaction
        return null; // Base class will handle response generation
    }
    
    [EventHandler]
    protected async Task HandleManualEscalation(EscalationEvent evt)
    {
        // Mix AI and traditional handlers
        await NotifyHumanAgent(evt);
    }
}
```

### Advanced AI Agent with Tools

```csharp
public class DataAnalysisAgent : AIGAgentBase<AnalysisState>
{
    protected override void ConfigureAI(AIConfiguration config)
    {
        config.Provider = "AutoGen";
        config.Model = "gpt-4";
        config.SystemPrompt = "You are a data analysis expert.";
        
        // Register tools
        config.Tools.Add(new SQLQueryTool());
        config.Tools.Add(new ChartGenerationTool());
        config.Tools.Add(new ReportGenerationTool());
    }
    
    [AIEventHandler(
        Mode = AIProcessingMode.ReAct,
        RequiredTools = ["sql_query", "generate_chart"])]
    protected async Task<IMessage?> AnalyzeDataRequest(DataAnalysisRequest request)
    {
        // ReAct pattern: Reason about the task, then act using tools
        // The framework handles the reasoning loop
        return null;
    }
}
```

### Multi-Agent Collaboration

```csharp
public class ResearchCoordinatorAgent : AIGAgentBase<ResearchState>
{
    [AIEventHandler(Mode = AIProcessingMode.TreeOfThoughts)]
    protected async Task<IMessage?> CoordinateResearch(ResearchTopicEvent topic)
    {
        // Tree of Thoughts: Explore multiple research paths in parallel
        // Automatically creates child agents for parallel exploration
        return null;
    }
    
    protected override async Task OnThoughtBranchCompleted(
        ThoughtBranch branch,
        object result)
    {
        // Aggregate results from parallel explorations
        State.ResearchPaths.Add(branch.Path, result);
        
        // Publish aggregated findings
        await PublishAsync(new ResearchFindingsEvent
        {
            Topic = State.CurrentTopic,
            Findings = State.ResearchPaths
        });
    }
}
```

## 🔄 Integration with Existing Framework

### Event Flow with AI

```
User Input → EventEnvelope → AIGAgentBase
                                   ↓
                            AI Processing
                                   ↓
                         [Check if AI Handler exists]
                            ↙️              ↘️
                      Yes                    No
                        ↓                     ↓
                  ILLMProvider          Regular Handler
                        ↓                     ↓
                   Generate             Process Event
                   Response                   ↓
                        ↓              Return Response
                 Parse & Execute
                  Tools if needed
                        ↓
                 Convert to Event
                        ↓
                  Publish Event
```

### State Management

```csharp
// AI-enhanced state with memory
message AIAgentState {
    // Regular state fields
    string agent_id = 1;
    int64 version = 2;
    
    // AI-specific fields
    ConversationHistory conversation = 3;
    WorkingMemory working_memory = 4;
    ToolExecutionHistory tool_history = 5;
}

message ConversationHistory {
    repeated ConversationMessage messages = 1;
    string session_id = 2;
}

message WorkingMemory {
    map<string, google.protobuf.Any> context = 1;
    repeated string active_goals = 2;
}
```

## 🚀 Configuration

### AI Configuration in appsettings.json

```json
{
  "AIAgents": {
    "DefaultProvider": "SemanticKernel",
    "Providers": {
      "SemanticKernel": {
        "Endpoint": "https://api.openai.com/v1",
        "ApiKey": "${OPENAI_API_KEY}",
        "Model": "gpt-4",
        "MaxTokens": 4000,
        "Temperature": 0.7
      },
      "AutoGen": {
        "OrchestratorType": "Sequential",
        "MaxRounds": 10,
        "Models": {
          "Primary": "gpt-4",
          "Fallback": "gpt-3.5-turbo"
        }
      }
    },
    "Memory": {
      "Provider": "Qdrant",
      "Endpoint": "http://localhost:6333",
      "Collection": "agent_memories",
      "EmbeddingModel": "text-embedding-ada-002"
    },
    "Tools": {
      "EnableBuiltInTools": true,
      "CustomToolsAssembly": "MyCompany.AITools"
    }
  }
}
```

## 📊 Monitoring & Observability

### AI-Specific Metrics

```csharp
public class AIMetrics
{
    // LLM Metrics
    public Counter LLMRequestsTotal { get; }
    public Histogram LLMRequestDuration { get; }
    public Counter TokensConsumed { get; }
    public Gauge ActiveConversations { get; }
    
    // Tool Metrics
    public Counter ToolExecutionsTotal { get; }
    public Histogram ToolExecutionDuration { get; }
    public Counter ToolExecutionErrors { get; }
    
    // Memory Metrics
    public Gauge MemoryItemsStored { get; }
    public Histogram MemoryRecallDuration { get; }
    public Counter MemoryRecallHits { get; }
}
```

### Logging

```csharp
// Structured logging for AI operations
Logger.LogInformation(
    "AI processing completed for event {EventId}. " +
    "Model: {Model}, Tokens: {Tokens}, Duration: {Duration}ms",
    eventId, model, tokenCount, duration);
```

## 🔒 Security Considerations

### Prompt Injection Prevention

```csharp
public class PromptSanitizer
{
    public string Sanitize(string userInput)
    {
        // Remove potential injection patterns
        // Validate against known attack vectors
        // Escape special characters
        return sanitizedInput;
    }
}
```

### Tool Execution Sandboxing

```csharp
public class ToolSandbox
{
    public async Task<ToolResult> ExecuteInSandbox(
        AITool tool,
        Dictionary<string, object> parameters)
    {
        // Validate parameters
        // Check permissions
        // Execute in isolated context
        // Monitor resource usage
        return result;
    }
}
```

## 📈 Performance Optimization

### Caching Strategy

```csharp
public interface IAICacheManager
{
    // Response caching
    Task<LLMResponse?> GetCachedResponseAsync(string promptHash);
    Task CacheResponseAsync(string promptHash, LLMResponse response);
    
    // Embedding caching
    Task<Embedding?> GetCachedEmbeddingAsync(string textHash);
    Task CacheEmbeddingAsync(string textHash, Embedding embedding);
    
    // Tool result caching
    Task<ToolResult?> GetCachedToolResultAsync(string toolCall);
    Task CacheToolResultAsync(string toolCall, ToolResult result);
}
```

### Batching & Streaming

```csharp
public class AIBatchProcessor
{
    public async Task<IEnumerable<LLMResponse>> ProcessBatchAsync(
        IEnumerable<LLMRequest> requests)
    {
        // Group similar requests
        // Execute in parallel where possible
        // Return results maintaining order
    }
}
```

## 🎭 Testing Support

### Mock Providers

```csharp
public class MockLLMProvider : ILLMProvider
{
    private readonly Dictionary<string, string> _responses;
    
    public Task<LLMResponse> GenerateAsync(LLMRequest request, CancellationToken ct)
    {
        // Return predetermined responses for testing
        if (_responses.TryGetValue(request.Prompt, out var response))
        {
            return Task.FromResult(new LLMResponse { Content = response });
        }
        
        return Task.FromResult(new LLMResponse { Content = "Mock response" });
    }
}
```

## 🚧 Roadmap

### Phase 1: Core Implementation
- [ ] Basic AIGAgentBase implementation
- [ ] ILLMProvider abstraction
- [ ] Semantic Kernel provider
- [ ] Basic prompt management
- [ ] Simple tool execution

### Phase 2: Advanced Features
- [ ] Microsoft AutoGen provider
- [ ] Advanced prompt templates
- [ ] Chain-of-thought reasoning
- [ ] Vector memory integration
- [ ] Tool orchestration

### Phase 3: Production Features
- [ ] Multi-modal support (vision, audio)
- [ ] Advanced caching strategies
- [ ] Distributed AI processing
- [ ] Model fine-tuning integration
- [ ] Comprehensive monitoring

### Phase 4: Ecosystem
- [ ] Plugin system for custom providers
- [ ] Marketplace for AI tools
- [ ] Pre-built agent templates
- [ ] Visual agent designer
- [ ] Performance profiler

## 📚 Dependencies

### Required NuGet Packages

```xml
<ItemGroup>
  <!-- Core AI -->
  <PackageReference Include="Microsoft.SemanticKernel" Version="1.x.x" />
  <PackageReference Include="Microsoft.AutoGen" Version="0.x.x" />
  
  <!-- Vector Stores -->
  <PackageReference Include="Microsoft.SemanticKernel.Connectors.Qdrant" Version="1.x.x" />
  <PackageReference Include="Microsoft.SemanticKernel.Connectors.Redis" Version="1.x.x" />
  
  <!-- Observability -->
  <PackageReference Include="OpenTelemetry.Instrumentation.AI" Version="x.x.x" />
</ItemGroup>
```

## 🎯 Design Principles

1. **Abstraction First**: Hide LLM provider complexity
2. **Event-Driven**: Maintain compatibility with existing event system
3. **Tool Composability**: Easy to add and combine tools
4. **Memory Hierarchy**: Short-term, long-term, and working memory
5. **Testability**: Mock providers and deterministic testing
6. **Observability**: Comprehensive metrics and logging
7. **Security**: Built-in protection against common AI risks
8. **Performance**: Caching, batching, and streaming support

---

*Design Version: 1.0.0*
*Date: 2024-01*
*Status: Proposal*
