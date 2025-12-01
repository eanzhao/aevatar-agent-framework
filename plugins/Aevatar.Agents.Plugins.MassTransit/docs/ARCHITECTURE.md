# MassTransit Plugin Architecture

## Overview

This plugin provides MassTransit-based message stream implementation for the Aevatar Agent Framework.
It enables agents to communicate through RabbitMQ, Azure Service Bus, or other MassTransit-supported transports.

## Category → Exchange/Topic Mapping

### Design Philosophy

The `category` parameter in `IMessageStreamProvider.GetStream(agentId, category)` maps to MassTransit exchanges/topics:

```
Category (logical)  →  Exchange/Topic (physical)
────────────────────────────────────────────────
null / empty        →  "aevatar-agent-{agentId}"
"events"            →  "aevatar-events-{agentId}"
"commands"          →  "aevatar-commands-{agentId}"
"custom-name"       →  "aevatar-custom-name-{agentId}"
```

### Mapping Rules

1. **Default (no category)**: Uses `aevatar-agent-{agentId}` exchange
2. **With category**: Uses `aevatar-{category}-{agentId}` exchange
3. **Exchange type**: Fanout (all subscribers receive all messages)

### Configuration

```csharp
services.AddMassTransitMessageStreams(options =>
{
    options.ExchangePrefix = "aevatar";  // Default prefix
    options.ExchangeType = "fanout";     // Fanout for broadcast
    options.DurableExchanges = true;     // Persist exchanges
    options.AutoDeleteExchanges = false; // Keep exchanges after disconnect
});
```

## Stream Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    MassTransit Plugin                        │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  IMessageStreamProvider                                      │
│       │                                                     │
│       ├── GetStream(agentId)                                │
│       │       │                                             │
│       │       └── MassTransitMessageStream                  │
│       │               │                                     │
│       │               ├── PublishAsync()  → Exchange        │
│       │               └── SubscribeAsync() ← Queue          │
│       │                                                     │
│       └── GetStream(agentId, category)                      │
│               │                                             │
│               └── MassTransitMessageStream(category)        │
│                       │                                     │
│                       └── Publishes to: {prefix}-{category}-{id}
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

## Message Flow

### Publishing

```
Agent.PublishAsync(event)
    ↓
MassTransitMessageStream.PublishAsync()
    ↓
IBus.Publish<ByteArrayMessage>()
    ↓
Exchange: aevatar-{category}-{agentId}
    ↓
All subscribed queues receive message
```

### Subscribing

```
Exchange: aevatar-{category}-{parentId}
    ↓
Queue: child-subscription-{childId}
    ↓
StreamMessageDispatcher.Consume()
    ↓
MassTransitMessageStream.OnMessageReceived()
    ↓
EventHandler(EventEnvelope)
```

## Usage Example

```csharp
// In Startup.cs
services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
        cfg.ConfigureEndpoints(context);
    });
});

services.AddMassTransitMessageStreams();

// In Agent code
var stream = StreamProvider.GetStream(parentId, "events");
await stream.SubscribeAsync<EventEnvelope>(OnEventReceived);
```

## Transport Support

| Transport | Status | Notes |
|-----------|--------|-------|
| RabbitMQ | ✅ Tested | Recommended for production |
| Azure Service Bus | ✅ Supported | Use topics instead of exchanges |
| Amazon SQS | ⚠️ Limited | SNS required for fanout |
| In-Memory | ✅ Tested | For testing only |

## Best Practices

1. **Use categories for logical separation**: Group related events by category
2. **Keep category names short**: They become part of exchange names
3. **Avoid special characters**: Use alphanumeric and hyphens only
4. **Configure durability**: Enable durable exchanges for production
5. **Monitor queue depth**: Set up alerts for queue backlog

---

*Last updated: 2025-11-27*

