using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.MEAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Aevatar.Agents.AI.Tests;

/// <summary>
/// AIGAgentBaseExamples 的单元测试
/// 测试从 AIGAgentBase 继承的 Agent
/// </summary>
public class AIGAgentBaseExampleTests
{
    private readonly Mock<IChatClient> _mockChatClient;
    private readonly Mock<ILogger<AIGAgentBaseExamples.CustomerServiceAgent>> _mockLogger1;
    private readonly Mock<ILogger<AIGAgentBaseExamples.DataAnalysisAgent>> _mockLogger2;

    public AIGAgentBaseExampleTests()
    {
        _mockChatClient = new Mock<IChatClient>();
        _mockLogger1 = new Mock<ILogger<AIGAgentBaseExamples.CustomerServiceAgent>>();
        _mockLogger2 = new Mock<ILogger<AIGAgentBaseExamples.DataAnalysisAgent>>();
    }

    /// <summary>
    /// 测试 1：CustomerServiceAgent 能正确生成响应
    /// </summary>
    [Fact]
    public async Task CustomerServiceAgent_CanGenerateResponse()
    {
        // Arrange
        var mockResponse = new Microsoft.Extensions.AI.ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello! How can I help you today?"));
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(mockResponse);

        // 创建 LLM Provider
        var providerConfig = new LLMProviderConfig
        {
            ProviderType = "openai",
            ApiKey = "test-key",
            Model = "gpt-4",
            Temperature = 0.7
        };
        var llmProvider = new MEAILLMProvider(_mockChatClient.Object, providerConfig, Mock.Of<ILogger<MEAILLMProvider>>());

        // 创建 Agent
        var agent = new AIGAgentBaseExamples.CustomerServiceAgent(llmProvider, _mockLogger1.Object);

        // Act
        var request = new AevatarLLMRequest
        {
            SystemPrompt = agent.SystemPrompt,
            UserPrompt = "Hello!"
        };
        var response = await llmProvider.GenerateAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.Contains("help", response.Content);

        // 验证 System Prompt 已正确设置
        Assert.Contains("customer service", agent.SystemPrompt);
        Assert.Contains("professional", agent.SystemPrompt);
    }

    /// <summary>
    /// 测试 2：DataAnalysisAgent 的配置正确
    /// </summary>
    [Fact]
    public void DataAnalysisAgent_Configuration_IsCorrect()
    {
        // Arrange
        var providerConfig = new LLMProviderConfig
        {
            ProviderType = "openai",
            ApiKey = "test-key",
            Model = "gpt-4"
        };
        var llmProvider = new MEAILLMProvider(_mockChatClient.Object, providerConfig, Mock.Of<ILogger<MEAILLMProvider>>());

        // 创建 Agent
        var agent = new AIGAgentBaseExamples.DataAnalysisAgent(llmProvider, _mockLogger2.Object);

        // Assert - 验证系统提示词
        Assert.Contains("data analyst", agent.SystemPrompt);
        Assert.Contains("step by step", agent.SystemPrompt);

        // 验证配置
        Assert.Equal("gpt-4", agent.Configuration.Model);
        Assert.Equal(0.3, agent.Configuration.Temperature); // 数据分析使用较低温度
        Assert.Equal(4000, agent.Configuration.MaxTokens);
    }

    /// <summary>
    /// 测试 3：CustomerServiceAgent 的 GetDescriptionAsync 工作正常
    /// </summary>
    [Fact]
    public async Task CustomerServiceAgent_GetDescriptionAsync_ReturnsCorrectDescription()
    {
        // Arrange
        var providerConfig = new LLMProviderConfig
        {
            ProviderType = "openai",
            ApiKey = "test-key",
            Model = "gpt-4"
        };
        var llmProvider = new MEAILLMProvider(_mockChatClient.Object, providerConfig, Mock.Of<ILogger<MEAILLMProvider>>());
        var agent = new AIGAgentBaseExamples.CustomerServiceAgent(llmProvider, _mockLogger1.Object);

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        Assert.Equal("Customer service agent for Aevatar Inc.", description);
    }

    /// <summary>
    /// 测试 4：DataAnalysisAgent 的 GetDescriptionAsync 工作正常
    /// </summary>
    [Fact]
    public async Task DataAnalysisAgent_GetDescriptionAsync_ReturnsCorrectDescription()
    {
        // Arrange
        var providerConfig = new LLMProviderConfig
        {
            ProviderType = "openai",
            ApiKey = "test-key",
            Model = "gpt-4"
        };
        var llmProvider = new MEAILLMProvider(_mockChatClient.Object, providerConfig, Mock.Of<ILogger<MEAILLMProvider>>());
        var agent = new AIGAgentBaseExamples.DataAnalysisAgent(llmProvider, _mockLogger2.Object);

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        Assert.Equal("Data analysis agent with visualization tools", description);
    }

