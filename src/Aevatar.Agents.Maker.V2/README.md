# MAKER V2 - Zero-Error LLM Task Execution

A clean, minimal implementation of the MAKER (Massively Decomposed Agentic Processes) framework for achieving zero-error execution of complex LLM tasks.

Based on the paper: [Solving a Million-Step LLM Task with Zero Errors](https://arxiv.org/html/2511.09030v1)

## Key Features

- **Simple API**: One method call to execute any task
- **Automatic Voting**: First-to-ahead-by-K consensus with N = 2K - 1
- **Smart Decomposition**: Tasks automatically broken into atomic subtasks
- **Red Flag Recovery**: Automatic error detection and recovery
- **Extensible Strategies**: Customize decomposition/solution/composition per domain
- **Full Trace**: Inspect every voting session and task tree node

## Quick Start

```csharp
// Zero-config usage
var result = await maker.ExecuteAsync("Analyze this research paper's contributions.");

// With reliability level
var result = await maker.ExecuteAsync(
    "Calculate optimal logistics routes.",
    new MakerOptions { Reliability = ReliabilityLevel.High });

// With domain-specific strategy
var result = await maker.ExecuteAsync(
    "为命主分析2024年事业运势",
    new MakerOptions
    {
        Decomposer = new BaziDecomposer(),
        Solver = new BaziSolver()
    });
```

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    IMakerExecutor                           │
│  ┌─────────────────────────────────────────────────────┐   │
│  │                 MakerExecutor                        │   │
│  │  - Orchestrates decomposition/voting/composition     │   │
│  │  - Manages task tree                                 │   │
│  │  - Handles red flags                                 │   │
│  └─────────────────────────────────────────────────────┘   │
│                          │                                  │
│  ┌───────────────┐ ┌─────┴─────┐ ┌───────────────────────┐ │
│  │  VoteEngine   │ │ Strategies │ │   ExecutionPool      │ │
│  │  - First-K    │ │ - Decomp   │ │   - Parallel LLM     │ │
│  │  - Consensus  │ │ - Solve    │ │   - Decorrelation    │ │
│  │  - Clustering │ │ - Compose  │ │   - Multi-provider   │ │
│  └───────────────┘ └───────────┘ └───────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

## Reliability Levels

| Level    | K | N (Samples) | Use Case                      |
|----------|---|-------------|-------------------------------|
| Low      | 1 | 1           | Fast exploration, low stakes  |
| Medium   | 2 | 3           | Most tasks (default)          |
| High     | 3 | 5           | Important tasks               |
| Critical | 5 | 9           | Mission-critical, zero errors |

## Customizing Strategies

### Decomposition Strategy

```csharp
public class MyDecomposer : IDecompositionStrategy
{
    public string BuildDecompositionPrompt(string task, IReadOnlyDictionary<string, string> ctx)
        => $"Break this task into steps: {task}";
    
    public bool IsAtomic(string task, int depth, int maxDepth)
        => depth >= 2;
    
    public IReadOnlyList<(string, string)> ParseDecomposition(string output)
        => new DefaultDecomposer().ParseDecomposition(output);
}
```

### Solution Strategy

```csharp
public class MySolver : ISolutionStrategy
{
    public string BuildSolvePrompt(string task, IReadOnlyDictionary<string, string> ctx)
        => $"Solve: {task}\nContext: {string.Join(", ", ctx.Values)}";
}
```

## DI Registration

### Pool Mode (Default)

```csharp
services.AddMEAI();
services.AddMakerV2(poolSize: 3, temperatureVariance: 0.1f);
```

### Agent Mode (Scalable)

For distributed systems or when you need state persistence:

```csharp
services.AddMEAI();
services.AddMakerV2WithAgents(defaultProviderName: "default");
```

Agent mode uses the Aevatar Agent Framework, enabling:
- Distributed execution across Orleans/ProtoActor runtimes
- State persistence and EventSourcing
- Event-driven progress tracking
- Horizontal scaling

## Result Inspection

```csharp
var result = await maker.ExecuteAsync("...");

Console.WriteLine($"Success: {result.Success}");
Console.WriteLine($"LLM Calls: {result.TotalLLMCalls}");
Console.WriteLine($"Duration: {result.Duration.TotalSeconds}s");

// Inspect task tree
foreach (var session in result.Trace.RootTask.VotingSessions)
{
    Console.WriteLine($"Voting: {session.Winner?.Votes} votes, {session.Rounds} rounds");
}
```

## Comparison with V1

| Aspect              | V1                          | V2                        |
|---------------------|-----------------------------|-----------------------------|
| User code (Bazi)    | 457 lines                   | 60 lines                    |
| User code (Paper)   | 303 lines                   | 50 lines                    |
| Entry point         | Inherit + override 5 methods| Call `ExecuteAsync()`       |
| Actor management    | Manual linking              | Automatic                   |
| Voting              | Embedded in TaskAgent       | Separate VoteEngine         |
| Configuration       | 15 parameters               | 3 core parameters           |
| Paper alignment     | Partial                     | Full (N=2K-1, decorrelation)|

## Theory

The MAKER framework achieves zero-error execution through three pillars:

1. **Maximal Decomposition**: Break tasks into atomic subtasks solvable with high confidence
2. **First-to-ahead-by-K Voting**: Multiple samples per decision, consensus required
3. **Red-Flagging**: Detect and recover from correlated errors

With K=2 (Medium reliability), the probability of error at each step is approximately `p^2` where `p` is the base model's error rate. For a model with 10% error rate, this reduces to 1% per step.

