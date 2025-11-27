# MAKER Projects Demo V2

A clean, minimal demo showcasing MAKER V2's capabilities.

## Running

```bash
cd examples/MakerProjectsDemoV2
dotnet run
```

Then open http://localhost:5088 in your browser.

## Configuration

Create `appsettings.secrets.json` with your API keys:

```json
{
  "LLMProviders": {
    "providers": {
      "deepseek": {
        "api_key": "your-deepseek-api-key"
      }
    }
  }
}
```

Or set environment variables:
```bash
export DEEPSEEK_API_KEY=your-key
```

## Projects

### 八字推演 (Bazi Analysis)
Multi-agent Bazi (Chinese Astrology) analysis demonstrating:
- Domain-specific decomposition strategy
- Context propagation between steps
- Structured final report generation

### 论文总结 (Paper Summary)
Multi-agent paper summarization demonstrating:
- Section-based decomposition
- Content injection into prompts
- Markdown report synthesis

## Code Comparison: V1 vs V2

### V1 (Old)
```
BaziMakerAgents.cs      457 lines  (extends MakerTaskAgent, overrides 5 methods)
PaperSummaryAgents.cs   303 lines  (extends MakerTaskAgent, overrides 4 methods)
MakerProjectRunner.cs   222 lines  (manual actor wiring)
MakerProjectsService.cs 106 lines
+ 7 more infrastructure files...
─────────────────────────
Total: ~1400 lines
```

### V2 (New)
```
BaziStrategies.cs        80 lines  (just the strategy interfaces)
PaperStrategies.cs       90 lines  (just the strategy interfaces)
MakerProjectService.cs  150 lines  (simple service, no actor management)
MEAILLMAdapter.cs       100 lines  (LLM bridge)
Program.cs               50 lines  (clean startup)
─────────────────────────
Total: ~470 lines (66% reduction!)
```

## Architecture

```
┌─────────────────────────────────────────────────────┐
│                   Program.cs                         │
│  - Register services                                 │
│  - Map API endpoints                                 │
└───────────────────────────┬─────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────┐
│              MakerProjectService                     │
│  - GetProjects()                                     │
│  - StartRunAsync()  →  IMakerExecutor.ExecuteAsync() │
│  - GetStatus/Snapshot/Timeline()                     │
└───────────────────────────┬─────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────┐
│                IMakerExecutor                        │
│  (from Aevatar.Agents.Maker.V2)                     │
│  - Handles decomposition, voting, composition       │
│  - Uses strategies from Projects/Bazi or Paper      │
└─────────────────────────────────────────────────────┘
```

## Key Insight

The user code is now **purely domain-focused**:
- `BaziDecomposer`: How to break down Bazi analysis
- `BaziSolver`: How to solve atomic Bazi steps
- `PaperDecomposer`: How to break down paper summary
- `PaperSolver`: How to summarize paper sections

All framework complexity (voting, error correction, recursion) is handled by MAKER V2.

