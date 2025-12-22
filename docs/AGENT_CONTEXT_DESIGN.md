# Agent Context Design Document

## Overview

This document describes the design of a **runtime-agnostic request context mechanism** for the Aevatar Agent Framework. The goal is to provide a way to pass contextual data (like language settings, region info, user metadata) across agent boundaries without modifying method signatures.

## Review Summary (Feasibility & Performance)

This section is a design review of the proposal, focused on **feasibility**, **performance**, and **runtime correctness** (Local / Orleans / ProtoActor).

### High-level verdict

- **Direction is feasible**: AsyncLocal-based ambient context + propagation through `EventEnvelope` is aligned with the framework's event-driven architecture.
- **Several changes are required before implementation** to avoid runtime breakage (especially in Orleans) and to control performance overhead.

### Must-fix blockers (before coding)

- **Orleans bridge is currently one-way**: Orleans `RequestContext` cannot be enumerated, so `GetAll()` returning empty means `InjectContext()` cannot propagate anything in Orleans mode.
- **Context leakage risk**: `ExtractContext()` sets ambient context but the design does not define a scope/restore mechanism. Without scoping, concurrent event handling can cross-contaminate contexts.
- **Schema/field evolution must be validated**: adding new fields to `EventEnvelope` requires strict field-number management and alignment with existing proto messages in this repository.

### Performance risks to address

- **Avoid full context serialization on every publish**. Prefer allowlisted keys and shallow/typed values.
- **Avoid string-prefix + JSON fallback** for frequently-used values. Prefer typed Protobuf representations.
- **Avoid lock-heavy context maps** on hot paths; prefer immutable snapshots or copy-on-write patterns (read-mostly).

### Issues fixed in this document (second review)

The following issues were identified and corrected during review:

1. ✅ **CorrelationId default value BUG** - Removed `Guid.NewGuid()` from static field default (would cause all requests to share one ID).
2. ✅ **GetOrCreate() not in interface** - Added extension method `AgentContextAccessorExtensions.GetOrCreate()`.
3. ✅ **ContextScope missing implementation** - Added complete `AgentContextScope` struct with usage example.
4. ✅ **AgentContextKey equality incomplete** - Added `IEquatable<T>`, `==`/`!=` operators.
5. ✅ **ProtoActor runtime missing** - Added section 3.3 clarifying it's future work (falls back to AsyncLocal for MVP).

## Problem Statement

Currently, the project uses Orleans `RequestContext` directly:

```csharp
// In Controller
RequestContext.Set("IsCN", isCN);
RequestContext.Set("GodGPTLanguage", language.ToString());

// In Agent/Grain
var context = RequestContext.Get("IsCN");
```

**Issues:**
1. **Runtime Coupling**: Orleans `RequestContext` only works in Orleans runtime
2. **No Type Safety**: Uses string keys with `object` values
3. **No Abstraction**: Direct dependency on `Orleans.Runtime` namespace
4. **Limited Propagation**: Only propagates through Orleans grain calls, not through custom event streams

## Design Goals

1. **Runtime Agnostic**: Works across Local, Orleans, and ProtoActor runtimes
2. **Type Safe**: Strongly-typed context keys and values
3. **Event-Driven Propagation**: Context flows through EventEnvelope metadata
4. **Backwards Compatible**: Can bridge with existing Orleans RequestContext
5. **Protobuf Compatible**: Context data serializable via Protobuf

## Architecture

### Layer Responsibility

```
┌─────────────────────────────────────────────────────────────────┐
│                     Application Layer                            │
│   (Controllers, Services - Set context before agent calls)       │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│              Aevatar.Agents.Abstractions                         │
│   - IAgentContext (interface)                                    │
│   - IAgentContextAccessor (interface)                            │
│   - AgentContextKey<T> (type-safe key)                           │
│   - EventEnvelope.metadata (proto field)                         │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                 Aevatar.Agents.Core                              │
│   - AsyncLocalAgentContext (default implementation)              │
│   - AgentContextPropagator (auto-propagation via events)         │
│   - AgentContextSerializer (protobuf serialization)              │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│              Runtime Implementations                             │
│                                                                  │
│   Local:    Uses AsyncLocalAgentContext directly                 │
│   Orleans:  Bridges with Orleans.Runtime.RequestContext          │
│   Proto:    Bridges with Proto.Actor message headers             │
└─────────────────────────────────────────────────────────────────┘
```

