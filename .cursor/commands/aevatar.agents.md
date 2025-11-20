---
description: Aevatar Agent Maker Protocol - Use this to generate correctly structured Agents
---

## User Input

```text
$ARGUMENTS
```

If the user provides a description of an agent, use it to populate the agent's purpose, state, and config.

## Protocol: Aevatar Agent Genesis

Follow this protocol IMPLICITLY when creating a new Agent for Aevatar Framework.

### 1. Decision Matrix (Architecture Selection)

Determine the base class based on requirements:

| Requirement | Base Class | Generics | Note |
|-------------|------------|----------|------|
| **Standard Logic** | `GAgentBase<TState>` | `<TState>` | Pure business logic, no LLM. |
| **Standard + Config** | `GAgentBase<TState, TConfig>` | `<TState, TConfig>` | Needs static config (e.g. thresholds). |
| **AI Capabilities** | `AIGAgentBase<TState, TConfig>` | `<TState, TConfig>` | Needs LLM/ChatGPT. **Must** have Config. |
| **Event Sourcing** | `GAgentBaseWithEventSourcing<TState>` | `<TState>` | Audit trails, finance, replayability. |
| **AI + Event Sourcing** | `AIGAgentBaseWithEventSourcing<TState, TConfig>` | `<TState, TConfig>` | AI with audit history. |

### 2. The Iron Law of Protobuf

*   **State (`TState`)**: MUST be a Protobuf message.
*   **Config (`TConfig`)**: MUST be a Protobuf message.
*   **Events**: All published/received events MUST be Protobuf messages.
*   **NO C# POCOs** for State, Config, or Events.

### 3. Implementation Steps

#### Step 1: Define Protobuf (`.proto`)

Create a `.proto` file defining State, Config, and Events.

```protobuf
syntax = "proto3";
import "google/protobuf/timestamp.proto";

message MyState { ... }
message MyConfig { ... }
message MyEvent { ... }
```

#### Step 2: Implement Agent Class (`.cs`)

*   **No Constructor Args**: Must use parameterless constructor.
*   **Initialization**: Override `protected override async Task OnActivateAsync(CancellationToken ct)`.
*   **State Modification**: Only in `OnActivateAsync` or `[EventHandler]` methods.

```csharp
public class MyAgent : AIGAgentBase<MyState, MyConfig>
{
    protected override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        if (string.IsNullOrEmpty(State.Id)) State.Id = Id.ToString();
    }

    [EventHandler]
    public async Task HandleEvent(MyEvent evt) { ... }
}
```

#### Step 3: Runtime Configuration

Use the provided Extension Methods to configure the runtime. **DO NOT** write manual Factory registration code.

```csharp
// For Local Runtime
builder.Services.AddAevatarLocalRuntime();

// For Proto.Actor
builder.Services.AddAevatarProtoActorRuntime();

// For Orleans
builder.Services.AddAevatarOrleansRuntime();
```

### 4. Documentation

Generate a `README.md` for the agent explaining:
*   Architecture (Base class, State type)
*   Event Handlers (Inputs and effects)
*   Usage Example (using `IGAgentFactory`, **NEVER** `new Agent()`)

### 5. Critical Checklist

Before outputting code, verify:
- [ ] Used Protobuf for ALL serializable types?
- [ ] Constructor is parameterless?
- [ ] `OnActivateAsync` is `protected override`?
- [ ] Used `IGAgentFactory` in usage examples (not `new`)?
- [ ] Used `AddAevatar...Runtime()` extension methods?

