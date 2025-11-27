# MAKER Projects Demo

A clean, minimal demo showcasing MAKER's capabilities.

## Running

```bash
cd examples/MakerSystem
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
│  (from Aevatar.Agents.Maker)                     │
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

All framework complexity (voting, error correction, recursion) is handled by MAKER.