## Detailed Design

### 1. Abstractions Layer

#### 1.1 IAgentContext Interface

```csharp
namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Agent execution context for passing request-scoped data across agent boundaries.
/// Runtime agnostic - works across Local, Orleans, and ProtoActor.
/// </summary>
public interface IAgentContext
{
    /// <summary>
    /// Get context value by key.
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="key">Context key</param>
    /// <returns>Value or default if not found</returns>
    T? Get<T>(AgentContextKey<T> key);

    /// <summary>
    /// Get context value by string key (for interop with Orleans RequestContext).
    /// </summary>
    /// <param name="key">String key</param>
    /// <returns>Value or null if not found</returns>
    object? Get(string key);

    /// <summary>
    /// Set context value.
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="key">Context key</param>
    /// <param name="value">Value to set</param>
    void Set<T>(AgentContextKey<T> key, T value);

    /// <summary>
    /// Set context value by string key (for interop with Orleans RequestContext).
    /// </summary>
    /// <param name="key">String key</param>
    /// <param name="value">Value to set</param>
    void Set(string key, object? value);

    /// <summary>
    /// Remove context value.
    /// </summary>
    /// <param name="key">Context key</param>
    void Remove(string key);

    /// <summary>
    /// Clear all context values.
    /// </summary>
    void Clear();

    /// <summary>
    /// Get all context entries as dictionary (for serialization).
    /// </summary>
    IReadOnlyDictionary<string, object?> GetAll();

    /// <summary>
    /// Import context entries from dictionary (for deserialization).
    /// </summary>
    void Import(IReadOnlyDictionary<string, object?> entries);
}
```

#### 1.2 IAgentContextAccessor Interface

```csharp
namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Provides access to current agent context.
/// Similar to IHttpContextAccessor in ASP.NET Core.
/// </summary>
public interface IAgentContextAccessor
{
    /// <summary>
    /// Current agent context. May be null if not in an agent execution scope.
    /// </summary>
    IAgentContext? Context { get; set; }
}

/// <summary>
/// Extension methods for IAgentContextAccessor.
/// </summary>
public static class AgentContextAccessorExtensions
{
    /// <summary>
    /// Get existing context or create a new one.
    /// </summary>
    public static IAgentContext GetOrCreate(this IAgentContextAccessor accessor)
    {
        return accessor.Context ??= new AsyncLocalAgentContext();
    }
}
```

#### 1.3 AgentContextKey<T> Type-Safe Key

```csharp
namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Type-safe context key.
/// Prevents key collisions and ensures type safety at compile time.
/// </summary>
/// <typeparam name="T">Value type</typeparam>
public sealed class AgentContextKey<T> : IEquatable<AgentContextKey<T>>
{
    /// <summary>
    /// Key name (used for serialization and lookup).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Default value when key is not found.
    /// </summary>
    public T? DefaultValue { get; }

    public AgentContextKey(string name, T? defaultValue = default)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DefaultValue = defaultValue;
    }

    public override string ToString() => Name;
    public override int GetHashCode() => Name.GetHashCode();
    
    public bool Equals(AgentContextKey<T>? other) => 
        other is not null && Name == other.Name;
    
    public override bool Equals(object? obj) => 
        obj is AgentContextKey<T> other && Equals(other);
    
    public static bool operator ==(AgentContextKey<T>? left, AgentContextKey<T>? right) =>
        left?.Equals(right) ?? right is null;
    
    public static bool operator !=(AgentContextKey<T>? left, AgentContextKey<T>? right) =>
        !(left == right);
}
```

#### 1.4 Well-Known Context Keys

