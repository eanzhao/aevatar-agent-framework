# Aevatar Agent Framework - AI 集成指南

## 🤖 概述

Aevatar Agent Framework 提供完整的AI能力集成，支持将LLM、工具调用、记忆系统集成到分布式智能体中。本文档基于 **Microsoft.Extensions.AI** 集成方案。

---

## 📦 架构层次

```
┌─────────────────────────────────────────┐
│      Your AI Agent                       │
│   (Inherits MEAIGAgentBase)             │
├─────────────────────────────────────────┤
│      MEAIGAgentBase<TState>             │
│  - ChatClient (IChatClient)             │
│  - SystemPrompt                         │
│  - AITools                              │
├─────────────────────────────────────────┤
│      AIGAgentBase<TState>               │
│  - LLMProvider                          │
│  - Configuration                        │
├─────────────────────────────────────────┤
│      GAgentBase<TState>                 │
│  - Event Handling                       │
│  - State Management                     │
└─────────────────────────────────────────┘
```

---

## 🚀 快速开始

### 1. 定义Agent State

```protobuf
// ai_agent.proto
syntax = "proto3";

import "ai_messages.proto";  // AevatarAIAgentState定义在这里

message MyAIAgentState {
    string agent_id = 1;
    aevatar.agents.ai.AevatarAIAgentState ai_state = 2;  // AI状态（对话历史等）
}
```

### 2. 实现AI Agent

```csharp
using Aevatar.Agents.AI.MEAI;
using Microsoft.Extensions.AI;

public class MyAIAgent : MEAIGAgentBase<MyAIAgentState>
{
    // 系统提示词
    public override string SystemPrompt => 
        "You are a helpful assistant that can manage tasks and answer questions.";

    // 构造函数 - 注入ChatClient
    public MyAIAgent(IChatClient chatClient, ILogger<MyAIAgent>? logger = null)
        : base(chatClient, logger)
    {
    }

    // 或者使用配置构造
    public MyAIAgent(MEAIConfiguration config, ILogger<MyAIAgent>? logger = null)
        : base(config, logger)
    {
    }

    // 提供AI State访问
    protected override AevatarAIAgentState GetAIState()
    {
        return State.AiState;
    }

    // 实现描述
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"AI Agent: {State.AgentId}");
    }

    // 可选：注册AI工具
    protected override void RegisterMEAITools()
    {
        // AITools.Add(AIFunctionFactory.Create(...));
    }
}
```

### 3. 配置和使用

```csharp
// 配置Azure OpenAI
var config = new MEAIConfiguration
{
    Provider = "azure",
    Endpoint = "https://your-endpoint.openai.azure.com",
    DeploymentName = "gpt-4",
    ApiKey = "your-api-key",  // 或使用Azure认证
    Temperature = 0.7,
    MaxTokens = 2000
};

// 创建Agent
var chatClient = CreateChatClient(config);  // 或由DI提供
var agent = new MyAIAgent(chatClient, logger);

// 或直接使用配置
var agent = new MyAIAgent(config, logger);

// 通过Actor Manager创建
var manager = services.GetRequiredService<LocalGAgentActorManager>();
// 注意：需要确保ChatClient可以通过DI获取
```

---

## 🛠️ AI工具系统

### 工具接口

```csharp
public interface IAevatarTool
{
    string Name { get; }
    string Description { get; }
    
    Task<AevatarToolResult> ExecuteAsync(
        AevatarToolContext context,
        Dictionary<string, object?> parameters,
        CancellationToken ct = default);
    
    ToolParameterValidationResult ValidateParameters(
        Dictionary<string, object?> parameters);
}
```

### 实现自定义工具

```csharp
public class WeatherTool : AevatarToolBase
{
    public override string Name => "get_weather";
    public override string Description => "Get current weather for a city";

    protected override void DefineParameters()
    {
        AddParameter("city", "string", "City name", required: true);
        AddParameter("unit", "string", "Temperature unit (celsius/fahrenheit)", required: false);
    }

    protected override async Task<AevatarToolResult> ExecuteCoreAsync(
        AevatarToolContext context,
        Dictionary<string, object?> parameters,
        CancellationToken ct)
    {
        var city = parameters["city"]?.ToString() ?? "Unknown";
        var unit = parameters.GetValueOrDefault("unit")?.ToString() ?? "celsius";
        
        // 调用天气API
        var weather = await FetchWeatherAsync(city, unit);
        
        return AevatarToolResult.Success(weather, metadata: new {
            city,
            unit,
            timestamp = DateTime.UtcNow
        });
    }
}
```

### 注册工具到Agent

