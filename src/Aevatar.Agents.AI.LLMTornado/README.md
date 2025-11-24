# Aevatar.Agents.AI.LLMTornado

**Aevatar Agent Framework** 的 [LlmTornado](https://github.com/lofcz/LlmTornado) 集成扩展包。

本项目为 Aevatar 框架提供了基于 `LlmTornado` 的 LLM 提供商实现 (`IAevatarLLMProvider`)。通过集成 LlmTornado，Aevatar 获得了更广泛的模型支持、更丝滑的本地模型体验以及面向未来的 MCP (Model Context Protocol) 能力。

## 🌪️ 为什么要集成 LlmTornado?

虽然 Aevatar 默认支持微软官方的 `Microsoft.Extensions.AI` (MEAI)，但引入 LlmTornado 能为特定场景带来显著优势：

### 1. 🔌 真正的 Provider Agnostic (供应商无关)
LlmTornado 的核心设计理念是"一套代码，走遍天下"。它屏蔽了不同 LLM 供应商（OpenAI, Anthropic, Google, Cohere, Groq 等）之间的 API 差异。
*   **优势**: 只需要修改配置，即可从 GPT-4 无缝切换到 Claude 3.5 Sonnet 或 Groq，无需修改任何业务代码。
*   **对比 MEAI**: MEAI 也是标准接口，但 LlmTornado 在非 OpenAI 兼容模型的适配上往往更新更及时，封装更统一。

### 2. 🏠 本地模型 (Local LLM) 的一等公民支持
LlmTornado 对 **Ollama** 和 **LocalAI** 提供了深度优化支持。
*   **优势**: 针对本地模型的 Prompt 模板、参数调整进行了专门优化，使得在本地运行 Llama 3、Mistral 等模型时体验更加丝滑，减少了"幻觉"和格式错误。
*   **场景**: 适用于对数据隐私要求极高、需要离线运行或通过本地算力降低成本的 Agent 场景。

### 3. 🔗 开箱即用的 MCP (Model Context Protocol) 支持
LlmTornado 原生支持 **Model Context Protocol (MCP)**。
*   **优势**: MCP 是一个新兴的工具互操作标准。通过 LlmTornado，Aevatar Agent 可以直接连接到支持 MCP 的海量外部工具和数据源（如 GitHub, Slack, Google Drive），瞬间扩展 Agent 的能力边界，而无需手动编写 Tool Wrapper。

### 4. 🖼️ 统一的多模态 (Multi-modal) 体验
提供了一套强类型、统一的 API 来处理图片、音频和视频输入。
*   **优势**: 简化了多模态 Agent 的开发。无论是分析图片还是处理语音，接口都保持一致且易于使用。

## 📦 如何使用

### 1. 安装 NuGet 包
```bash
dotnet add package Aevatar.Agents.AI.LLMTornado
```

### 2. 注册服务
在你的 `Program.cs` 或 `Startup.cs` 中：

```csharp
using Aevatar.Agents.AI.LLMTornado;

// ...

builder.Services.AddAevatarLLMTornado(config =>
{
    // 配置 API Key
    config.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    
    // 选择供应商 (支持 OpenAi, Anthropic, AzureOpenAi, Google, Cohere, Groq, Ollama 等)
    config.Provider = LlmTornado.Code.LLmProviders.OpenAi;
    
    // 可选：配置 Endpoint (例如用于 Ollama 或 LocalAI)
    // config.Endpoint = "http://localhost:11434";
});
```

### 3. 在 Agent 中使用
Aevatar 框架会自动注入 `IAevatarLLMProvider`。你只需要在 Agent 初始化时指定使用该 Provider（通常通过配置或默认注入）。

```csharp
public class MyAgent : AIGAgentBase<MyState, MyConfig>
{
    // ...
    
    public async Task HandleEvent(...)
    {
        // 框架会自动使用注册的 LlmTornadoProvider
        await InitializeAsync("llmtornado", ...);
    }
}
```

## 🤝 贡献
欢迎提交 Issue 和 PR 来丰富 LlmTornado 的集成能力，特别是针对新出的模型和 MCP 工具的适配。
