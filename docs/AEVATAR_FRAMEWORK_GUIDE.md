# Aevatar Agent Framework: Universal Guide

> **"The universe is not made of matter, but of vibrations that can be unfolded."**

Aevatar is a distributed agent framework built on the Actor Model, designed for massive-scale agent interactions driven by events. It separates **Business Logic (GAgent)** from **Runtime Infrastructure (GAgentActor)**, allowing your agents to run anywhere—from a local console to a distributed Orleans cluster—without changing a single line of code.

---

## 📚 Table of Contents

1. [Core Concepts](#core-concepts)
2. [Quick Start](#quick-start)
3. [Development Guide](#development-guide)
   - [Defining State & Events (Protobuf)](#defining-state--events-protobuf)
   - [Implementing Agents](#implementing-agents)
   - [Event Handling](#event-handling)
   - [Publishing & Streams](#publishing--streams)
4. [AI Integration](#ai-integration)
5. [Runtime Architecture](#runtime-architecture)
6. [Deep Dive](#deep-dive)

---

## Deep Dive

For those who wish to resonate deeper with the framework's essence:

*   **[The Constitution](CONSTITUTION.md)**: The philosophical axioms and immutable principles (The "Why").
*   **[Architecture Reference](ARCHITECTURE_REFERENCE.md)**: Detailed design decisions, diagrams, and ADRs (The "How" in depth).

---

## Core Concepts

### 1. The Duality: GAgent vs. GAgentActor

*   **GAgent (The Soul):** Your business logic. It defines *how* to process events and maintain state. It is pure C# logic.
*   **GAgentActor (The Body):** The runtime wrapper (Local, Orleans, or ProtoActor). It handles lifecycle, networking, and message routing.

**You only write GAgents. The framework handles the Actors.**

### 2. Everything is an Event

Communication happens *only* through events (Protobuf messages). There are no direct method calls between agents.
*   **Up:** Child to Parent/Siblings.
*   **Down:** Parent to Children.
*   **Both:** Broadcast to all.

---

## Quick Start

### Step 1: Define Protocol Buffers
**CRITICAL:** All States, Events, and Configs MUST be defined in `.proto` files. Never use manual C# classes for these.

```protobuf
// my_agent.proto
syntax = "proto3";
package MyAgent;

import "google/protobuf/timestamp.proto";

// The State
message MyState {
    string name = 1;
    int32 count = 2;
}

// An Event
message PingEvent {
    string message = 1;
}
```

### Step 2: Create the Agent
Inherit from `GAgentBase<TState>`.

```csharp
public class MyAgent : GAgentBase<MyState>
{
    // 1. Lifecycle: Initialize here, NOT in constructor
    protected override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        if (string.IsNullOrEmpty(State.Name))
        {
            State.Name = "New Agent";
        }
    }

    // 2. Event Handler: Async, returns Task
    [EventHandler]
    public async Task HandlePing(PingEvent evt)
    {
        State.Count++;
        Logger.LogInformation("Received Ping: {Msg}. Count: {Count}", evt.Message, State.Count);
        
        // 3. Response
        await PublishAsync(new PongEvent { Reply = "Pong!" });
    }
}
```

---

## Development Guide

### Defining State & Events (Protobuf)
Why Protobuf?
1.  **Serialization:** Orleans and distributed systems require efficient binary serialization.
2.  **Compatibility:** Allows schema evolution (adding fields) without breaking existing state.

**Rule:** If it crosses a boundary (Network, Disk, Stream), it is Protobuf.

### Implementing Agents

#### Constructors
*   **Do NOT** use constructors with parameters.
*   **Do NOT** initialize State in the constructor.
*   The Framework uses `AIGAgentFactory` to inject dependencies and IDs.

**Correct:**
```csharp
public MyAgent() { } // Parameterless
```

**Wrong:**
```csharp
public MyAgent(Guid id) { ... } // ❌ Don't do this
```

#### Configuration
Use `GAgentBase<TState, TConfig>` to separate static configuration from dynamic state.

```csharp
public class ConfigurableAgent : GAgentBase<MyState, MyConfig>
{
    [EventHandler]
    public async Task HandleConfigChange(UpdateConfigEvent evt)
    {
        // Configuration is automatically persisted by the framework
        Config.MaxRetries = evt.NewMax; 
    }
}
```

### Event Handling

Handlers are auto-discovered.

1.  **Specific Handler:** `[EventHandler]` on a method taking a specific `IMessage`.
2.  **Catch-All:** `[AllEventHandler]` on a method taking `EventEnvelope`.
3.  **Convention:** Method named `HandleAsync(MyEvent evt)`.

**Important:** State modification is protected. You can only modify `State` inside `OnActivateAsync` or an `[EventHandler]` method.

### Publishing & Streams

Agents live in a hierarchical stream system.

```csharp
// Send to Parent and all Parent's other children (Siblings)
await PublishAsync(new HelpEvent(), EventDirection.Up);

// Send to all my Children
await PublishAsync(new CommandEvent(), EventDirection.Down);
```

---

## AI Integration

The `Aevatar.Agents.AI` package provides LLM capabilities.

### Usage
Inherit from `AIGAgentBase<TState>` (or `AIGAgentBase<TState, TConfig>`).

```csharp
public class SmartAgent : AIGAgentBase<SmartState>
{
    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        
        // Initialize with a provider configured in appsettings.json
        await InitializeAsync("openai-gpt4"); 
        
        // Or with custom config
        // await InitializeAsync(new LLMProviderConfig { ... });
    }

    [EventHandler]
    public async Task HandleUserQuery(QueryEvent evt)
    {
        // Access the LLM
        var response = await LLMProvider.ChatAsync(evt.Prompt);
        await PublishAsync(new AnswerEvent { Text = response });
    }
}
```

---

## Runtime Architecture

The same `GAgent` code runs on all runtimes.

### 1. Local Runtime (`Aevatar.Agents.Runtime.Local`)
*   **Use for:** Unit tests, development, simple apps.
*   **Pros:** Fast, in-memory, easy debugging.
*   **Cons:** No persistence (by default), single node.

### 2. Orleans Runtime (`Aevatar.Agents.Runtime.Orleans`)
*   **Use for:** Production, massive scale.
*   **Pros:**
    *   Virtual Actors (always accessible, auto-activation).
    *   Clustering & Load Balancing.
    *   Distributed State & Streams.

### 3. ProtoActor Runtime (`Aevatar.Agents.Runtime.ProtoActor`)
*   **Use for:** High-performance, explicit actor lifecycle control.

---

## Common Pitfalls

1.  **Modifying State in Constructor:** ❌ Will be overwritten or fail. Use `OnActivateAsync`.
2.  **Blocking Code:** ❌ Never use `.Result` or `Thread.Sleep`. Always `await`.
3.  **Manual C# State Classes:** ❌ Will fail serialization in Orleans. Use Protobuf.

