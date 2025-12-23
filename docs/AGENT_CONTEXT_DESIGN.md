# Agent Context Design Document

## Overview

Runtime-agnostic request context mechanism for passing contextual data (language, region, user metadata) across agent boundaries without modifying method signatures.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Layer                         │
│   (Controllers - Set context before agent calls)             │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│              Aevatar.Agents.Abstractions                     │
│   - IAgentContext / IAgentContextAccessor                    │
│   - AgentContextKey<T> (type-safe key)                       │
│   - AgentContextPropagationOptions                           │
│   - EventEnvelope.ContextMetadata (proto field)              │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                 Aevatar.Agents.Core                          │
│   - AsyncLocalAgentContext (ConcurrentDictionary)            │
│   - AgentContextPropagator (inject/extract)                  │
│   - AgentContextSerializer (type-prefixed encoding)          │
│   - AgentContextScope (scoped restore)                       │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│              Runtime Implementations                         │
│   Local:   AsyncLocalAgentContext directly                   │
│   Orleans: OrleansAgentContextBridge → RequestContext        │
│   Proto:   Falls back to AsyncLocal (future: message headers)│
└─────────────────────────────────────────────────────────────┘
```

## Core API

### Type-Safe Keys

```csharp
// Framework-provided keys
AgentContextKeys.CorrelationId  // AgentContextKey<string>
AgentContextKeys.UserId         // AgentContextKey<string>
AgentContextKeys.Language       // AgentContextKey<string>, default: "en"
AgentContextKeys.IsCN           // AgentContextKey<bool>, default: false

// Custom keys
public static class MyContextKeys
{
    public static readonly AgentContextKey<string> OrderId = new("OrderId");
    public static readonly AgentContextKey<decimal> Amount = new("Amount");
}
```

### Context Access

```csharp
// In Controller - Set context
var context = _contextAccessor.GetOrCreate();
context.Set(AgentContextKeys.CorrelationId, Guid.NewGuid().ToString());
context.Set(AgentContextKeys.Language, "zh-CN");
context.Set(MyContextKeys.OrderId, "ORD-12345");

// In Agent - Read context
[EventHandler]
public async Task Handle(MyEvent evt)
{
    var correlationId = Context?.Get(AgentContextKeys.CorrelationId);
    var language = Context?.Get(AgentContextKeys.Language) ?? "en";
    var orderId = Context?.Get(MyContextKeys.OrderId);
    
    // Context auto-propagates when publishing events
    await PublishAsync(new ResponseEvent { ... });
}
```

## Propagation Options

```csharp
public class AgentContextPropagationOptions
{
    public int MaxKeys { get; set; } = 32;
    public int MaxTotalBytes { get; set; } = 8192;
    public HashSet<string>? AllowedKeys { get; set; }  // null = allow all
    public HashSet<string>? DeniedKeys { get; set; }   // takes precedence

    // Fluent API
    public AgentContextPropagationOptions Allow(params string[] keys);
    public AgentContextPropagationOptions Deny(params string[] keys);
}
```

## DI Registration

```csharp
// Local Runtime - default (propagate all keys)
services.AddAevatarAgentSystem(builder =>
{
    builder.UseLocalRuntime();
    builder.AddAgentContext();
});

// Local Runtime - with allowlist
services.AddAevatarAgentSystem(builder =>
{
    builder.UseLocalRuntime();
    builder.AddAgentContext(options => options
        .Allow("CorrelationId", "UserId", "Language"));
});

// Local Runtime - with denylist
services.AddAevatarAgentSystem(builder =>
{
    builder.UseLocalRuntime();
    builder.AddAgentContext(options => options
        .Deny("Password", "SecretToken"));
});

// Orleans Runtime
services.AddAevatarAgentSystem(builder =>
{
    builder.UseOrleansRuntime();
    builder.AddOrleansAgentContext();
});

// Orleans Runtime - with options
services.AddAevatarAgentSystem(builder =>
{
    builder.UseOrleansRuntime();
    builder.AddOrleansAgentContext(options => options
        .Allow("CorrelationId", "Language", "IsCN"));
});
```

## How It Works

### Publish Side (GAgentActorBase)

```csharp
public async Task<string> PublishEventAsync<TEvent>(TEvent evt, ...)
{
    var envelope = CreateEnvelope(evt, direction);
    
    // Inject current context into envelope
    ContextPropagator?.InjectContext(envelope);
    
    await PublishToStream(envelope);
}
```

### Receive Side (GAgentBase)

```csharp
protected virtual async Task HandleEventCoreAsync(EventEnvelope envelope, ...)
{
    // Create scoped context (auto-restores previous on dispose)
    using var contextScope = ContextAccessor?.CreateScope(envelope) 
        ?? AgentContextScope.Empty;

    // Context property available during handler execution
    await InvokeHandlers(envelope, ct);
}
```

### Serialization Format

Type-prefixed string encoding in `EventEnvelope.ContextMetadata`:

| Type | Format | Example |
|------|--------|---------|
| string | `s:value` | `s:hello` |
| bool | `b:True/False` | `b:True` |
| int | `i:123` | `i:42` |
| long | `l:123` | `l:9223372036854775807` |
| double | `d:3.14` | `d:3.14159` |
| DateTime | `dt:ISO8601` | `dt:2024-12-23T10:30:00Z` |
| Guid | `g:guid` | `g:550e8400-e29b-41d4-a716-446655440000` |

## Migration from Orleans RequestContext

```csharp
// Before (Orleans-specific)
RequestContext.Set("IsCN", isCN);
var value = RequestContext.Get("IsCN");

// After (Runtime-agnostic, type-safe)
context.Set(AgentContextKeys.IsCN, isCN);
var value = context.Get(AgentContextKeys.IsCN);
```

## Key Features

- ✅ **Runtime Agnostic** - Works across Local, Orleans, ProtoActor
- ✅ **Type Safe** - `AgentContextKey<T>` with compile-time checking
- ✅ **Auto Propagation** - Context flows through `EventEnvelope.ContextMetadata`
- ✅ **Configurable** - Allow/Deny lists, size limits
- ✅ **Scoped** - `AgentContextScope` prevents context leakage
- ✅ **High Performance** - `ConcurrentDictionary`, `Span<char>` parsing

---

*Last updated: 2024-12 | .NET 10 | Aevatar Agent Framework*
