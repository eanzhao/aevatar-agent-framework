# Aevatar.Agents.AI.Core

AI-enhanced agent framework for building intelligent, autonomous agents with LLM capabilities.

## 🚀 Features

- **Multiple LLM Providers**: Support for Semantic Kernel, Microsoft AutoGen, and custom providers
- **Advanced Reasoning**: Chain-of-thought, ReAct, and Tree-of-thoughts processing modes
- **Tool Management**: Dynamic tool registration and execution with Function Calling support
- **Memory System**: Short-term (conversation), long-term (vector store), and working memory
- **Prompt Engineering**: Template management, optimization, and few-shot learning
- **Event-Driven AI**: Seamless integration with the Aevatar Agent Framework's event system

## 📦 Installation

```bash
dotnet add package Aevatar.Agents.AI.Core --version 1.0.0-alpha
```

## 🎯 Quick Start

### 1. Create an AI Agent

```csharp
public class CustomerSupportAgent : AevatarAIAgentBase<CustomerSupportState>
{
    protected override void ConfigureAI(AevatarAIAgentConfiguration config)
    {
        config.Provider = "SemanticKernel";
        config.Model = "gpt-4";
        config.Temperature = 0.7;
        config.SystemPrompt = @"
            You are a helpful customer support agent.
            Be polite, professional, and solution-oriented.
        ";
    }
    
    [AevatarAIEventHandler(PromptTemplate = "customer_inquiry")]
    protected async Task<IMessage?> HandleCustomerInquiry(CustomerInquiryEvent evt)
    {
        // AI automatically processes the event
        return null;
    }
}
```

### 2. Configure Services

```csharp
services.AddSingleton<IAevatarLLMProvider, SemanticKernelProvider>();
services.AddSingleton<IAevatarPromptManager, FilePromptManager>();
services.AddSingleton<IAevatarToolManager, AevatarToolManager>();
services.AddSingleton<IAevatarMemory, InMemoryAIMemory>();
```

### 3. Initialize and Run

```csharp
var agent = new CustomerSupportAgent();
await agent.InitializeAIAsync(
    llmProvider,
    promptManager,
    toolManager,
    memory);

// Agent is ready to process events with AI
```

## 🏗️ Architecture

### Core Components

- **`AevatarAIAgentBase<TState>`**: Base class for AI-enhanced agents
- **`IAevatarLLMProvider`**: Abstraction for LLM backends
- **`IAevatarPromptManager`**: Template and prompt management
- **`IAevatarToolManager`**: Tool/function management
- **`IAevatarMemory`**: Multi-tier memory system

### Processing Modes

1. **Standard**: Single LLM call with optional tool usage
2. **Chain-of-Thought**: Step-by-step reasoning
3. **ReAct**: Reasoning + Acting pattern
4. **Tree-of-Thoughts**: Parallel exploration of solution paths

## 🛠️ Advanced Usage

### Custom Tools

```csharp
var sqlTool = new AevatarTool
{
    Name = "execute_sql",
    Description = "Execute SQL query on database",
    Parameters = new AevatarToolParameters
    {
        ["query"] = new() { Type = "string", Required = true },
        ["database"] = new() { Type = "string", DefaultValue = "main" }
    },
    ExecuteAsync = async (parameters, context, ct) =>
    {
        var query = parameters["query"].ToString();
        // Execute query and return results
        return results;
    }
};

await toolManager.RegisterToolAsync(sqlTool);
```

### Memory Management

```csharp
// Store long-term memory
await memory.StoreMemoryAsync(new AevatarMemoryItem
{
    Content = "Customer prefers email communication",
    Tags = new[] { "customer", "preferences" },
    Importance = 0.8
});

// Recall relevant memories
var memories = await memory.RecallAsync(
    "How does the customer prefer to be contacted?",
    new AevatarRecallOptions { TopK = 3, Threshold = 0.7 });
```

### Prompt Templates

```csharp
await promptManager.RegisterTemplateAsync("customer_inquiry", new AevatarPromptTemplate
{
    Content = @"
        Customer Query: {{question}}
        Customer History: {{history}}
        
        Please provide a helpful response that:
        1. Addresses the customer's concern
        2. Offers a solution or next steps
        3. Maintains a professional tone
    ",
    Parameters = new Dictionary<string, AevatarTemplateParameter>
    {
        ["question"] = new() { Type = "string", Required = true },
        ["history"] = new() { Type = "string", Required = false }
    }
});
```

## 🔌 Provider Implementations

### Semantic Kernel

```csharp
public class SemanticKernelProvider : IAevatarLLMProvider
{
    // Implementation using Microsoft Semantic Kernel
}
```

### Microsoft AutoGen

```csharp
public class AutoGenProvider : IAevatarLLMProvider
{
    // Implementation using Microsoft AutoGen
}
```

## 📊 Monitoring

Track AI agent performance with built-in metrics:

- LLM request count and latency
- Token consumption
- Tool execution statistics
- Memory usage and recall performance

## 🚧 Roadmap

- [ ] Semantic Kernel provider implementation
- [ ] AutoGen provider implementation
- [ ] Vector store integrations (Qdrant, Pinecone, Weaviate)
- [ ] Multi-modal support (vision, audio)
- [ ] Fine-tuning integration
- [ ] Distributed AI processing
- [ ] Visual agent designer

## 📄 License

MIT License - see LICENSE file for details.

## 🤝 Contributing

Contributions are welcome! Please read our contributing guidelines before submitting PRs.

## 📚 Documentation

For detailed documentation, visit [docs.aevatar.ai](https://docs.aevatar.ai)

## 💬 Support

- GitHub Issues: [github.com/aevatar/agent-framework/issues](https://github.com/aevatar/agent-framework/issues)
- Discord: [discord.gg/aevatar](https://discord.gg/aevatar)
- Email: support@aevatar.ai
