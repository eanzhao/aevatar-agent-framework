using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Core.Extensions;
using Aevatar.Agents.AI.MEAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Aevatar.Agents.AI.Tests;

/// <summary>
/// MEAIGAgentBase 测试
/// 测试Microsoft.Extensions.AI集成和对话管理
/// </summary>
public class MEAIGAgentBaseTests
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<IChatClient> _mockChatClient;
    private readonly Mock<ILogger<TestMEAIAgent>> _mockLogger;

    public MEAIGAgentBaseTests(ITestOutputHelper output)
    {
        _output = output;
        _mockChatClient = new Mock<IChatClient>();
        _mockLogger = new Mock<ILogger<TestMEAIAgent>>();
    }

    /// <summary>
    /// 测试基本初始化
    /// </summary>
    [Fact]
    public void Test_MEAIAgent_Initialization()
    {
        // Arrange & Act
        var agent = new TestMEAIAgent(_mockChatClient.Object, _mockLogger.Object);

        // Assert
        Assert.NotNull(agent);
        // ChatClient is protected, cannot access directly
        Assert.Equal("You are a test MEAI assistant", agent.SystemPrompt);
        
        // Test AI state initialization
        var aiState = agent.GetTestAIState();
        Assert.NotNull(aiState);
        Assert.NotNull(aiState.ConversationHistory);
        // InitializeConversation() automatically adds system message during construction
        Assert.Single(aiState.ConversationHistory);
        Assert.Equal(Aevatar.Agents.AI.AevatarChatRole.System, aiState.ConversationHistory[0].Role);
    }

    /// <summary>
    /// 测试对话历史管理
    /// </summary>
    [Fact]
    public void Test_ConversationHistory_Management()
    {
        // Arrange
        var agent = new TestMEAIAgent(_mockChatClient.Object, _mockLogger.Object);
        var aiState = agent.GetTestAIState();

        // Act - Add messages using extension methods
        aiState.AddUserMessage("Hello AI", 100);
        aiState.AddAssistantMessage("Hello! How can I help you?", 100);
        aiState.AddUserMessage("What's the weather?", 100);
        aiState.AddAssistantMessage("I don't have access to weather data.", 100);

        // Assert - Agent automatically adds system message during initialization
        // So we expect 5 messages: 1 system + 4 added
        Assert.Equal(5, aiState.ConversationHistory.Count);
        
        // Test message content and roles (index 0 is system message added during init)
        Assert.Equal(Aevatar.Agents.AI.AevatarChatRole.System, aiState.ConversationHistory[0].Role);
        Assert.Equal("Hello AI", aiState.ConversationHistory[1].Content);
        Assert.Equal(Aevatar.Agents.AI.AevatarChatRole.User, aiState.ConversationHistory[1].Role);
        
        Assert.Equal("Hello! How can I help you?", aiState.ConversationHistory[2].Content);
        Assert.Equal(Aevatar.Agents.AI.AevatarChatRole.Assistant, aiState.ConversationHistory[2].Role);
        
        // Test GetChatMessages conversion (includes system message)
        var chatMessages = agent.GetChatMessages();
        Assert.Equal(5, chatMessages.Count); // 1 system + 4 messages
        Assert.Equal(ChatRole.System, chatMessages[0].Role);
        Assert.Equal(ChatRole.User, chatMessages[1].Role);
        Assert.Equal(ChatRole.Assistant, chatMessages[2].Role);
        
        _output.WriteLine($"Conversation history contains {aiState.ConversationHistory.Count} messages");
    }

    /// <summary>
    /// 测试系统提示词添加
    /// </summary>
    [Fact]
    public void Test_SystemPrompt_In_Conversation()
    {
        // Arrange
        var agent = new TestMEAIAgent(_mockChatClient.Object, _mockLogger.Object);
        var aiState = agent.GetTestAIState();

        // Act - Add user message (system message already added during initialization)
        aiState.AddUserMessage("Hello", 100);

        // Assert - Should have system message from init + user message
        Assert.Equal(2, aiState.ConversationHistory.Count);
        Assert.Equal(Aevatar.Agents.AI.AevatarChatRole.System, aiState.ConversationHistory[0].Role);
        Assert.Equal(agent.SystemPrompt, aiState.ConversationHistory[0].Content);
        
        // Test conversion to Microsoft.Extensions.AI format
        var chatMessages = agent.GetChatMessages();
        Assert.Equal(ChatRole.System, chatMessages[0].Role);
        Assert.Equal(agent.SystemPrompt, chatMessages[0].Text);
    }

    /// <summary>
    /// 测试对话历史限制
    /// </summary>
    [Fact]
    public void Test_ConversationHistory_MaxLimit()
    {
        // Arrange
        var agent = new TestMEAIAgent(_mockChatClient.Object, _mockLogger.Object);
        var aiState = agent.GetTestAIState();
        const int maxHistory = 3;

        // Act - Add more messages than max limit
        aiState.AddUserMessage("Message 1", maxHistory);
        aiState.AddAssistantMessage("Response 1", maxHistory);
        aiState.AddUserMessage("Message 2", maxHistory);
        aiState.AddAssistantMessage("Response 2", maxHistory);
        aiState.AddUserMessage("Message 3", maxHistory);

        // Assert - Should keep only last maxHistory messages
        Assert.True(aiState.ConversationHistory.Count <= maxHistory);
        
        // The most recent messages should be kept
        var lastMessage = aiState.ConversationHistory.Last();
        Assert.Equal("Message 3", lastMessage.Content);
        
        _output.WriteLine($"History limited to {aiState.ConversationHistory.Count} messages (max: {maxHistory})");
    }

    /// <summary>
    /// 测试清空对话历史
    /// </summary>
    [Fact]
    public void Test_ClearConversationHistory()
    {
        // Arrange
        var agent = new TestMEAIAgent(_mockChatClient.Object, _mockLogger.Object);
        var aiState = agent.GetTestAIState();

        // Add some messages (system message was already added during initialization)
        aiState.AddUserMessage("Hello", 100);
        aiState.AddAssistantMessage("Hi there!", 100);
        Assert.Equal(3, aiState.ConversationHistory.Count); // 1 system + 2 added

        // Act - Clear the conversation history
        aiState.ConversationHistory.Clear();

        // Assert
        Assert.Empty(aiState.ConversationHistory);
        
        var chatMessages = agent.GetChatMessages();
        Assert.Empty(chatMessages);
    }

    /// <summary>
    /// 测试获取最近的对话历史
    /// </summary>
    [Fact]
    public void Test_GetRecentHistory()
    {
        // Arrange
        var agent = new TestMEAIAgent(_mockChatClient.Object, _mockLogger.Object);
        var aiState = agent.GetTestAIState();

        // Add multiple messages
        for (int i = 1; i <= 10; i++)
        {
            aiState.AddUserMessage($"Message {i}", 100);
            aiState.AddAssistantMessage($"Response {i}", 100);
        }

        // Act
        var recentHistory = aiState.GetRecentHistory(3);

        // Assert
        Assert.Equal(3, recentHistory.Count);
        
        // Should get the last 3 messages
        Assert.Equal("Response 9", recentHistory[0].Content);
        Assert.Equal("Message 10", recentHistory[1].Content);
        Assert.Equal("Response 10", recentHistory[2].Content);
    }

    /// <summary>
    /// 测试ChatClient模拟
    /// </summary>
    [Fact]
    public async Task Test_ChatClient_Integration()
    {
        // Arrange
        var expectedMessage = new ChatMessage(ChatRole.Assistant, "This is a test response");
        var messages = new List<ChatMessage> { expectedMessage };
        var chatResponse = new Microsoft.Extensions.AI.ChatResponse(messages);
        
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        var agent = new TestMEAIAgent(_mockChatClient.Object, _mockLogger.Object);

        // Act
        var inputMessages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "Hello")
        };
        
        var result = await _mockChatClient.Object.GetResponseAsync(inputMessages, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Messages);
        Assert.NotEmpty(result.Messages);
        Assert.Equal("This is a test response", result.Text);
        var lastMessage = result.Messages.Last();
        Assert.Equal(ChatRole.Assistant, lastMessage.Role);
        
        _output.WriteLine($"Chat response returned: {result.Text}");
    }

    /// <summary>
    /// 测试配置初始化
    /// </summary>
    [Fact]
    public void Test_MEAIConfiguration()
    {
        // Arrange
        var config = new MEAIConfiguration
        {
            Provider = "azure",
            Model = "gpt-4",
            Temperature = 0.7,
            MaxTokens = 2000,
            DeploymentName = "my-deployment",
            Endpoint = "https://test.openai.azure.com",
            ApiKey = "test-key"
        };

        // Act & Assert
        Assert.Equal("azure", config.Provider);
        Assert.Equal("gpt-4", config.Model);
        Assert.Equal(0.7, config.Temperature);
        Assert.Equal(2000, config.MaxTokens);
        Assert.Equal("my-deployment", config.DeploymentName);
        Assert.Equal("https://test.openai.azure.com", config.Endpoint);
        Assert.Equal("test-key", config.ApiKey);
    }
}

/// <summary>
/// 测试用的MEAI Agent实现
/// </summary>
public class TestMEAIAgent : MEAIGAgentBase<Aevatar.Agents.AI.AevatarAIAgentState>
{
    private readonly Aevatar.Agents.AI.AevatarAIAgentState _aiState = new Aevatar.Agents.AI.AevatarAIAgentState();
    
    public override string SystemPrompt => "You are a test MEAI assistant";

    public TestMEAIAgent(IChatClient chatClient, ILogger<TestMEAIAgent>? logger = null)
        : base(chatClient, logger)
    {
        _aiState.AgentId = Id.ToString();
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Test MEAI agent for unit tests");
    }

    protected override Aevatar.Agents.AI.AevatarAIAgentState GetAIState()
    {
        return _aiState;
    }

    public Aevatar.Agents.AI.AevatarAIAgentState GetTestAIState()
    {
        return _aiState;
    }
}