```csharp
namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Well-known agent context keys used across the framework.
/// </summary>
public static class AgentContextKeys
{
    /// <summary>
    /// Correlation ID for distributed tracing.
    /// NOTE: No default value - caller must set explicitly per request.
    /// DO NOT use Guid.NewGuid() as default (static field executes only once).
    /// </summary>
    public static readonly AgentContextKey<string> CorrelationId = 
        new("CorrelationId");

    /// <summary>
    /// User ID for authentication context.
    /// </summary>
    public static readonly AgentContextKey<string> UserId = 
        new("UserId");

    /// <summary>
    /// Language/locale setting.
    /// </summary>
    public static readonly AgentContextKey<string> Language = 
        new("Language", "en");

    /// <summary>
    /// Region indicator (e.g., for geo-routing).
    /// </summary>
    public static readonly AgentContextKey<string> Region = 
        new("Region");

    /// <summary>
    /// Whether client is in China mainland (for compliance routing).
    /// </summary>
    public static readonly AgentContextKey<bool> IsCN = 
        new("IsCN", false);

    /// <summary>
    /// Request timestamp.
    /// </summary>
    public static readonly AgentContextKey<DateTime> RequestTime = 
        new("RequestTime");

    /// <summary>
    /// Tenant ID for multi-tenant scenarios.
    /// </summary>
    public static readonly AgentContextKey<string> TenantId = 
        new("TenantId");
}
```

#### 1.5 EventEnvelope Proto Update

Add metadata field to EventEnvelope for context propagation:

```protobuf
// In abstrations_messages.proto

message EventEnvelope {
  // ... existing fields ...

  // Context metadata for request-scoped data propagation
  // Key-value pairs serialized from IAgentContext
  map<string, string> context_metadata = 16;
}
```

**Important notes (review):**

- **Repository alignment:** at the time of writing, `EventEnvelope` in this repository has fields up to `on_arrival_direction = 15`. Adding `context_metadata = 16` is intended to be the next available field number **within `EventEnvelope`**.
- **Prefer typed context over `map<string, string>`** for long-term correctness and performance.
  - Recommended option: `map<string, google.protobuf.Any> context = N;`
  - Alternative: `repeated ContextEntry { string key; google.protobuf.Any value; }`
- If keeping `map<string, string>`, strictly enforce:
  - **Allowlist** of keys that can be propagated
  - **Size limits** (max keys / max total bytes)
  - **No sensitive values** unless encrypted/signed
  - **Stable encoding** (avoid ambiguous JSON/object fallbacks on hot paths)

### 2. Core Layer Implementation

#### 2.1 AsyncLocalAgentContext

```csharp
namespace Aevatar.Agents.Core;

/// <summary>
/// Default IAgentContext implementation using AsyncLocal for async flow.
/// Works for Local runtime and as base for other runtimes.
/// </summary>
public class AsyncLocalAgentContext : IAgentContext
{
    private readonly Dictionary<string, object?> _data = new();
    private readonly object _lock = new();

    public T? Get<T>(AgentContextKey<T> key)
    {
        lock (_lock)
        {
            return _data.TryGetValue(key.Name, out var value) && value is T typedValue
                ? typedValue
                : key.DefaultValue;
        }
    }

    public object? Get(string key)
    {
        lock (_lock)
        {
            return _data.TryGetValue(key, out var value) ? value : null;
        }
    }

    public void Set<T>(AgentContextKey<T> key, T value)
    {
        lock (_lock)
        {
            _data[key.Name] = value;
        }
    }

    public void Set(string key, object? value)
    {
        lock (_lock)
        {
            _data[key] = value;
        }
    }

    public void Remove(string key)
    {
        lock (_lock)
        {
            _data.Remove(key);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _data.Clear();
        }
    }

    public IReadOnlyDictionary<string, object?> GetAll()
    {
        lock (_lock)
        {
            return new Dictionary<string, object?>(_data);
        }
    }

    public void Import(IReadOnlyDictionary<string, object?> entries)
    {
        lock (_lock)
        {
            foreach (var (key, value) in entries)
            {
                _data[key] = value;
            }
        }
    }
}
```

#### 2.2 AsyncLocalAgentContextAccessor

```csharp
namespace Aevatar.Agents.Core;

/// <summary>
/// Default IAgentContextAccessor using AsyncLocal for async flow preservation.
/// </summary>
public class AsyncLocalAgentContextAccessor : IAgentContextAccessor
{
    private static readonly AsyncLocal<IAgentContext?> _current = new();

    public IAgentContext? Context
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    /// <summary>
    /// Get or create current context.
    /// </summary>
    public IAgentContext GetOrCreate()
    {
        return Context ??= new AsyncLocalAgentContext();
    }
}
```

