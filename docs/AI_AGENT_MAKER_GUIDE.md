# Aevatar Agent Maker Guide 🌌

> **For AI Agents Only**
> This protocol defines the standard operating procedure for generating Aevatar Agents.
> When a user asks you to "create an agent", follow this guide implicitly.

## 1. Decision Matrix (决策矩阵)

Before writing code, determine the Agent's nature based on user requirements:

### A. Inheritance Path (继承路径)
| Requirement | Base Class | Generics | Note |
|-------------|------------|----------|------|
| **Standard Logic** | `GAgentBase<TState>` | `<TState>` | Pure business logic, no LLM. |
| **Standard + Config** | `GAgentBase<TState, TConfig>` | `<TState, TConfig>` | Needs static config (e.g. thresholds). |
| **AI Capabilities** | `AIGAgentBase<TState, TConfig>` | `<TState, TConfig>` | Needs LLM/ChatGPT. **Must** have Config. |
| **Event Sourcing** | `GAgentBaseWithEventSourcing<TState>` | `<TState>` | Audit trails, finance, replayability. |
| **AI + Event Sourcing** | `AIGAgentBaseWithEventSourcing<TState, TConfig>` | `<TState, TConfig>` | AI with audit history. |

### B. Protobuf Requirement (铁律)
*   **State (`TState`)**: MUST be a Protobuf message.
*   **Config (`TConfig`)**: MUST be a Protobuf message.
*   **Events**: All published/received events MUST be Protobuf messages.
*   **NO C# POCOs** for these roles.

---

## 2. Implementation Protocol (实现协议)

### Step 1: Define Protobuf (.proto)
Create a `.proto` file first. Define State, Config (if needed), and Events.

```protobuf
syntax = "proto3";
import "google/protobuf/timestamp.proto";

message MyState {
    string id = 1;
    int32 count = 2;
}

message MyConfig {  // Required for AIGAgentBase
    string target_goal = 1;
}

message MyEvent {
    string content = 1;
}
```

### Step 2: Implement Agent Class
Rules:
1.  **No Constructor Args**: Use parameterless constructor.
2.  **Initialization**: Override `OnActivateAsync`.
3.  **State Modification**: ONLY in `OnActivateAsync` or methods with `[EventHandler]`.

```csharp
public class MyAgent : AIGAgentBase<MyState, MyConfig>
{
    protected override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        // Initialize default state if empty
        if (string.IsNullOrEmpty(State.Id)) State.Id = Id.ToString();
    }

    [EventHandler] 
    public async Task HandleEvent(MyEvent evt)
    {
        State.Count++; // State mutation allowed here
        await PublishAsync(new ResponseEvent());
    }
}
```

---

## 3. Configuration Helper (配置助手)

The framework provides extension methods for each runtime to simplify setup. Choose the one that matches your target runtime.

### A. Local Runtime (Development/Testing)
```csharp
using Aevatar.Agents.Runtime.Local.Extensions;

builder.Services.AddAevatarLocalRuntime();
```

### B. Proto.Actor Runtime (High Performance)
```csharp
using Aevatar.Agents.Runtime.ProtoActor.Extensions;

builder.Services.AddAevatarProtoActorRuntime();
```

### C. Orleans Runtime (Distributed)
*Note: Requires Orleans Silo configuration.*

```csharp
using Aevatar.Agents.Runtime.Orleans.Extensions;

builder.Services.AddAevatarOrleansRuntime();
```

---

## 4. Generated Documentation Template (文档模板)

For every agent you create, generate a `README.md` following this structure:

```markdown
# [Agent Name]

## 1. Overview
[Brief description of what the agent does]

## 2. Architecture
- **Base Class**: [e.g. AIGAgentBase<MyState, MyConfig>]
- **State Type**: [Protobuf Message Name]
- **Config Type**: [Protobuf Message Name]

## 3. Event Handlers
| Event Type | Action | State Changes |
|------------|--------|---------------|
| `MyEvent`  | Increments count | `State.Count` |

## 4. Usage

### A. Simple Test (Logic Only)
Use `IGAgentFactory` to create the agent instance with all dependencies injected.

```csharp
// 1. Setup minimal container
var services = new ServiceCollection();
services.AddLogging();
services.AddAevatarLocalRuntime(); // Registers factory
var sp = services.BuildServiceProvider();

// 2. Get Factory
var agentFactory = sp.GetRequiredService<IGAgentFactory>();

// 3. Create Agent (DO NOT use 'new MyAgent()')
var agent = agentFactory.CreateGAgent<MyAgent>();

// 4. Test Logic
await agent.HandleEventAsync(new EventEnvelope { ... });
```

### B. Full Runtime (Streaming & Collaboration)
**Required for**: Multiple agents, Event Sourcing, Pub/Sub.

```csharp
using Aevatar.Agents.Runtime.Local.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// 1. Setup Container
var builder = Host.CreateApplicationBuilder(args);

// 2. Load Configuration
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: true);

// 3. Configure Logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});

// 4. Use the Runtime Extension
builder.Services.AddAevatarLocalRuntime();

var app = builder.Build();
await app.StartAsync();

// 5. Get Actor Factory
var factory = app.Services.GetRequiredService<IGAgentActorFactory>();

// 6. Create Actor (The Wrapper)
var actor = await factory.CreateGAgentActorAsync<MyAgent>(Guid.NewGuid());

// 7. Interact
await actor.PublishEventAsync(new MyEvent());

await app.StopAsync();
```

## 5. Important Notes
*   **Self-Handling**: By default, this agent ignores events it publishes itself. Set `[EventHandler(AllowSelfHandling = true)]` if needed.
*   **State**: State is automatically persisted by the runtime. Do not modify `State` outside of `OnActivateAsync` or Event Handlers.

```

---

## 5. Critical Checklist for AI

Before outputting code:
1. [ ] Did I use Protobuf for State/Config/Events?
2. [ ] Is the constructor parameterless?
3. [ ] Is `OnActivateAsync` used for initialization?
4. [ ] Are all `await` calls properly awaited?
5. [ ] Did I provide the `README.md`?

**End of Protocol.** 🌌

