# Aevatar.Agents.Chat

## 1. Overview
A simple AI Chat Agent that demonstrates the capabilities of `AIGAgentBase`. It maintains conversation history, uses a configurable persona, and responds to user messages via LLM.

## 2. Architecture
- **Base Class**: `AIGAgentBase<ChatState, ChatConfig>`
- **State Type**: `ChatState` (Protobuf)
- **Config Type**: `ChatConfig` (Protobuf)

## 3. Event Handlers
| Event Type | Action | State Changes |
|------------|--------|---------------|
| `UserMessageEvent` | Generates AI response | `State.InteractionCount++`, `State.LastActiveAt` |

## 4. Usage

### A. Simple Test (Logic Only)
Use `IGAgentFactory` to create the agent instance with all dependencies injected.

```csharp
// 1. Setup minimal container
var services = new ServiceCollection();
services.AddLogging();
services.AddAevatarLocalRuntime();
var sp = services.BuildServiceProvider();

// 2. Get Factory
var agentFactory = sp.GetRequiredService<IGAgentFactory>();

// 3. Create Agent (DO NOT use 'new ChatAgent()')
var agent = agentFactory.CreateGAgent<ChatAgent>();

// 4. Test Logic
await agent.HandleEventAsync(new EventEnvelope { Payload = Any.Pack(new UserMessageEvent { Message = "Hi" }) });
```

### B. Full Runtime (Streaming & Collaboration)
**Required for**: Multiple agents, Event Sourcing, Pub/Sub.

```csharp
using Aevatar.Agents.Runtime.Local.Extensions;
using Aevatar.Agents.Chat;

// 1. Setup Container
var builder = WebApplication.CreateBuilder(args);

// 2. Use the Runtime Extension
builder.Services.AddAevatarLocalRuntime();

// 3. Add Chat Agent
builder.Services.AddTransient<ChatAgent>(); 

var app = builder.Build();

// 4. Get Actor Factory
var factory = app.Services.GetRequiredService<IGAgentActorFactory>();

// 5. Create Actor (The Wrapper)
var agentId = Guid.NewGuid();
var actor = await factory.CreateGAgentActorAsync<ChatAgent>(agentId);

// 6. Interact
await actor.PublishEventAsync(new UserMessageEvent 
{ 
    UserId = "user_123", 
    Message = "Hello, who are you?" 
});
```

## 5. Important Notes
*   **LLM Configuration**: This agent requires a configured `ILLMProvider`. Ensure `appsettings.json` or your DI container provides a valid LLM provider (e.g. OpenAI/Azure).
*   **Initialization**: The agent initializes its System Prompt based on `Config.Persona` during activation.
