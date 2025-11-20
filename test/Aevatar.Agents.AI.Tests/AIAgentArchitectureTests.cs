using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace Aevatar.Agents.AI.Tests;

/// <summary>
/// Simple tests demonstrating how to use AI Agent
/// No Silo configuration needed - just demonstrates the architecture
/// </summary>
public class AIAgentArchitectureTests
{
    [Fact]
    public async Task Test_1_AgentWithLLMProvider()
    {
        // Arrange - Mock the ChatClient (this simulates OpenAI, Azure, or any LLM)
        var mockChatClient = new Mock<IChatClient>();
        var mockResponse = new Microsoft.Extensions.AI.ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello! I am your AI assistant."));
        mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // Create LLM Provider (this wraps the ChatClient)
        var providerConfig = new LLMProviderConfig
        {
            ProviderType = "openai",
            ApiKey = "test-key",
            Model = "gpt-4",
            Temperature = 0.7
        };
        var provider = new TestMEAILLMProvider(mockChatClient.Object, providerConfig);

        // Create Agent
        var agent = new TestSimpleAgent(provider);

        // Act - Use the Agent
        var response = await agent.GenerateResponseAsync("Hello, who are you?");

        // Assert
        Assert.Contains("AI assistant", response);
    }

    [Fact]
    public void Test_2_Level1AgentDefinition()
    {
        // This test shows how to define a Level 1 Agent (Basic AI Agent with chat)
        var agentDefinition =
            "public class CustomerServiceAgent : AIGAgentBase<AevatarAIAgentState>\n" +
            "{\n" +
            "    protected override string SystemPrompt =>\n" +
            "        \"You are Emma, a friendly customer service representative...\";\n" +
            "\n" +
            "    public CustomerServiceAgent(IAevatarLLMProvider llmProvider)\n" +
            "        : base(llmProvider) { }\n" +
            "\n" +
            "    protected override AevatarAIAgentState GetAIState()\n" +
            "        => new AevatarAIAgentState();\n" +
            "\n" +
            "    public override Task<string> GetDescriptionAsync()\n" +
            "        => Task.FromResult(\"Customer service agent\");\n" +
            "}";

        Assert.NotNull(agentDefinition);
    }

    [Fact]
    public void Test_3_Level2AgentDefinition()
    {
        // This test shows how to define a Level 2 Agent (with custom tools)
        var agentDefinition =
            "public class DataAnalysisAgent : AIGAgentBase<AevatarAIAgentState>\n" +
            "{\n" +
            "    protected override string SystemPrompt =>\n" +
            "        \"You are a data analyst...\";\n" +
            "\n" +
            "    public DataAnalysisAgent(IAevatarLLMProvider llmProvider)\n" +
            "        : base(llmProvider)\n" +
            "    {\n" +
            "        // Register custom tools\n" +
            "        RegisterTool<DataVisualizationTool>();\n" +
            "        RegisterTool<DataQueryTool>();\n" +
            "        RegisterTool<ExcelExportTool>();\n" +
            "    }\n" +
            "\n" +
            "    protected override AevatarAIAgentState GetAIState()\n" +
            "        => new AevatarAIAgentState();\n" +
            "\n" +
            "    public override Task<string> GetDescriptionAsync()\n" +
            "        => Task.FromResult(\"Data analysis agent\");\n" +
            "}";

        Assert.NotNull(agentDefinition);
    }

    [Fact]
    public void Test_4_AppsettingsConfiguration()
    {
        // Example appsettings.json configuration
        var configExample = @"
{
  ""LLMProviders"": {
    ""default"": ""openai-gpt4"",
    ""providers"": {
      // High performance for complex conversations
      ""openai-gpt4"": {
        ""providerType"": ""openai"",
        ""apiKey"": ""${OPENAI_API_KEY}"",
        ""model"": ""gpt-4"",
        ""temperature"": 0.7,
        ""maxTokens"": 4000
      },
      // Fast and cheap for simple tasks
      ""azure-gpt35"": {
        ""providerType"": ""azureopenai"",
        ""apiKey"": ""${AZURE_API_KEY}"",
        ""endpoint"": ""https://your-resource.openai.azure.com"",
        ""deploymentName"": ""gpt-35-turbo"",
        ""temperature"": 0.3
      },
      // Local and private for sensitive data
      ""local-llama"": {
        ""providerType"": ""ollama"",
        ""endpoint"": ""http://localhost:11434"",
        ""model"": ""llama3.1:70b""
      }
    }
  }
}";

        Assert.NotNull(configExample);
    }
}

/// <summary>
/// Test LLM Provider - wraps IChatClient for testing
/// </summary>
internal class TestMEAILLMProvider : IAevatarLLMProvider
{
    private readonly IChatClient _chatClient;
    private readonly LLMProviderConfig _config;

    public TestMEAILLMProvider(IChatClient chatClient, LLMProviderConfig config)
    {
        _chatClient = chatClient;
        _config = config;
    }

    public async Task<AevatarLLMResponse> GenerateAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(request.SystemPrompt))
            messages.Add(new ChatMessage(ChatRole.System, request.SystemPrompt));

        if (!string.IsNullOrEmpty(request.UserPrompt))
            messages.Add(new ChatMessage(ChatRole.User, request.UserPrompt));

        var options = new ChatOptions
        {
            Temperature = (float)(request.Settings?.Temperature ?? _config.Temperature),
            MaxOutputTokens = request.Settings?.MaxTokens ?? _config.MaxTokens,
            ModelId = _config.Model
        };

        var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);

        return new AevatarLLMResponse
        {
            Content = response.Text ?? string.Empty,
            ModelName = response.ModelId ?? _config.Model
        };
    }

    public async IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(
        AevatarLLMRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new AevatarLLMToken { Content = "Stream not implemented in test" };
    }

    public Task<AevatarModelInfo> GetModelInfoAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AevatarModelInfo
        {
            Name = _config.Model,
            MaxTokens = _config.MaxTokens
        });
    }
}

/// <summary>
/// Simple test agent for demonstration
/// </summary>
public class TestSimpleAgent
{
    private readonly IAevatarLLMProvider _llmProvider;
    private readonly string _systemPrompt = "You are a helpful AI assistant.";

    public TestSimpleAgent(IAevatarLLMProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

    public async Task<string> GenerateResponseAsync(string userInput)
    {
        var request = new AevatarLLMRequest
        {
            SystemPrompt = _systemPrompt,
            UserPrompt = userInput
        };

        var response = await _llmProvider.GenerateAsync(request);
        return response.Content;
    }
}