```csharp
public class MyAIAgent : MEAIGAgentBase<MyAIAgentState>
{
    protected override void RegisterAevatarTools()
    {
        // 注册自定义工具
        ToolManager.RegisterTool(new WeatherTool());
        
        // 或使用委托
        ToolManager.RegisterTool(
            name: "calculate",
            description: "Perform calculation",
            parameters: new[] {
                ("expression", "string", "Math expression")
            },
            execute: async (context, parameters) => {
                var expr = parameters["expression"]?.ToString();
                var result = Evaluate(expr);
                return AevatarToolResult.Success(result);
            }
        );
    }
}
```

### 内置工具

框架提供了一些内置工具：

1. **StateQueryTool**: 查询Agent状态
2. **EventPublisherTool**: 发布事件
3. **MemorySearchTool**: 搜索记忆
4. **HttpRequestTool**: HTTP请求

```csharp
// 启用内置工具
ToolManager.RegisterCoreTools(
    enableStateQuery: true,
    enableEventPublisher: true,
    enableMemorySearch: true
);
```

---

## 💬 对话管理

### 对话历史自动管理

```csharp
// AI State包含完整对话历史
var aiState = GetAIState();

// 添加消息（使用扩展方法）
aiState.AddUserMessage("Hello AI", maxHistory: 20);
aiState.AddAssistantMessage("Hello! How can I help?", maxHistory: 20);

// 对话历史自动限制在maxHistory条
// 自动估算token数量
```

### 对话上下文

```csharp
// 获取最近对话
var recent = aiState.GetRecentHistory(5);

// 获取估算token数
var tokens = aiState.GetEstimatedTokenCount();

// 按token限制修剪
aiState.TrimToTokenLimit(maxTokens: 4000, preserveSystemMessage: true);

// 清空历史
aiState.ConversationHistory.Clear();
```

---

## 🔌 LLM Provider 支持

### Microsoft.Extensions.AI (MEAI)

**当前推荐方案** ⭐

```csharp
// 支持多种后端
var config = new MEAIConfiguration
{
    Provider = "azure",  // 或 "openai"
    Model = "gpt-4",
    Temperature = 0.7
};

// Azure OpenAI
config.Endpoint = "https://*.openai.azure.com";
config.ApiKey = "key" 或 config.UseAzureCliAuth = true;

// OpenAI
config.ApiKey = "sk-...";
```

**特点**:
- ✅ 微软官方AI抽象
- ✅ 支持Azure OpenAI和OpenAI
- ✅ 原生工具调用支持
- ✅ 流式响应支持

---

## 🎯 AI事件处理

### AI增强的事件处理器

```csharp
[AevatarAIEventHandler]
public async Task HandleUserQuestion(UserQuestionEvent evt)
{
    // 构建AI请求
    var request = new AevatarLLMRequest
    {
        UserPrompt = evt.Question,
        SystemPrompt = SystemPrompt,
        Settings = new AevatarLLMSettings
        {
            Temperature = 0.7,
            MaxTokens = 500
        }
    };

    // 调用LLM
    var response = await LLMProvider.GenerateAsync(request);

    // 发布响应事件
    await PublishAsync(new AIResponseEvent
    {
        QuestionId = evt.QuestionId,
        Answer = response.Content
    });
}
```

---

## 📚 完整示例

```csharp
// 1. 定义proto
message CustomerServiceAgentState {
    string agent_id = 1;
    aevatar.agents.ai.AevatarAIAgentState ai_state = 2;
    repeated string handled_tickets = 3;
}

message CustomerInquiryEvent {
    string ticket_id = 1;
    string customer_id = 2;
    string question = 3;
}

// 2. 实现Agent
public class CustomerServiceAgent : MEAIGAgentBase<CustomerServiceAgentState>
{
    public override string SystemPrompt => 
        "You are a helpful customer service agent. Be polite and professional.";

    public CustomerServiceAgent(IChatClient chatClient, ILogger<CustomerServiceAgent>? logger = null)
        : base(chatClient, logger)
    {
    }

    protected override AevatarAIAgentState GetAIState() => State.AiState;

    [EventHandler]
    public async Task HandleCustomerInquiry(CustomerInquiryEvent evt)
    {
        // 使用AI处理客户问题
        var aiState = GetAIState();
        aiState.AddUserMessage(evt.Question, maxHistory: 10);

        // 调用LLM（通过MEAILLMProvider自动处理）
        var response = await LLMProvider.GenerateAsync(new AevatarLLMRequest
        {
            UserPrompt = evt.Question,
            Messages = aiState.ConversationHistory.ToList()
        });

        // 记录响应
        aiState.AddAssistantMessage(response.Content, maxHistory: 10);
        State.HandledTickets.Add(evt.TicketId);

        Logger.LogInformation("Handled ticket {TicketId} for customer {CustomerId}",
            evt.TicketId, evt.CustomerId);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Customer Service Agent, Handled {State.HandledTickets.Count} tickets");
    }
}

// 3. 配置和使用
var services = new ServiceCollection();
services.AddLogging();

// 配置MEAI
var chatClient = new AzureOpenAIClient(
    new Uri("https://your-endpoint.openai.azure.com"),
    new AzureKeyCredential("your-key")
).GetChatClient("gpt-4").AsIChatClient();

services.AddSingleton(chatClient);

// 注册Local Runtime
services.AddSingleton<LocalGAgentActorFactory>();
services.AddSingleton<LocalGAgentActorManager>();
// ...

var sp = services.BuildServiceProvider();
var manager = sp.GetRequiredService<LocalGAgentActorManager>();

// 创建AI Agent
var actor = await manager.CreateAndRegisterAsync<CustomerServiceAgent>(agentId);

// 发送客户咨询
await actor.PublishEventAsync(new EventEnvelope
{
    Id = Guid.NewGuid().ToString(),
    Payload = Any.Pack(new CustomerInquiryEvent
    {
        TicketId = "T-001",
        CustomerId = "C-123",
        Question = "How do I reset my password?"
    })
});
```

