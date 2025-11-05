# Stream and Event Propagation - Design and Implementation

## 🌊 Event Propagation Model

### Core Principles

1. **Publishers Always Process Their Own Events**
   - When an agent publishes an event, it first processes it itself
   - This follows the publish-subscribe semantic model

2. **Direction-Based Routing**
   - **UP**: Event goes to parent's stream → broadcasts to all siblings
   - **DOWN**: Event goes to own stream → broadcasts to all children  
   - **BOTH**: Event goes both UP and DOWN simultaneously

3. **Subscription-Based Reception**
   - Child agents subscribe to parent's stream
   - Parent agents have references to children's streams
   - Events flow through streams, not direct calls

## 🔴 Critical Design Rule: Preventing Infinite Loops

### The Problem

When a child receives a `BOTH` direction event from its parent's stream, it must NOT propagate it back UP to avoid infinite loops.

### The Solution

Events received from parent streams follow these rules:

```csharp
// From parent stream:
if (envelope.Direction == EventDirection.Down)
{
    // DOWN: Continue propagating down to children
    await ContinuePropagationAsync(envelope);
}
else if (envelope.Direction == EventDirection.Both)  
{
    // BOTH: Convert to DOWN-only to prevent loops
    var downOnlyEnvelope = envelope.Clone();
    downOnlyEnvelope.Direction = EventDirection.Down;
    await ContinuePropagationAsync(downOnlyEnvelope);
}
// UP: Do not propagate (already broadcast in parent stream)
```

## 📊 Event Flow Diagrams

### UP Direction
```
Child publishes UP
    ↓
Parent's Stream
    ↓ (broadcast)
All Siblings receive
```

### DOWN Direction  
```
Parent publishes DOWN
    ↓
Parent's Own Stream
    ↓ (broadcast)
All Children receive
    ↓ (continue down)
Grandchildren receive
```

### BOTH Direction (Fixed)
```
Agent publishes BOTH
    ├── UP to Parent's Stream
    │   └── Siblings receive (no further UP)
    └── DOWN to Own Stream
        └── Children receive
            └── Convert to DOWN-only
                └── Grandchildren receive
```

## 🛡️ Loop Prevention Mechanisms

### 1. Publishers List
Each EventEnvelope maintains a list of agent IDs that have already processed it:
- Before sending, check if target is already in Publishers list
- Add self to Publishers list when forwarding

### 2. Hop Count Limits
- `CurrentHopCount`: Incremented with each hop
- `MaxHopCount`: Optional limit to prevent runaway propagation
- Safety limit: 100 hops (hard-coded failsafe)

### 3. Direction Conversion
- BOTH events from parent streams become DOWN-only
- Prevents bidirectional loops in hierarchical structures

## 🧪 Test Coverage

The following tests verify correct propagation:

1. **SetParent_Should_Subscribe_To_Parent_Stream**
   - Verifies automatic subscription on parent setting

2. **UP_Direction_Should_Broadcast_To_Siblings**
   - Tests sibling notification via parent stream

3. **DOWN_Direction_Should_Broadcast_To_All_Children**
   - Tests parent-to-children broadcast

4. **BOTH_Direction_Should_Broadcast_In_Both_Directions**
   - Tests complex three-layer propagation
   - Previously caused stack overflow (now fixed)

## 🔧 Implementation Status

| Runtime | Propagation Logic | Loop Prevention | Status |
|---------|------------------|-----------------|--------|
| Local | ✅ Fixed | ✅ Implemented | Complete |
| Orleans | ✅ Correct | ✅ Implemented | Complete |
| ProtoActor | ✅ Correct | ✅ Implemented | Complete |

## 📝 Best Practices

1. **Always Clone Envelopes**
   - Before modifying direction or hop count
   - Preserves immutability

2. **Log Propagation Decisions**
   - Use structured logging with event ID
   - Include direction changes

3. **Test Multi-Layer Hierarchies**
   - Minimum 3 layers for BOTH direction
   - Verify no stack overflows

4. **Handle Self-Events Carefully**
   - Use `AllowSelfHandling` attribute sparingly
   - Can contribute to loops if not careful

## 🚨 Known Issues (Fixed)

### Stack Overflow in BOTH Direction (RESOLVED)

**Issue**: Child agents receiving BOTH events from parent streams would propagate back UP, causing infinite loops.

**Fix Applied**: LocalGAgentActor now converts BOTH to DOWN-only when received from parent stream.

**Verification**: All StreamMechanismTests pass without stack overflow.

---

*Last Updated: 2025-01-05*
*Status: Production Ready*