#### 2.3 AgentContextSerializer

```csharp
namespace Aevatar.Agents.Core;

/// <summary>
/// Serializes/deserializes agent context to/from EventEnvelope metadata.
/// </summary>
public static class AgentContextSerializer
{
    /// <summary>
    /// Serialize context to string dictionary for EventEnvelope.
    /// </summary>
    public static IDictionary<string, string> Serialize(IAgentContext context)
    {
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in context.GetAll())
        {
            if (value != null)
            {
                result[key] = SerializeValue(value);
            }
        }
        return result;
    }

    /// <summary>
    /// Deserialize context from EventEnvelope metadata.
    /// </summary>
    public static void Deserialize(
        IDictionary<string, string> metadata, 
        IAgentContext context)
    {
        foreach (var (key, value) in metadata)
        {
            context.Set(key, DeserializeValue(value));
        }
    }

    private static string SerializeValue(object value)
    {
        return value switch
        {
            string s => $"s:{s}",
            bool b => $"b:{b}",
            int i => $"i:{i}",
            long l => $"l:{l}",
            double d => $"d:{d}",
            DateTime dt => $"dt:{dt:O}",
            Guid g => $"g:{g}",
            _ => $"j:{System.Text.Json.JsonSerializer.Serialize(value)}"
        };
    }

    private static object? DeserializeValue(string serialized)
    {
        if (string.IsNullOrEmpty(serialized) || serialized.Length < 2)
            return serialized;

        var prefix = serialized[..2];
        var value = serialized[2..];

        return prefix switch
        {
            "s:" => value,
            "b:" => bool.Parse(value),
            "i:" => int.Parse(value),
            "l:" => long.Parse(value),
            "d:" => double.Parse(value),
            "dt:" => DateTime.Parse(value),
            "g:" => Guid.Parse(value),
            "j:" => System.Text.Json.JsonSerializer.Deserialize<object>(value),
            _ => serialized
        };
    }
}
```

**Review recommendations (Serializer):**

- **Avoid `JsonSerializer.Deserialize<object>`** for non-trivial types. It often deserializes into `JsonElement`, making `Get<T>` unreliable and increasing CPU/allocations.
- If you must keep string encoding, prefer:
  - Allowlist of value types (string/bool/int/long/double/DateTime/Guid)
  - Reject/skip unsupported types instead of JSON fallback, unless explicitly needed
- For best performance and compatibility, use **Protobuf `Any`** for values and avoid string encoding entirely.

#### 2.4 AgentContextPropagator

```csharp
namespace Aevatar.Agents.Core;

/// <summary>
/// Automatically propagates context through event publishing.
/// Integrates with IEventPublisher to inject context into EventEnvelope.
/// </summary>
public class AgentContextPropagator
{
    private readonly IAgentContextAccessor _contextAccessor;

    public AgentContextPropagator(IAgentContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
    }

    /// <summary>
    /// Inject current context into EventEnvelope before publishing.
    /// </summary>
    public void InjectContext(EventEnvelope envelope)
    {
        var context = _contextAccessor.Context;
        if (context == null) return;

        var metadata = AgentContextSerializer.Serialize(context);
        foreach (var (key, value) in metadata)
        {
            // NOTE: this assumes the proto update added `context_metadata`
            // and the generated C# property is available (typically `ContextMetadata`).
            envelope.ContextMetadata[key] = value;
        }
    }

    /// <summary>
    /// Extract context from EventEnvelope and restore to current scope.
    /// </summary>
    public void ExtractContext(EventEnvelope envelope)
    {
        // NOTE: this assumes the proto update added `context_metadata`
        // and the generated C# property is available (typically `ContextMetadata`).
        if (envelope.ContextMetadata.Count == 0) return;

        var context = _contextAccessor.Context ?? new AsyncLocalAgentContext();
        AgentContextSerializer.Deserialize(envelope.ContextMetadata, context);
        _contextAccessor.Context = context;
    }
}
```

**Important notes (review): Context scoping is required**