---

## 🎯 高级特性

### 1. 流式响应

```csharp
// 使用GetStreamingResponseAsync进行流式输出
await foreach (var chunk in chatClient.GetStreamingResponseAsync(messages))
{
    Console.Write(chunk.Text);
    // 实时输出AI响应
}
```

### 2. 工具链

```csharp
// AI可以调用多个工具形成工具链
ToolManager.RegisterTool(new DatabaseQueryTool());
ToolManager.RegisterTool(new SendEmailTool());

// AI会自动决定调用顺序：
// 1. DatabaseQueryTool → 查询数据
// 2. SendEmailTool → 发送结果
```

### 3. 记忆系统

```csharp
// AI Agent可以访问长期记忆
public class SmartAgent : MEAIGAgentBase<SmartAgentState>
{
    private IAevatarMemory _memory;

    protected override void RegisterAevatarTools()
    {
        // 注册记忆搜索工具
        ToolManager.RegisterTool(new MemorySearchTool(_memory));
    }
}
```

---

## 📝 配置参考

### MEAIConfiguration

```csharp
public class MEAIConfiguration
{
    // 提供商
    public string Provider { get; set; }  // "azure" | "openai"
    
    // Azure OpenAI
    public string? Endpoint { get; set; }
    public string? DeploymentName { get; set; }
    public bool UseAzureCliAuth { get; set; } = false;
    
    // OpenAI
    public string? ApiKey { get; set; }
    
    // 模型设置
    public string? Model { get; set; } = "gpt-4";
    public double? Temperature { get; set; } = 0.7;
    public int? MaxTokens { get; set; } = 2000;
    
    // 或直接提供ChatClient
    public IChatClient? ChatClient { get; set; }
}
```

---

## 🔧 工具开发指南

### 工具基类

```csharp
public abstract class AevatarToolBase : IAevatarTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    
    protected List<ToolParameter> Parameters { get; } = new();

    // 子类实现
    protected abstract Task<AevatarToolResult> ExecuteCoreAsync(
        AevatarToolContext context,
        Dictionary<string, object?> parameters,
        CancellationToken ct);

    // 定义参数
    protected void AddParameter(string name, string type, string description, bool required = false)
    {
        Parameters.Add(new ToolParameter
        {
            Name = name,
            Type = type,
            Description = description,
            Required = required
        });
    }
}
```

### 工具上下文

```csharp
public class AevatarToolContext
{
    public Guid AgentId { get; set; }        // 调用工具的Agent
    public string? ConversationId { get; set; }  // 对话ID
    public ILogger? Logger { get; set; }     // 日志
    public IServiceProvider? Services { get; set; }  // DI容器
}
```

### 工具结果

```csharp
public class AevatarToolResult
{
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> Metadata { get; set; }

    // 便利方法
    public static AevatarToolResult Success(object? data, object? metadata = null);
    public static AevatarToolResult Failure(string error);
}
```

---

## 📖 参考示例

- `src/Aevatar.Agents.AI.MEAI/` - MEAI集成实现
- `src/Aevatar.Agents.AI.Core/Tools/` - 内置工具实现
- `test/Aevatar.Agents.AI.Tests/MEAIGAgentBaseTests.cs` - AI Agent测试

---

**AI + Agent = 分布式智能的完美结合** 🤖🌊

