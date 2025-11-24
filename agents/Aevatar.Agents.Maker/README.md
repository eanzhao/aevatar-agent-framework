# Aevatar MAKER Agents

MAKER Agents bring the Massively Decomposed Agentic Processes paradigm into the Aevatar ecosystem. The package contains two collaborating agents:

- **`MakerTaskAgent`** – Supervises a single goal, orchestrates voting, recursion, and red-flag detection.
- **`MakerWorkerAgent`** – Produces decompositions or atomic answers by calling the configured LLM provider.

Both agents are implemented with `AIGAgentBase<TState, TConfig>` and follow the full protobuf-first contract.

## 1. Architecture

- **State Types**: `TaskAgentState`, `WorkerAgentState`
- **Config Types**: `TaskAgentConfig`, `WorkerAgentConfig`
- **Events**:
  - `AssignTaskEvent` – Parent/clients assign work to a task agent.
  - `GenerateProposalEvent` – Task agent broadcasts worker requests (DOWN).
  - `ProposalReceivedEvent` – Worker responses bubble up (UP) for voting.
  - `TaskOutcomeEvent` – Child task results propagate upwards.
  - `RedFlagRaisedEvent` – Signals consensus failure or unexpected loops.

The agents rely on the existing stream hierarchy. Typical topology:

```
MakerTaskAgent (parent)
 ├─ MakerWorkerAgent #1
 ├─ MakerWorkerAgent #2
 └─ MakerWorkerAgent #N
```

Recursion is achieved when a `MakerTaskAgent` publishes `AssignTaskEvent` messages Downstream while other `MakerTaskAgent` instances run as children.

## 2. Implementation Highlights

- **First-to-ahead-by-K voting** with canonicalization and SHA-256 hashing ensures deterministic proposals.
- **Fan-out configurable** via `TaskAgentConfig.InitialFanOut` and `ConsensusThresholdK`.
- **Red-flag escalation** after `MaxAttempts` failed voting rounds.
- **Plan parsing** converts JSON arrays into child assignments automatically.
- **Worker prompts** switch between `decomposer` and `solver` roles depending on depth.
- **All serialization** handled via `maker_messages.proto`, preserving cross-runtime portability.

## 3. Usage

### 3.1 Add to Solution

Include `agents/Aevatar.Agents.Maker/Aevatar.Agents.Maker.csproj` in your solution (already wired inside `aevatar-agent-framework.slnx`).

### 3.2 Register Runtime & Factory

```csharp
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddAevatarAgentSystem();
services.AddAevatarLocalRuntime(); // or ProtoActor/Orleans

var provider = services.BuildServiceProvider();
var actorFactory = provider.GetRequiredService<IGAgentActorFactory>();
```

### 3.3 Create Agents

```csharp
var taskId = Guid.NewGuid();
var worker1Id = Guid.NewGuid();
var worker2Id = Guid.NewGuid();

var taskActor = await actorFactory.CreateGAgentActorAsync<MakerTaskAgent>(taskId);
var worker1 = await actorFactory.CreateGAgentActorAsync<MakerWorkerAgent>(worker1Id);
var worker2 = await actorFactory.CreateGAgentActorAsync<MakerWorkerAgent>(worker2Id);

await ActorHierarchyCoordinator.LinkAsync(taskActor, worker1);
await ActorHierarchyCoordinator.LinkAsync(taskActor, worker2);
```

> Tip: import `Aevatar.Agents.Core.Hierarchy` to access `ActorHierarchyCoordinator`.

### 3.4 Kick Off a Task

```csharp
await taskActor.PublishEventAsync(new AssignTaskEvent
{
    TaskId = $"root-{Guid.NewGuid():N}",
    GoalDescription = "Design a launch plan for a new analytics product",
    CurrentDepth = 0
});
```

The task agent will:

1. Broadcast `GenerateProposalEvent` to its workers.
2. Accumulate `ProposalReceivedEvent` instances until the leading option exceeds the runner-up by `K`.
3. Parse decomposition plans into child assignments or publish the atomic answer upward via `TaskOutcomeEvent`.

## 4. Configuration Tips

| Setting | Description | Default |
|---------|-------------|---------|
| `ConsensusThresholdK` | Minimum lead for consensus | 2 |
| `InitialFanOut` | Worker requests per round | 3 |
| `MaxDepth` | Maximum recursion depth | 4 |
| `MaxAttempts` | Voting retries before red flag | 3 |
| `WorkerAgentConfig.DefaultModel` | Model id used by workers | `deepseek-chat` |

Override configuration using protobuf stores or runtime config injections just like any other `AIGAgentBase` derivative.

## 5. Testing Checklist

- [ ] Workers respond to both decomposition and atomic requests.
- [ ] Task agent reaches consensus when proposals match (hash equality).
- [ ] Red flags fire after `MaxAttempts` failures.
- [ ] Child `AssignTaskEvent` messages are emitted when depth allows recursion.
- [ ] `TaskOutcomeEvent` aggregation completes once every child reports back.

These components follow the MAKER Architectural Design document and are ready for integration with Local, Orleans, or ProtoActor runtimes.