`ExtractContext()` must not permanently overwrite ambient context. Define a scope mechanism:

- Save previous context
- Set extracted context for the duration of handling
- Restore previous context in `finally`

Without scoping, concurrent/overlapped event handling can leak context between events.

### 3. Runtime Implementations

#### 3.1 Orleans Runtime Bridge

```csharp
namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans-specific context accessor that bridges with Orleans.Runtime.RequestContext.
/// </summary>
public class OrleansAgentContextAccessor : IAgentContextAccessor
{
    private readonly AsyncLocalAgentContextAccessor _fallback = new();

    public IAgentContext? Context
    {
        get => new OrleansAgentContextBridge();
        set
        {
            // Sync to Orleans RequestContext
            if (value != null)
            {
                foreach (var (key, val) in value.GetAll())
                {
                    Orleans.Runtime.RequestContext.Set(key, val);
                }
            }
        }
    }
}

/// <summary>
/// Bridge implementation that reads/writes Orleans RequestContext.
/// </summary>
public class OrleansAgentContextBridge : IAgentContext
{
    public T? Get<T>(AgentContextKey<T> key)
    {
        var value = Orleans.Runtime.RequestContext.Get(key.Name);
        return value is T typedValue ? typedValue : key.DefaultValue;
    }

    public object? Get(string key)
    {
        return Orleans.Runtime.RequestContext.Get(key);
    }

    public void Set<T>(AgentContextKey<T> key, T value)
    {
        Orleans.Runtime.RequestContext.Set(key.Name, value);
    }

    public void Set(string key, object? value)
    {
        Orleans.Runtime.RequestContext.Set(key, value);
    }

    public void Remove(string key)
    {
        Orleans.Runtime.RequestContext.Set(key, null);
    }

    public void Clear()
    {
        // Orleans RequestContext doesn't have Clear, 
        // track keys separately if needed
    }

    public IReadOnlyDictionary<string, object?> GetAll()
    {
        // Orleans RequestContext doesn't expose enumeration
        // Return empty or track separately
        return new Dictionary<string, object?>();
    }

    public void Import(IReadOnlyDictionary<string, object?> entries)
    {
        foreach (var (key, value) in entries)
        {
            Orleans.Runtime.RequestContext.Set(key, value);
        }
    }
}
```

**Review warning (Orleans bridge):**

This bridge, as written, is **not sufficient for automatic propagation** because it cannot enumerate keys.

Recommended approaches:

- **Approach A (recommended, simplest):** do not rely on enumerating Orleans `RequestContext`. Instead, build `EventEnvelope` context from an allowlisted set of well-known keys at the framework entry/publish point (controllers, gateways, publisher pipeline).
- **Approach B:** maintain a key registry (the list of keys written via this abstraction) so `GetAll()` can return those keys by reading each key from Orleans `RequestContext`.

Do not proceed with implementation until one of these is chosen.

#### 3.2 Local Runtime

```csharp
namespace Aevatar.Agents.Runtime.Local;

// Local runtime uses AsyncLocalAgentContextAccessor directly
// No bridge needed - register in DI:
// services.AddSingleton<IAgentContextAccessor, AsyncLocalAgentContextAccessor>();
```

#### 3.3 ProtoActor Runtime (Future Work)

ProtoActor runtime bridge is planned but not yet implemented. The approach will be:

- Bridge with Proto.Actor message headers for context propagation
- Use `Proto.Context.MessageHeaders` to store/retrieve context values
- Similar pattern to Orleans bridge but with ProtoActor-specific APIs

For MVP, ProtoActor runtime falls back to `AsyncLocalAgentContextAccessor` (same as Local).

### 4. Integration Points

#### 4.1 GAgentBase Integration

```csharp
// In GAgentBase.cs, add context accessor property

public abstract class GAgentBase : IGAgent
{
    /// <summary>
    /// Agent context accessor for request-scoped data.
    /// </summary>
    protected IAgentContextAccessor? ContextAccessor { get; set; }

    /// <summary>
    /// Convenience property to get current context.
    /// </summary>
    protected IAgentContext? Context => ContextAccessor?.Context;

    // In HandleEventAsync, extract context from envelope:
    public async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        // Extract context from envelope metadata
        if (ContextAccessor != null && envelope.ContextMetadata.Count > 0)
        {
            var context = new AsyncLocalAgentContext();
            AgentContextSerializer.Deserialize(envelope.ContextMetadata, context);
            ContextAccessor.Context = context;
        }

        // ... existing event handling logic ...
    }
}
```

