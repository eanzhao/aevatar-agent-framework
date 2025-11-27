# MAKER V2 - Multi-Agent Knowledge-Enhanced Reasoning System

A production-ready implementation of the MAKER (Multi-Agent Knowledge-Enhanced Reasoning) algorithm on the Aevatar Agent Framework. This system leverages multiple LLM agents with consensus voting to achieve higher accuracy than single-shot inference.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Core Algorithms](#core-algorithms)
- [Configuration Reference](#configuration-reference)
- [Configuration Samples](#configuration-samples)
- [API Reference](#api-reference)
- [Integration Guide](#integration-guide)

---

## Overview

### What is MAKER?

MAKER is a multi-agent reasoning framework that improves LLM accuracy by:

1. **Decomposing** complex tasks into simpler subtasks
2. **Sampling** multiple independent solutions from workers
3. **Voting** to reach consensus on the best answer
4. **Composing** results back into a coherent final output

### Key Benefits

| Benefit | Description |
|---------|-------------|
| **Higher Accuracy** | Consensus voting catches LLM errors |
| **Scalable Complexity** | Recursive decomposition handles arbitrarily complex tasks |
| **Cost Control** | Budget-based limits prevent runaway token usage |
| **Flexibility** | Dual-mode design (Production vs Academic) |
| **Observability** | Real-time progress streaming via SSE |

### When to Use MAKER

✅ **Good for:**
- Tasks where correctness matters more than speed
- Complex multi-step reasoning
- Fact-checking and verification
- Mathematical proofs and calculations
- Critical business decisions

❌ **Not ideal for:**
- Simple single-shot queries
- Real-time chat applications
- Tasks with strict latency requirements
- Creative tasks where diversity is desired

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         MakerCoordinatorGAgent                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                    Recursive Task Execution                      │    │
│  │  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐          │    │
│  │  │   Assess    │ →  │  Decompose  │ →  │   Execute   │          │    │
│  │  │  Atomicity  │    │   or Solve  │    │  Children   │          │    │
│  │  └─────────────┘    └─────────────┘    └─────────────┘          │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                              │                                           │
│                              │ Events (DOWN)                             │
│                              ↓                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                     Worker Pool (N agents)                       │    │
│  │  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐│    │
│  │  │Worker 0 │  │Worker 1 │  │Worker 2 │  │Worker 3 │  │Worker N ││    │
│  │  │ T=0.25  │  │ T=0.35  │  │ T=0.28  │  │ T=0.32  │  │ T=...   ││    │
│  │  └────┬────┘  └────┬────┘  └────┬────┘  └────┬────┘  └────┬────┘│    │
│  │       │            │            │            │            │      │    │
│  │       └────────────┴────────────┴────────────┴────────────┘      │    │
│  │                              │                                    │    │
│  │                              │ ProposalResult (UP)                │    │
│  │                              ↓                                    │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                              │                                           │
│                              ↓                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                      VoteEngine                                  │    │
│  │  ┌─────────────────────────────────────────────────────────┐    │    │
│  │  │  Semantic Clustering (Embedding-based similarity)       │    │    │
│  │  │  First-to-ahead-by-K Consensus Algorithm                │    │    │
│  │  │  Early Termination on Consensus                         │    │    │
│  │  └─────────────────────────────────────────────────────────┘    │    │
│  └─────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────┘
```

### Components

| Component | Role |
|-----------|------|
| **MakerCoordinatorGAgent** | Orchestrates the entire execution flow |
| **MakerWorkerGAgent** | Executes individual LLM calls |
| **VoteEngine** | Implements consensus voting with semantic clustering |
| **IDecompositionStrategy** | Defines how tasks are broken down |
| **ISolutionStrategy** | Defines how atomic tasks are solved |
| **ICompositionStrategy** | Defines how results are combined |

---

## Core Algorithms

### 1. First-to-ahead-by-K Voting

The consensus algorithm used by MAKER:

```
Given: K (consensus threshold), N = 2K-1 (samples per round)

For each proposal received:
  1. Compute semantic embedding
  2. Find most similar existing cluster (cosine similarity ≥ threshold)
  3. If found: add vote to cluster
     Else: create new cluster
  4. Check consensus: leader.votes - runnerUp.votes ≥ K ?
     If yes: CONSENSUS REACHED → early termination
     If no: continue sampling
```

**Example (K=2, N=3):**

| Proposal | Action | Cluster A | Cluster B | Gap | Result |
|----------|--------|-----------|-----------|-----|--------|
| P1: "4" | Create A | 1 | 0 | 1 | Continue |
| P2: "4" | Join A | 2 | 0 | 2 | **CONSENSUS!** |

### 2. Semantic Clustering

Unlike exact-match voting, MAKER uses embedding-based similarity:

```
Proposal A: "Singapore is a city-state in Southeast Asia"
Proposal B: "Singapore is located in Southeast Asia as a city-state"

Cosine Similarity = 0.94 > 0.85 (threshold)
→ Same cluster, merged votes
```

### 3. Streaming Race Pattern

Workers don't wait for all results:

```
T=0ms:    Dispatch 5 workers
T=800ms:  Worker 2 returns → vote → 1 vote for A
T=1200ms: Worker 0 returns → vote → 2 votes for A → CONSENSUS!
T=1201ms: Cancel remaining workers (3, 4, 1)
          → Saved ~2 seconds of waiting
```

### 4. Continuous Sampling

If consensus not reached in batch 1, add more workers:

```
Batch 1: 5 workers → No consensus (A:2, B:2, C:1)
Batch 2: 5 more workers → A:4, B:3, C:3 → No consensus
Batch 3: 5 more workers → A:6, B:4, C:5 → CONSENSUS! (A leads by 2)
```

---

## Configuration Reference

### MakerOptions

The main configuration object for MAKER execution.

#### Reliability Settings

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Reliability` | `ReliabilityLevel` | `Medium` | Preset reliability level |
| `CustomK` | `int?` | `null` | Override K value directly |

**ReliabilityLevel Values:**

| Level | K | N (Samples) | Use Case |
|-------|---|-------------|----------|
| `Low` | 1 | 1 | Fast exploration, okay with errors |
| `Medium` | 2 | 3 | Balanced for most tasks |
| `High` | 3 | 5 | Important tasks |
| `VeryHigh` | 4 | 7 | Critical tasks |
| `Critical` | 5 | 9 | Must be correct |
| `UltraCritical` | 7 | 13 | Extremely critical |
| `Extreme` | 10 | 19 | Maximum reliability |

#### Budget Controls

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `MaxTotalLlmCalls` | `int` | `500` | Maximum LLM API calls |
| `MaxTotalTokens` | `long` | `2,000,000` | Maximum tokens consumed |
| `MaxDuration` | `TimeSpan` | `30 min` | Maximum execution time |
| `DepthWarningThreshold` | `int` | `10` | Warn if depth exceeds this |
| `HardDepthCap` | `int` | `50` | Absolute max depth (safety net) |

#### Execution Mode

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Mode` | `ExecutionMode` | `Production` | Execution strategy |

**ExecutionMode Values:**

| Mode | Behavior | Cost | Accuracy |
|------|----------|------|----------|
| `Production` | Assess atomicity first, decompose only if needed | 💰 Lower | ✓ Good |
| `Academic` | Force decomposition by default (paper's approach) | 💰💰 Higher | ✓✓ Better |

#### Decomposition Settings

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Granularity` | `DecompositionGranularity` | `Balanced` | How many subtasks per decomposition |
| `ContextIsolation` | `ContextIsolationMode` | `Full` | How context is passed to children |

**DecompositionGranularity Values:**

| Value | Steps per Decomposition | Tree Shape | Use Case |
|-------|-------------------------|------------|----------|
| `Balanced` | 3-6 | Wide and shallow | General purpose |
| `Binary` | 2 | Binary tree | Precise control |
| `Single` | 1 | Linear chain | Extreme granularity |

**ContextIsolationMode Values:**

| Value | Behavior | Context Size | Use Case |
|-------|----------|--------------|----------|
| `Full` | Inherit all parent context + all sibling results | 📈 Grows | Coherent writing |
| `Minimal` | Only domain context + previous sibling result | 📊 Controlled | Deep recursion |
| `None` | No inheritance, start fresh | 📉 Minimal | Independent subtasks |

#### Voting Settings

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ClusteringMethod` | `string` | `"semantic"` | `"semantic"` or `"exact"` |
| `SemanticSimilarityThreshold` | `float` | `0.85` | Similarity threshold for clustering |
| `BaseTemperature` | `float` | `0.3` | Base LLM temperature |
| `TemperatureVariance` | `float` | `0.1` | Temperature decorrelation range |
| `UseMultipleProviders` | `bool` | `false` | Use multiple LLM providers (future) |
| `RedFlagThreshold` | `int` | `3` | Max red flags before abort |
| `RedFlagStrategy` | `IRedFlagStrategy?` | `null` | Custom content validation strategy |
| `RedFlagOptions` | `RedFlagOptions` | `(see below)` | Options for default red flag strategy |

#### RedFlagOptions (Content Validation Settings)

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `MaxContentLength` | `int` | `8000` | Maximum allowed content length (increase for novels) |
| `MinContentLength` | `int` | `10` | Minimum required content length |
| `EnableRefusalDetection` | `bool` | `true` | Detect LLM refusals at content start |
| `EnableDegenerationDetection` | `bool` | `true` | Detect repetitive output (generation loop) |
| `EnableLengthValidation` | `bool` | `true` | Validate content length limits |
| `CustomRefusalPrefixes` | `IReadOnlyList<string>?` | `null` | Custom refusal prefixes (e.g., for Chinese) |
| `DegenerationMinLength` | `int` | `3` | Min pattern length for degeneration detection |
| `DegenerationMaxRepetitions` | `int` | `10` | Max repetitions before flagging |

**Available Red Flag Strategies:**

| Strategy | Use Case |
|----------|----------|
| `DefaultEnglishRedFlagStrategy` | English content (default) |
| `ChineseRedFlagStrategy` | Chinese content (中文) |
| `CodeAwareRedFlagStrategy` | Code generation (relaxed refusal detection) |
| `CompositeRedFlagStrategy` | Combine multiple strategies |
| `NoOpRedFlagStrategy` | Disable all checks |

#### Strategy Overrides

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Decomposer` | `IDecompositionStrategy?` | `null` | Custom decomposition strategy |
| `Solver` | `ISolutionStrategy?` | `null` | Custom solution strategy |
| `Composer` | `ICompositionStrategy?` | `null` | Custom composition strategy |
| `Context` | `Dictionary<string, string>?` | `null` | Initial context variables |

#### Timeout Settings

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `StepTimeout` | `TimeSpan` | `60 sec` | Timeout per individual step |

#### Callbacks

| Parameter | Type | Description |
|-----------|------|-------------|
| `OnProgress` | `Action<MakerProgress>?` | Progress callback |

---

## Configuration Samples

### 1. Simple Task (Cost-Optimized)

Best for straightforward tasks where speed matters.

```json
{
    "name": "Quick Summary",
    "task": "Summarize this article in 3 sentences",
    "reliability": "Low",
    "maxTotalLlmCalls": 50,
    "maxTotalTokens": 100000,
    "maxDurationMinutes": 5,
    "executionMode": "Production",
    "granularity": "Balanced",
    "contextIsolation": "Full"
}
```

**Characteristics:**
- K=1, N=1 (single sample, no voting)
- Fast execution
- Lower cost
- Acceptable error rate for non-critical tasks

---

### 2. General Purpose (Balanced)

Default configuration for most tasks.

```json
{
    "name": "Country Introduction",
    "task": "Write a comprehensive introduction to Singapore",
    "reliability": "Medium",
    "maxTotalLlmCalls": 200,
    "maxTotalTokens": 500000,
    "maxDurationMinutes": 15,
    "executionMode": "Production",
    "granularity": "Balanced",
    "contextIsolation": "Full",
    "context": {
        "language": "English",
        "style": "Encyclopedia",
        "length": "1500-2000 words"
    }
}
```

**Characteristics:**
- K=2, N=3 (basic consensus)
- 3-6 subtasks per decomposition
- Full context inheritance for coherence
- Good balance of cost and accuracy

---

### 3. Mathematical Proof (High Precision)

For tasks requiring logical correctness.

```json
{
    "name": "Mathematical Derivation",
    "task": "Prove by induction: 1+2+3+...+n = n(n+1)/2",
    "reliability": "High",
    "maxTotalLlmCalls": 300,
    "maxTotalTokens": 800000,
    "maxDurationMinutes": 20,
    "executionMode": "Academic",
    "granularity": "Binary",
    "contextIsolation": "Minimal",
    "context": {
        "rigor": "Strict mathematical proof",
        "audience": "Undergraduate level"
    }
}
```

**Characteristics:**
- K=3, N=5 (stronger consensus)
- Academic mode: always decompose first
- Binary decomposition for precise control
- Minimal context to prevent confusion

---

### 4. Fact Verification (Critical)

For tasks where accuracy is paramount.

```json
{
    "name": "Historical Timeline",
    "task": "Create an accurate timeline of WWII key events with precise dates",
    "reliability": "Critical",
    "maxTotalLlmCalls": 500,
    "maxTotalTokens": 2000000,
    "maxDurationMinutes": 30,
    "executionMode": "Academic",
    "granularity": "Binary",
    "contextIsolation": "Minimal",
    "context": {
        "accuracy": "Dates must be precise to the day",
        "source": "Mainstream historical consensus"
    }
}
```

**Characteristics:**
- K=5, N=9 (strong consensus required)
- Academic mode for maximum scrutiny
- Higher token budget
- Longer execution time allowed

---

### 5. Logic Puzzle (Step-by-Step)

For tasks requiring careful sequential reasoning.

```json
{
    "name": "Logic Puzzle",
    "task": "Solve: Alice, Bob, Carol live in different houses (red, green, blue). Alice doesn't live in red. Bob doesn't live in green or blue. Who lives where?",
    "reliability": "High",
    "maxTotalLlmCalls": 300,
    "maxTotalTokens": 800000,
    "maxDurationMinutes": 20,
    "executionMode": "Academic",
    "granularity": "Single",
    "contextIsolation": "Minimal",
    "context": {
        "method": "Step-by-step deductive reasoning",
        "verification": "Must verify answer satisfies all constraints"
    }
}
```

**Characteristics:**
- Single-step granularity (chain of thought)
- Each step verified before proceeding
- Minimal context prevents reasoning errors

---

### 6. Creative Writing (Diverse Output)

For tasks where variety is acceptable.

```json
{
    "name": "Sci-Fi Story",
    "task": "Write a 2000-word science fiction story about AI consciousness",
    "reliability": "Low",
    "maxTotalLlmCalls": 300,
    "maxTotalTokens": 1500000,
    "maxDurationMinutes": 25,
    "executionMode": "Production",
    "granularity": "Balanced",
    "contextIsolation": "Full",
    "context": {
        "tone": "Thought-provoking, suspenseful",
        "setting": "Year 2050",
        "style": "Asimov-style hard sci-fi"
    }
}
```

**Characteristics:**
- Low reliability (K=1) since creativity varies
- Full context for narrative coherence
- Production mode to avoid over-decomposition

---

### 7. API Design (Technical)

For structured technical output.

```json
{
    "name": "REST API Design",
    "task": "Design a complete REST API for a task management system",
    "reliability": "High",
    "maxTotalLlmCalls": 400,
    "maxTotalTokens": 1500000,
    "maxDurationMinutes": 25,
    "executionMode": "Production",
    "granularity": "Balanced",
    "contextIsolation": "Full",
    "context": {
        "format": "OpenAPI 3.0 YAML",
        "auth": "JWT Token",
        "versioning": "URL path versioning"
    }
}
```

**Characteristics:**
- High reliability for consistency
- Full context for API coherence
- Balanced decomposition for manageable chunks

---

### 8. Research Paper Summary (Academic Extreme)

For maximum accuracy on complex documents.

```json
{
    "name": "Paper Summary",
    "task": "Summarize this research paper, including methodology, findings, and limitations",
    "reliability": "VeryHigh",
    "maxTotalLlmCalls": 600,
    "maxTotalTokens": 3000000,
    "maxDurationMinutes": 45,
    "executionMode": "Academic",
    "granularity": "Binary",
    "contextIsolation": "Minimal",
    "decomposition": {
        "minDepthForAtomic": 2,
        "atomicKeywords": ["Summarize Section", "Extract", "List"]
    }
}
```

**Characteristics:**
- Very high reliability (K=4, N=7)
- Academic mode forces thorough decomposition
- Custom atomicity detection via keywords

---

### 9. Code Review (Technical Precision)

For analyzing code quality.

```json
{
    "name": "Code Review",
    "task": "Review this Python codebase for bugs, security issues, and best practices",
    "reliability": "High",
    "maxTotalLlmCalls": 400,
    "maxTotalTokens": 2000000,
    "maxDurationMinutes": 30,
    "executionMode": "Production",
    "granularity": "Balanced",
    "contextIsolation": "Minimal",
    "context": {
        "language": "Python 3.11",
        "focus": "Security, Performance, Maintainability",
        "output": "Markdown report with severity levels"
    }
}
```

---

### 10. Extreme Complexity (Million-Step Task)

For research or extremely complex problems.

```json
{
    "name": "Complex Analysis",
    "task": "Solve this complex multi-part problem requiring extensive reasoning",
    "reliability": "Extreme",
    "maxTotalLlmCalls": 2000,
    "maxTotalTokens": 10000000,
    "maxDurationMinutes": 120,
    "executionMode": "Academic",
    "granularity": "Single",
    "contextIsolation": "None",
    "depthWarningThreshold": 30,
    "hardDepthCap": 100
}
```

**Characteristics:**
- Extreme reliability (K=10, N=19)
- Single-step granularity (linear chain)
- No context inheritance (each step independent)
- Extended limits for long-running analysis

---

## Voting Parameter Cheat Sheet

| Task Type | K | Granularity | Context | Mode |
|-----------|---|-------------|---------|------|
| Quick query | 1 | Balanced | Full | Production |
| General writing | 2 | Balanced | Full | Production |
| Technical docs | 2-3 | Balanced | Full | Production |
| Math/Logic | 3-5 | Binary/Single | Minimal | Academic |
| Fact-checking | 4-5 | Binary | Minimal | Academic |
| Research | 4-7 | Binary | Minimal | Academic |
| Critical decisions | 5-10 | Single | None | Academic |

---

## API Reference

### IMakerExecutor

```csharp
public interface IMakerExecutor
{
    Task<MakerResult> ExecuteAsync(
        string task,
        MakerOptions? options = null,
        CancellationToken ct = default);
}
```

### MakerResult

```csharp
public record MakerResult
{
    public bool Success { get; init; }
    public string? Content { get; init; }
    public string? Error { get; init; }
    public MakerTrace? Trace { get; init; }
}

public record MakerTrace
{
    public TaskNode? RootTask { get; init; }
    public int TotalLLMCalls { get; init; }
    public long TotalTokens { get; init; }
    public long PromptTokens { get; init; }
    public long CompletionTokens { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<RedFlagEvent>? RedFlags { get; init; }
}
```

### MakerProgress

```csharp
public record MakerProgress
{
    public MakerPhase Phase { get; init; }
    public string? TaskId { get; init; }
    public string? Message { get; init; }
    public int Depth { get; init; }
    public DateTime Timestamp { get; init; }
    public VotingProgress? Voting { get; init; }
    public LLMProposal? Proposal { get; init; }
    public string? RedFlagReason { get; init; }
}
```

---

## Integration Guide

### Basic Usage

```csharp
// Register services
services.AddMakerV2();

// Inject and use
public class MyService
{
    private readonly IMakerExecutor _maker;
    
    public MyService(IMakerExecutor maker)
    {
        _maker = maker;
    }
    
    public async Task<string> AnalyzeAsync(string input)
    {
        var result = await _maker.ExecuteAsync(
            $"Analyze the following: {input}",
            new MakerOptions
            {
                Reliability = ReliabilityLevel.High,
                OnProgress = p => Console.WriteLine($"[{p.Phase}] {p.Message}")
            });
        
        return result.Success 
            ? result.Content! 
            : throw new Exception(result.Error);
    }
}
```

### With Custom Strategies

```csharp
var options = new MakerOptions
{
    Decomposer = new MyCustomDecomposer(),
    Solver = new MyCustomSolver(),
    Composer = new MyCustomComposer(),
    Context = new Dictionary<string, string>
    {
        ["domain"] = "finance",
        ["language"] = "en"
    }
};
```

### From JSON Configuration

```csharp
var config = ProjectConfig.FromJson(jsonString);
var options = config.BuildOptions(progress => 
    Console.WriteLine($"[{progress.Phase}] {progress.Message}"));

var result = await maker.ExecuteAsync(config.Task, options);
```

---

## Red Flag System

MAKER includes a pluggable quality control system (`IRedFlagStrategy`) that rejects problematic LLM outputs before they enter the voting pool.

### Default Checks (DefaultEnglishRedFlagStrategy)

| Red Flag | Trigger | Action |
|----------|---------|--------|
| Content Too Long | > MaxContentLength (8000) | Reject from voting |
| Content Too Short | < MinContentLength (10) | Reject from voting |
| LLM Refusal | Starts with "I cannot", "I'm sorry", etc. | Reject from voting |
| Excessive Repetition | Same pattern repeated many times | Reject from voting |
| Decomposition Failed | No valid steps parsed | Fallback to direct solve |
| Consensus Failed | No consensus after max samples | Use best candidate |

### Custom Red Flag Strategies

```csharp
// For Chinese content
var options = new MakerOptions
{
    RedFlagStrategy = new ChineseRedFlagStrategy(new RedFlagOptions
    {
        MaxContentLength = 10000
    })
};

// For code generation (relaxed)
var options = new MakerOptions
{
    RedFlagStrategy = new CodeAwareRedFlagStrategy()
};

// Disable all checks
var options = new MakerOptions
{
    RedFlagStrategy = NoOpRedFlagStrategy.Instance
};

// Custom strategy
public class MyDomainRedFlagStrategy : IRedFlagStrategy
{
    public bool Validate(string content, string proposalId, out string? reason)
    {
        reason = null;
        
        // Your domain-specific validation logic
        if (content.Contains("ILLEGAL_MOVE"))
        {
            reason = "Invalid game move detected";
            return false;
        }
        
        return true;
    }
}
```

---

## Performance Tuning

### Reducing Cost

1. Lower `Reliability` level
2. Use `Production` mode
3. Reduce `MaxTotalLlmCalls`
4. Use `Balanced` granularity

### Increasing Accuracy

1. Higher `Reliability` level
2. Use `Academic` mode
3. Use `Binary` or `Single` granularity
4. Increase token budgets

### Handling Deep Recursion

1. Use `Minimal` or `None` context isolation
2. Set appropriate `DepthWarningThreshold`
3. Ensure `HardDepthCap` is reasonable

---

## Troubleshooting

### "No consensus reached"

- Increase `Reliability` level
- Check if task is too ambiguous
- Lower `SemanticSimilarityThreshold` for subjective tasks

### "Budget exceeded"

- Increase `MaxTotalLlmCalls` or `MaxTotalTokens`
- Use `Production` mode
- Use `Balanced` granularity

### "Infinite decomposition loop"

- Check custom `IsAtomic` implementation
- Ensure `HardDepthCap` is set
- Use `Single` granularity for problem tasks

### "Context too large"

- Use `Minimal` or `None` context isolation
- Reduce initial context size

---

## License

MIT License - See LICENSE file for details.

---

## References

- MAKER Paper: [Multi-Agent Knowledge-Enhanced Reasoning](https://arxiv.org/abs/...)
- Aevatar Agent Framework: [Documentation](https://github.com/...)
