# Aevatar.Agents.CreativeReasoning

Universe of Thoughts (UoT) implementation for creative reasoning with LLMs.

Based on the research paper: [Universe of Thoughts: Enabling Creative Reasoning with Large Language Models](https://arxiv.org/html/2511.20471v2)

## Overview

This module implements **C-UoT (Combinational UoT)** - a creative reasoning framework that generates novel solutions by combining thoughts from analogous problems in different domains.

### C-UoT Six-Step Process

1. **Analogical Retrieval & Solution Harvesting**: Find structurally similar problems from diverse domains
2. **Decompose Each Solution into Thoughts**: Break solutions into atomic, recombinable thought units
3. **Choose Host & Substitution Sites**: Select the best base solution and identify modification points
4. **Far-then-Analogical Donor Selection**: Select donor thoughts prioritizing semantic distance for novelty
5. **Substitute to Synthesize New Combinations**: Create novel candidate solutions
6. **Evaluate and Rank**: Three-dimensional evaluation (Feasibility, Utility, Novelty)

### Evaluation Dimensions

| Dimension | Role | Description |
|-----------|------|-------------|
| **Feasibility** | Hard Constraint | Can this be implemented? (Must pass threshold) |
| **Utility** | Soft Metric | How effective is this solution? |
| **Novelty** | Soft Metric | How different from existing solutions? |

## Quick Start

```csharp
// Register services
services.AddUoTCreativeReasoning();

// Execute creative reasoning
var executor = serviceProvider.GetRequiredService<IUoTExecutor>();

var result = await executor.ExecuteAsync(
    problem: "How can a traditional bookstore regain growth in the e-commerce era?",
    options: new UoTOptions
    {
        ProviderName = "openai-gpt4",
        DomainHint = "retail, business strategy",
        MaxAnalogies = 5,
        MaxCandidates = 10,
        FeasibilityThreshold = 0.6f,
        UtilityWeight = 0.5f,
        NoveltyWeight = 0.5f,
        OnProgress = progress => Console.WriteLine($"[{progress.Phase}] {progress.Message}")
    });

if (result.Success)
{
    Console.WriteLine($"Best Solution (Score: {result.BestSolution.Score.Composite:F2}):");
    Console.WriteLine(result.BestSolution.Content);
}
```

## Architecture

```
UoTCoordinatorGAgent
    │
    ├── Step 1: Analogical Retrieval (IAnalogyStrategy)
    │   └── Find problems from different domains with structural similarity
    │
    ├── Step 2: Thought Decomposition (IThoughtDecompositionStrategy)
    │   └── Extract atomic thoughts: CORE, COMPONENT, INTERACTION, CONSTRAINT, OUTPUT
    │
    ├── Step 3: Host Selection (IHostSelectionStrategy)
    │   └── Choose best base solution and substitution sites
    │
    ├── Step 4: Donor Selection (IDonorSelectionStrategy)
    │   └── Far-then-Analogical: prioritize distant donors for novelty
    │
    ├── Step 5: Synthesis (ISynthesisStrategy)
    │   └── Combine host + donor thoughts into new solutions
    │
    └── Step 6: Evaluation (IEvaluationStrategy)
        └── Score: Feasibility (gate) × (Utility + Novelty)
```

## Strategies

All strategies are pluggable. Default implementations use structured JSON prompts for reliable parsing.

### Custom Strategies

```csharp
coordinator.SetStrategies(
    analogyStrategy: new MyCustomAnalogyStrategy(),
    evaluationStrategy: new DomainSpecificEvaluationStrategy()
);
```

## Example Use Cases

### Business Strategy
```csharp
var result = await executor.ExecuteAsync(
    "Design a new revenue model for local newspapers",
    new UoTOptions { ProviderName = "openai-gpt4", DomainHint = "media, subscription" });
```

### Engineering Design
```csharp
var result = await executor.ExecuteAsync(
    "Design a traffic management system for a single-lane bridge",
    new UoTOptions { ProviderName = "openai-gpt4", DomainHint = "transportation, distributed systems" });
```

### Product Innovation
```csharp
var result = await executor.ExecuteAsync(
    "Create a novel fitness app feature for senior users",
    new UoTOptions { ProviderName = "openai-gpt4", DomainHint = "health, gamification" });
```

## Future: E-UoT and T-UoT

This module currently implements **C-UoT**. Future extensions will add:

- **E-UoT (Exploratory)**: Discover new thoughts beyond existing solution space
- **T-UoT (Transformative)**: Break rules and assumptions for radical innovation

## References

- [Universe of Thoughts Paper](https://arxiv.org/html/2511.20471v2)
- [Margaret Boden's Creativity Types](https://en.wikipedia.org/wiki/Computational_creativity)

