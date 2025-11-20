# Aevatar.Agents.Translation

## 1. Overview
A dedicated AI Agent for translating text files (`.txt`, `.md`, etc.). It reads files from the local filesystem, translates them using an LLM, and saves the result with a language suffix (e.g., `file_zh.txt`).

## 2. Architecture
- **Base Class**: `AIGAgentBase<TranslationState, TranslationConfig>`
- **State Type**: `TranslationState` (Protobuf) - Tracks processed file count and characters.
- **Config Type**: `TranslationConfig` (Protobuf) - Sets default target language and overwrite policy.

## 3. Event Handlers
| Event Type | Action | State Changes |
|------------|--------|---------------|
| `TranslateFileEvent` | Reads file, calls LLM, writes output | `State.FilesProcessedCount++`, `State.TotalCharsTranslated` |

## 4. Usage

### A. Simple Test (Logic Only)
Use `IGAgentFactory` to create the agent.

```csharp
// Setup dependencies
var services = new ServiceCollection();
services.AddLogging(l => l.AddConsole());
services.AddAevatarLocalRuntime();
// ... Add LLM Provider config ...
var sp = services.BuildServiceProvider();

// Create Agent
var factory = sp.GetRequiredService<IGAgentFactory>();
var agent = factory.CreateGAgent<TranslationAgent>();

// Run
await agent.HandleEventAsync(new EventEnvelope 
{ 
    Payload = Any.Pack(new TranslateFileEvent 
    { 
        FilePath = "/path/to/doc.md", 
        TargetLanguage = "zh" 
    }) 
});
```

### B. Full Runtime (Streaming & Collaboration)
**Required for**: Distributed scenarios.

```csharp
using Aevatar.Agents.Runtime.Local.Extensions;
using Aevatar.Agents.Translation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

// 1. Configure Runtime
builder.Services.AddAevatarLocalRuntime();

// 2. Add LLM Provider (Required for AIGAgent)
// builder.Services.AddSingleton<ILLMProvider>(...);

var app = builder.Build();
await app.StartAsync();

// 3. Get Factory & Create Actor
var factory = app.Services.GetRequiredService<IGAgentActorFactory>();
var actor = await factory.CreateGAgentActorAsync<TranslationAgent>(Guid.NewGuid());

// 4. Publish Translation Task
await actor.PublishEventAsync(new TranslateFileEvent 
{ 
    FilePath = "./readme.md", 
    TargetLanguage = "ja" 
});

await app.StopAsync();
```

## 5. Configuration
Ensure you provide a valid `ILLMProvider` in your DI container, as this agent relies on `ChatAsync` to perform translations.