    /// <summary>
    /// 测试 5：配置参数被正确应用
    /// </summary>
    [Fact]
    public void CustomerServiceAgent_Configuration_AppliesCorrectly()
    {
        // Arrange
        var providerConfig = new LLMProviderConfig
        {
            ProviderType = "openai",
            ApiKey = "test-key",
            Model = "gpt-4"
        };
        var llmProvider = new MEAILLMProvider(_mockChatClient.Object, providerConfig, Mock.Of<ILogger<MEAILLMProvider>>());

        // Act
        var agent = new AIGAgentBaseExamples.CustomerServiceAgent(llmProvider, _mockLogger1.Object);

        // Assert
        Assert.NotNull(agent.Configuration);
        Assert.Equal("gpt-4", agent.Configuration.Model);
        Assert.Equal(0.7, agent.Configuration.Temperature);  // 客服使用较高温度，更 creative
        Assert.Equal(2000, agent.Configuration.MaxTokens);
        Assert.Equal(50, agent.Configuration.MaxHistory);    // 保存50条历史记录
    }

    /// <summary>
    /// 测试 6：SystemPrompt 是公共可访问的
    /// </summary>
    [Fact]
    public void BothAgents_SystemPrompt_IsAccessible()
    {
        // Arrange
        var providerConfig = new LLMProviderConfig
        {
            ProviderType = "openai",
            ApiKey = "test-key",
            Model = "gpt-4"
        };
        var llmProvider = new MEAILLMProvider(_mockChatClient.Object, providerConfig, Mock.Of<ILogger<MEAILLMProvider>>());

        var customerAgent = new AIGAgentBaseExamples.CustomerServiceAgent(llmProvider, _mockLogger1.Object);
        var analysisAgent = new AIGAgentBaseExamples.DataAnalysisAgent(llmProvider, _mockLogger2.Object);

        // Assert - SystemPrompt 应该是公共可访问的
        Assert.NotEmpty(customerAgent.SystemPrompt);
        Assert.NotEmpty(analysisAgent.SystemPrompt);

        Assert.Contains("Emma", customerAgent.SystemPrompt);
        Assert.Contains("customer service", customerAgent.SystemPrompt);

        Assert.Contains("data analyst", analysisAgent.SystemPrompt);
        Assert.Contains("step by step", analysisAgent.SystemPrompt);
    }

    /// <summary>
    /// 测试 7：Agent 能正确使用 LLM Provider 生成响应
    /// </summary>
    [Fact]
    public async Task Agent_CanUseLLMProvider_ToGenerateResponse()
    {
        // Arrange
        var expectedResponse = "Based on my analysis, the data shows...";
        var mockResponse = new Microsoft.Extensions.AI.ChatResponse(new ChatMessage(ChatRole.Assistant, expectedResponse));
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(mockResponse);

        var providerConfig = new LLMProviderConfig
        {
            ProviderType = "openai",
            ApiKey = "test-key",
            Model = "gpt-4",
            Temperature = 0.3,
            MaxTokens = 4000
        };
        var llmProvider = new MEAILLMProvider(_mockChatClient.Object, providerConfig, Mock.Of<ILogger<MEAILLMProvider>>());

        // 使用 DataAnalysisAgent（要求更严格）
        var agent = new AIGAgentBaseExamples.DataAnalysisAgent(llmProvider, _mockLogger2.Object);

        // Act
        var request = new AevatarLLMRequest
        {
            SystemPrompt = agent.SystemPrompt,
            UserPrompt = "Analyze this sales data for Q4"
        };
        var response = await llmProvider.GenerateAsync(request);

        // Assert
        Assert.NotNull(response);

        // 验证调用了 ChatClient
        _mockChatClient.Verify(
            x => x.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<System.Threading.CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// 测试 8：LLMProvider 属性可访问
    /// </summary>
    [Fact]
    public void Agent_LLMProvider_IsAccessible()
    {
        // Arrange
        var providerConfig = new LLMProviderConfig
        {
            ProviderType = "openai",
            ApiKey = "test-key",
            Model = "gpt-4"
        };
        var llmProvider = new MEAILLMProvider(_mockChatClient.Object, providerConfig, Mock.Of<ILogger<MEAILLMProvider>>());
        var agent = new AIGAgentBaseExamples.CustomerServiceAgent(llmProvider, _mockLogger1.Object);

        // Assert
        Assert.NotNull(agent.LLMProvider);
        Assert.Equal(llmProvider, agent.LLMProvider);
    }
}