#### 4.2 GAgentActorBase Integration

```csharp
// In GAgentActorBase, inject context when publishing events

public abstract class GAgentActorBase : IGAgentActor
{
    private readonly AgentContextPropagator _contextPropagator;

    // In PublishEventAsync, inject context:
    public async Task<string> PublishEventAsync<TEvent>(
        TEvent evt,
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default,
        bool isInternalCall = false) where TEvent : IMessage
    {
        var envelope = CreateEnvelope(evt, direction);
        
        // Inject current context into envelope
        _contextPropagator.InjectContext(envelope);

        // ... publish envelope ...
    }
}
```

### 5. Usage Examples

#### 5.1 In Controller (Setting Context)

```csharp
[ApiController]
public class GodGPTController : ControllerBase
{
    private readonly IAgentContextAccessor _contextAccessor;

    [HttpPost("chat")]
    public async Task<IActionResult> ChatAsync(ChatRequest request)
    {
        // Get or create context
        var context = _contextAccessor.GetOrCreate();
        
        // Set context values (type-safe)
        context.Set(AgentContextKeys.IsCN, await CheckIsCN());
        context.Set(AgentContextKeys.Language, request.Language);
        context.Set(AgentContextKeys.UserId, CurrentUser.Id.ToString());
        context.Set(AgentContextKeys.CorrelationId, Activity.Current?.Id ?? Guid.NewGuid().ToString());

        // Call agent - context automatically propagates
        var response = await _chatAgent.ProcessAsync(request.Message);
        return Ok(response);
    }
}
```

#### 5.2 In Agent (Reading Context)

```csharp
public class GodChatGAgent : GAgentBase<GodChatState>
{
    [EventHandler]
    public async Task HandleChatMessage(ChatMessageEvent evt)
    {
        // Read context (type-safe)
        var isCN = Context?.Get(AgentContextKeys.IsCN) ?? false;
        var language = Context?.Get(AgentContextKeys.Language) ?? "en";
        var userId = Context?.Get(AgentContextKeys.UserId);

        // Use context for business logic
        if (isCN)
        {
            // Route to CN-specific processing
        }

        // Context automatically propagates when publishing events
        await PublishAsync(new ChatResponseEvent { ... });
    }
}
```

#### 5.3 Custom Context Keys

```csharp
// Define custom keys for your domain
public static class GodGPTContextKeys
{
    public static readonly AgentContextKey<GodGPTLanguage> Language = 
        new("GodGPTLanguage", GodGPTLanguage.English);

    public static readonly AgentContextKey<string> SessionId = 
        new("SessionId");

    public static readonly AgentContextKey<bool> IsGuest = 
        new("IsGuest", false);

    public static readonly AgentContextKey<int> QuotaRemaining = 
        new("QuotaRemaining", 0);
}

// Usage
context.Set(GodGPTContextKeys.Language, GodGPTLanguage.TraditionalChinese);
var lang = context.Get(GodGPTContextKeys.Language);
```

### 6. DI Registration

```csharp
// In ServiceCollectionExtensions.cs

public static IAevatarBuilder AddAgentContext(this IAevatarBuilder builder)
{
    // Register context accessor based on runtime
    var runtimeType = builder.GetRuntimeType();
    
    switch (runtimeType)
    {
        case AgentRuntimeType.Orleans:
            builder.Services.AddSingleton<IAgentContextAccessor, OrleansAgentContextAccessor>();
            break;
        case AgentRuntimeType.Local:
        case AgentRuntimeType.ProtoActor:
        default:
            builder.Services.AddSingleton<IAgentContextAccessor, AsyncLocalAgentContextAccessor>();
            break;
    }

    builder.Services.AddSingleton<AgentContextPropagator>();
    
    return builder;
}
```

## Migration Guide

### From Orleans RequestContext

