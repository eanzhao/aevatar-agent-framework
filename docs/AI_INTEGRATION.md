# Aevatar Agent Framework - AI 集成指南

## 🌌 概述

本文档介绍如何在Aevatar Agent Framework中构建AI Agent，包括LLM集成、工具系统、对话管理、策略选择等内容。

---

## 📊 AI Agent 架构（3级层次结构）

### Level 1: AIGAgentBase（基础AI代理）

最基础的AI Agent，提供LLM聊天能力。注意：**AIGAgentBase只有无参构造函数，LLM provider必须通过InitializeAsync()方法初始化**。

  ```csharp
  public class CustomerServiceAgent : AIGAgentBase<AevatarAIAgentState>
  {
      // 在编码时定义System Prompt（关键：不是运行时配置）
      public override string SystemPrompt =>
          "You are Emma, a friendly customer service agent for Aevatar Inc. " +
          "Always be helpful, patient, and professional.";

      public CustomerServiceAgent()
      {
          // 注意：AIGAgentBase只有无参构造函数
          // LLM provider将在InitializeAsync中初始化
      }

      public override async Task<string> GetDescriptionAsync()
      {
          return "Customer service agent for Aevatar Inc.";
      }

      // 可选：配置AI参数
      protected override void ConfigureAI(AevatarAIAgentConfiguration config)
      {
          config.Model = "gpt-4";
          config.Temperature = 0.7;
          config.MaxTokens = 2000;
          config.MaxHistory = 50;
      }
  }

  // 使用方式1：通过provider name从配置初始化
  var agent = new CustomerServiceAgent();
  await agent.InitializeAsync("openai-gpt4");  // 从appsettings.json读取配置
  var response = await agent.GenerateResponseAsync("Hello!");

  // 使用方式2：通过自定义配置初始化
  var config = new LLMProviderConfig
  {
      ProviderType = "openai",
      ApiKey = "sk-...",
      Model = "gpt-4",
      Temperature = 0.7
  };
  var agent = new CustomerServiceAgent();
  await agent.InitializeAsync(config);

  能力：
  - GenerateResponseAsync() - 生成AI响应
  - ChatAsync() - 带对话历史的聊天
  - ChatStreamAsync() - 流式响应
  - SupportsStreamingAsync() - 检查是否支持流式

  ---
  Level 2: AIGAgentWithToolBase（带工具的AI代理）

  在Level 1基础上增加工具/函数调用能力。同样只有无参构造函数，通过InitializeAsync初始化。

  public class DataAnalysisAgent : AIGAgentWithToolBase<AevatarAIAgentState>
  {
      public override string SystemPrompt =>
          "You are a data analyst with access to visualization tools.";

      public DataAnalysisAgent()
      {
          // AIGAgentWithToolBase也只有无参构造函数
          // 父类AIGAgentBase的InitializeAsync将负责初始化LLM provider
      }

      // 在构造函数或此方法中注册工具
      protected override void RegisterTools()
      {
          // 使用传统方式
          RegisterTool<AevatarMemorySearchTool>();
          RegisterTool<AevatarFileReadTool>();

          // 使用新的接口方式（推荐）
          RegisterToolAsync(new HttpRequestTool());
          RegisterToolAsync(new CustomCalculatorTool());
      }

      // 必须实现的事件发布方法
      protected override Task PublishChatResponseAsync(
          ChatResponse response, string requestId)
      {
          // 发布到Grain的stream
          return PublishAsync(response);
      }

      protected override Task PublishToolExecutionEventAsync(
          string toolName,
          Dictionary<string, object> parameters,
          ToolExecutionResult result,
          string requestId)
      {
          var evt = new ToolExecutionResponseEvent
          {
              ToolName = toolName,
              Success = result.Success,
              Result = result.Result?.ToString() ?? ""
          };
          return PublishAsync(evt);
      }

      protected override void UpdateActiveToolsInState()
      {
          State.ActiveTools.Clear();
          foreach (var tool in GetTools())
          {
              State.ActiveTools.Add(tool.Name);
          }
      }
  }

  // 使用
  var agent = new DataAnalysisAgent();
  await agent.InitializeAsync("azure-gpt35");  // 先初始化LLM provider

  var response = await agent.ChatWithToolAsync(new ChatRequest
  {
      Message = "Calculate the average of this data and visualize it"
  });

  能力（继承Level 1的所有能力）：
  - ChatWithToolAsync() - 当LLM需要时自动调用工具
  - ExecuteToolAsync() - 手动执行工具
  - GetTools() - 获取已注册的工具列表

  ---
  Level 3: AIGAgentWithProcessStrategy（带策略的AI代理）

  在Level 2基础上增加LLM元推理能力，让AI自动选择处理策略。同样只有无参构造函数。

  public class SmartRouterAgent : AIGAgentWithProcessStrategy<AevatarAIAgentState>
  {
      public override string SystemPrompt =>
          "You are an intelligent router that selects the best strategy for each query.";

      public SmartRouterAgent()
      {
          // AIGAgentWithProcessStrategy也只有无参构造函数
          // 继承自AIGAgentWithToolBase，同样通过InitializeAsync初始化
      }

      protected override void RegisterTools()
      {
          RegisterTool<CalculatorTool>();
          RegisterTool<SearchTool>();
          RegisterTool<CodeExecutionTool>();
      }
  }

  // 使用 - LLM自动选择策略
  var agent = new SmartRouterAgent();
  await agent.InitializeAsync("openai-gpt4");

  // 对于简单问题，LLM会自动选择standard策略
  var response1 = await agent.ChatWithToolAsync(new ChatRequest
  {
      Message = "What's 2+2?"
  });

  // 对于需要解释的问题，LLM会自动选择chain_of_thought
  var response2 = await agent.ChatWithToolAsync(new ChatRequest
  {
      Message = "Explain how photosynthesis works step by step"
  });

  // 对于需要工具的问题，LLM会自动选择react
  var response3 = await agent.ChatWithToolAsync(new ChatRequest
  {
      Message = "Calculate last month's sales from the database"
  });

  新增能力：
  - LLM会自动选择策略：standard、chain_of_thought、react、tree_of_thoughts
  - 支持手动指定：request.Context["strategy"] = "react"
  - 关键词回退（当LLM选择失败时）

  支持的策略：
  - standard - 直接回答，适用于简单问题
  - chain_of_thought - 逐步推理，适用于"为什么"、"如何"
  - react - 使用工具，适用于需要计算/搜索/查询
  - tree_of_thoughts - 多路径探索，适用于复杂问题

  ---
  🔧 LLM Provider 配置

  支持的Provider类型

  # appsettings.json
  {
    "LLMProviders": {
      "default": "openai-gpt4",
      "providers": {
        "openai-gpt4": {
          "providerType": "openai",
          "apiKey": "${OPENAI_API_KEY}",
          "model": "gpt-4",
          "temperature": 0.7,
          "maxTokens": 2000
        },
        "azure-gpt35": {
          "providerType": "azure",
          "apiKey": "${AZURE_API_KEY}",
          "endpoint": "https://your-resource.openai.azure.com",
          "deployment": "gpt-35-turbo",
          "temperature": 0.3
        },
        "local-llama": {
          "providerType": "ollama",
          "endpoint": "http://localhost:11434",
          "model": "llama2:70b"
        }
      }
    }
  }

  配置LLMProviderFactory

  // 在DI容器中注册
  services.AddSingleton<ILLMProviderFactory, MEAILLMProviderFactory>();

  // 配置LLM providers
  services.Configure<LLMProvidersConfig>(configuration.GetSection("LLMProviders"));

  // 如果要手动获取provider
  var factory = serviceProvider.GetRequiredService<ILLMProviderFactory>();
  var provider = await factory.GetProviderAsync("openai-gpt4");

  ---
  🛠️ 工具系统（Tool System）

  工具接口

  public interface IAevatarTool
  {
      string Name { get; }
      string Description { get; }
      IReadOnlyList<ToolParameter> Parameters { get; }

      Task<ToolExecutionResult> ExecuteAsync(
          Dictionary<string, object> parameters,
          ExecutionContext? context = null,
          CancellationToken cancellationToken = default);
  }

  创建自定义工具

  public class WeatherTool : IAevatarTool
  {
      public string Name => "get_weather";
      public string Description => "Get current weather for a location";

      public IReadOnlyList<ToolParameter> Parameters => new[]
      {
          new ToolParameter("location", "string", "City name or coordinates", true),
          new ToolParameter("unit", "string", "celsius or fahrenheit", false)
      };

      public async Task<ToolExecutionResult> ExecuteAsync(
          Dictionary<string, object> parameters,
          ExecutionContext? context,
          CancellationToken cancellationToken)
      {
          var location = parameters["location"].ToString();
          var unit = parameters.ContainsKey("unit") ? parameters["unit"].ToString() : "celsius";

          // Call weather API
          var weatherData = await _weatherApi.GetWeatherAsync(location, unit);

          return new ToolExecutionResult
          {
              Success = true,
              Result = weatherData
          };
      }
  }

  // 注册工具
  RegisterToolAsync(new WeatherTool());

  内置工具

  框架提供以下内置工具：

  | 工具                      | 描述        | 用途        |
  |-------------------------|-----------|-----------|
  | AevatarMemorySearchTool | 搜索对话历史    | 回顾之前的聊天   |
  | AevatarFileReadTool     | 读取文件      | 加载文档      |
  | HttpRequestTool         | HTTP请求    | API调用     |
  | StateQueryTool          | 查询Agent状态 | 获取内部状态    |
  | EventPublisherTool      | 发布事件      | 触发其他Agent |

  ---
  💬 对话管理

  对话历史

  Agent自动维护对话历史（在State中）：

  message AevatarAIAgentState {
      string id = 1;
      repeated ChatMessage conversation_history = 2;
      repeated string active_tools = 3;
      AevatarAIAgentConfiguration ai_configuration = 4;
  }

  message ChatMessage {
      AevatarChatRole role = 1;
      string content = 2;
      google.protobuf.Timestamp timestamp = 3;
      string name = 4; // 可选，用于工具调用
  }

  enum AevatarChatRole {
      System = 0;
      User = 1;
      Assistant = 2;
      Function = 3;
  }

  自定义对话历史

  protected override void AddMessageToHistory(string content, AevatarChatRole role, string? name = null)
  {
      State.ConversationHistory.Add(new ChatMessage
      {
          Role = role,
          Content = content,
          Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
          Name = name ?? ""
      });

      // 保持历史长度不超过配置
      if (State.ConversationHistory.Count > Configuration.MaxHistory)
      {
          State.ConversationHistory.RemoveAt(0);
      }
  }

  ---
  🎛️ 策略系统（Processing Strategies）

  策略系统让LLM能够选择最佳的处理方式。

  内置策略

  // Standard - 直接回答
  var response = await agent.ChatWithToolAsync(new ChatRequest
  {
      Message = "What's 2+2?",
      Context = new Dictionary<string, object> { ["strategy"] = "standard" }
  });

  // Chain-of-Thought - 逐步推理
  var response = await agent.ChatWithToolAsync(new ChatRequest
  {
      Message = "Explain how to solve x^2 - 5x + 6 = 0",
      Context = new Dictionary<string, object> { ["strategy"] = "chain_of_thought" }
  });

  // ReAct - 使用工具
  var response = await agent.ChatWithToolAsync(new ChatRequest
  {
      Message = "Calculate the weather in Beijing and send it to the team",
      Context = new Dictionary<string, object> { ["strategy"] = "react" }
  });

  元推理（Meta-Reasoning）

  AIGAgentWithProcessStrategy会自动使用LLM选择策略：

  // 用户请求
  "What's the capital of France?" → standard（直接回答）

  // 用户请求
  "Explain step-by-step how photosynthesis works" → chain_of_thought（需要解释）

  // 用户请求
  "Calculate the total of last month's sales" → react（需要查询数据）

  // 用户请求
  "Find three different ways to optimize this algorithm" → tree_of_thoughts（需要创造性）

  ---
  🚀 最佳实践

  1. System Prompt设计

  // ✅ 好的System Prompt
  public override string SystemPrompt =>
      "You are a senior data analyst with 10 years of experience. " +
      "Always explain your reasoning step-by-step. " +
      "When using tools, show your work.";

  // ❌ 不明确的Prompt
  public override string SystemPrompt => "You are an assistant.";

  2. 工具使用

  // ✅ 提供详细描述
  public class DataAnalysisTool : IAevatarTool
  {
      public string Description =>
          "Analyze CSV data and return statistics. " +
          "Input: CSV file path. Output: JSON with mean, median, mode.";

      // ...
  }

  3. 错误处理

  try
  {
      var response = await agent.ChatWithToolAsync(request);
  }
  catch (ToolExecutionException ex)
  {
      // 工具执行失败
      _logger.LogError(ex, "Tool execution failed: {Tool}", ex.ToolName);
  }
  catch (LLMProviderException ex)
  {
      // LLM调用失败
      _logger.LogError(ex, "LLM provider error");
  }

  4. 流式响应

  // 对于长回答，使用流式
  if (await agent.SupportsStreamingAsync())
  {
      await foreach (var token in agent.ChatStreamAsync(request))
      {
          await Console.WriteAsync(token);
      }
  }
  else
  {
      var response = await agent.ChatAsync(request);
      Console.WriteLine(response.Content);
  }

  ---
  🔍 调试和监控

  工具调用追踪

  // 订阅工具执行事件
  [EventHandler]
  public async Task HandleToolExecution(ToolExecutionResponseEvent evt)
  {
      _logger.LogInformation("Tool {Tool} executed: Success={Success}, Result={Result}",
          evt.ToolName, evt.Success, evt.Result);
  }

  令牌使用监控

  var response = await agent.ChatWithToolAsync(request);
  if (response.Usage != null)
  {
      _metrics.RecordTokenUsage(
          response.Usage.PromptTokens,
          response.Usage.CompletionTokens,
          response.Usage.TotalTokens);
  }

  ---
  📚 代码示例

  完整示例请参考：
  - examples/AIAgentDemo/ - AI Agent完整示例
  - test/Aevatar.Agents.AI.Core.Tests/AIGAgentBaseExamples.cs - 使用示例
  - test/Aevatar.Agents.AI.Tests/AIGAgentTests.cs - 单元测试

  ---
  Last Updated: 2025-11-17
  Framework Version: 3.0 (3-Level AI Agent Hierarchy)

  **主要修正**：
  1. ✅ AIGAgentBase只有无参构造函数（`public CustomerServiceAgent()`）
  2. ✅ 必须通过`InitializeAsync()`方法初始化LLM provider
  3. ✅ 提供了两种初始化方式（通过provider name或自定义config）
  4. ✅ AIGAgentWithToolBase和AIGAgentWithProcessStrategy同样只有无参构造函数