```csharp
// Before (Orleans-specific)
RequestContext.Set("IsCN", isCN);
var value = RequestContext.Get("IsCN");

// After (Runtime-agnostic, type-safe)
_contextAccessor.Context?.Set(AgentContextKeys.IsCN, isCN);
var value = _contextAccessor.Context?.Get(AgentContextKeys.IsCN);

// Or with extension method for convenience
AgentContext.Set(AgentContextKeys.IsCN, isCN);
var value = AgentContext.Get(AgentContextKeys.IsCN);
```

## Benefits

1. **Type Safety**: Compile-time type checking prevents runtime errors
2. **Runtime Agnostic**: Same code works across Local, Orleans, ProtoActor
3. **Auto-Propagation**: Context flows through event streams automatically
4. **Testability**: Easy to mock IAgentContextAccessor in tests
5. **Discoverability**: Well-known keys in AgentContextKeys class
6. **Backwards Compatible**: Orleans bridge maintains existing behavior

## Future Considerations

1. **Compression**: Compress large context metadata for performance
2. **Encryption**: Encrypt sensitive context values
3. **TTL**: Add expiration for context values
4. **Tracing Integration**: Deeper integration with OpenTelemetry
5. **Validation**: Add validation rules for context values

## Summary

This design provides a clean, runtime-agnostic mechanism for passing request-scoped context data across agent boundaries in the Aevatar Agent Framework. It maintains type safety, supports automatic propagation through events, and bridges seamlessly with existing Orleans RequestContext usage.

---

## Implementation Notes (MVP Path)

This section defines a minimal, high-confidence implementation sequence that is safe for production traffic.

### MVP scope (recommended)

- Propagate **only allowlisted keys**:
  - `CorrelationId`
  - `Language`
  - `IsCN`
  - `UserId`
  - `TenantId`
- Provide **ContextScope** for safe set/restore during event handling.
- Enforce **limits**:
  - Max keys (e.g., 16)
  - Max total serialized bytes (e.g., 4 KB)
- No sensitive values unless encrypted/signed.

### ContextScope (required)

Define a scope helper to prevent context leakage:

```csharp
/// <summary>
/// Scoped context that restores previous context on dispose.
/// Use this in HandleEventAsync to prevent context leakage between events.
/// </summary>
public readonly struct AgentContextScope : IDisposable
{
    private readonly IAgentContextAccessor _accessor;
    private readonly IAgentContext? _previous;

    public AgentContextScope(IAgentContextAccessor accessor, IAgentContext newContext)
    {
        _accessor = accessor;
        _previous = accessor.Context;
        _accessor.Context = newContext;
    }

    public void Dispose()
    {
        _accessor.Context = _previous;
    }
}

// Usage in HandleEventAsync:
public async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct)
{
    IAgentContext? scopedContext = null;
    if (envelope.ContextMetadata.Count > 0)
    {
        scopedContext = new AsyncLocalAgentContext();
        AgentContextSerializer.Deserialize(envelope.ContextMetadata, scopedContext);
    }
    
    using (scopedContext != null 
        ? new AgentContextScope(ContextAccessor!, scopedContext) 
        : default)
    {
        await InvokeHandlers(envelope, ct);
    }
    // Context automatically restored here
}
```

### Propagation pipeline (recommended)

- **Publish side**: inject allowlisted context into `EventEnvelope` before streaming.
- **Receive side**: extract context from `EventEnvelope` and apply only within a scoped handler execution.

### Orleans compatibility (recommended)

Do not depend on enumerating Orleans `RequestContext`.

- Set well-known values at entry points (HTTP controllers, gateway services).
- At publish time, inject those values into the envelope using the allowlist.

This avoids the key-enumeration limitation while preserving behavior consistency across runtimes.

---

## Performance & Safety Checklist

- **No full-context serialization on every publish** (allowlist only).
- **Prefer typed Protobuf values** over string-prefix encoding for hot-path keys.
- **Avoid JSON fallback** unless explicitly required and infrequent.
- **Scope context** for every event handling call.
- **Bound size** and **sanitize** keys/values to prevent abuse (DoS via metadata bloat).
- **Never store secrets** in context metadata (or encrypt/sign if unavoidable).

