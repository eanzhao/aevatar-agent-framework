using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Extensions;
// Models are now in Aevatar.Agents.AI namespace from protobuf
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.Core.Tests.Messages;
using FluentAssertions;
using Moq;

namespace Aevatar.Agents.AI.Core.Tests.Agents;

// Test implementation of AIGAgentBase
public class TestAIAgent : AIGAgentBase<TestAIAgentState>
{
    private readonly AevatarAIAgentState _aiState = new AevatarAIAgentState();
    
    public override string SystemPrompt => "You are a test AI assistant.";
    
    public TestAIAgent() : base()
    {
        _aiState.AgentId = Id.ToString();
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Test AI Agent for unit testing");
    }

    public AIAgentConfiguration GetConfiguration() => Configuration;
    
    protected override AevatarAIAgentState GetAIState()
    {
        return _aiState;
    }
    
    protected override IAevatarLLMProvider CreateLLMProvider()
    {
        var mockProvider = new Mock<IAevatarLLMProvider>();
        mockProvider.Setup(p => p.GenerateAsync(It.IsAny<AevatarLLMRequest>(), CancellationToken.None))
            .ReturnsAsync(new AevatarLLMResponse { Content = "Test response" });
        return mockProvider.Object;
    }

    // Expose protected methods for testing
    public Task<ChatResponse> TestChatAsync(ChatRequest request, CancellationToken ct = default)
        => ChatAsync(request);

    public Task TestHandleChatRequestEvent(ChatRequestEvent evt, CancellationToken ct = default)
        => HandleChatRequestEvent(evt);
    
    public AevatarAIAgentState AIState => _aiState;
}

public class AIGAgentBaseTests : IDisposable
{
    private readonly TestAIAgent _sut;
    private readonly Mock<IEventPublisher> _mockEventPublisher;

    public AIGAgentBaseTests()
    {
        _sut = new TestAIAgent();
        _mockEventPublisher = new Mock<Aevatar.Agents.Abstractions.IEventPublisher>();
        // _sut.SetEventPublisher(_mockEventPublisher.Object); // This method doesn't exist, we'll need to verify event publishing differently
    }

    [Fact]
    public void SystemPrompt_Should_Return_Configured_Value()
    {
        // Assert
        _sut.SystemPrompt.Should().Be("You are a test AI assistant.");
    }

    [Fact]
    public void Configuration_Should_Be_Initialized_With_Default_Values()
    {
        // Act
        var config = _sut.GetConfiguration();

        // Assert
        config.Should().NotBeNull();
        config.Model.Should().NotBeNullOrEmpty();
        config.Temperature.Should().BeInRange(0.0, 1.0);
        config.MaxTokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ConversationHistory_Should_Be_Initialized()
    {
        // Assert
        _sut.ConversationHistory.Should().NotBeNull();
        _sut.ConversationHistory.Count.Should().Be(0);
    }

    [Fact]
    public void LLMProvider_Should_Be_Initialized()
    {
        // Assert
        _sut.LLMProvider.Should().NotBeNull();
    }

    [Fact]
    public async Task ChatAsync_Should_Return_Response_From_LLM()
    {
        // Arrange
        var request = new ChatRequest
        {
            RequestId = "test-1",
            Message = "Hello, AI!",
            Context = { new Dictionary<string, string> { ["systemPrompt"] = "You are helpful." } }
        };

        // Act
        var response = await _sut.TestChatAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.RequestId.Should().Be("test-1");
        response.Content.Should().Be("Test response");
    }

    [Fact]
    public async Task ChatAsync_Should_Add_Messages_To_Conversation_History()
    {
        // Arrange
        var request = new ChatRequest
        {
            RequestId = "test-1",
            Message = "Hello, AI!"
        };

        // Act
        await _sut.TestChatAsync(request);

        // Assert
        _sut.ConversationHistory.Count.Should().Be(2); // User + Assistant
        var history = _sut.ConversationHistory;
        history[0].Role.Should().Be(Aevatar.Agents.AI.AevatarChatRole.User);
        history[0].Content.Should().Be("Hello, AI!");
        history[1].Role.Should().Be(Aevatar.Agents.AI.AevatarChatRole.Assistant);
        history[1].Content.Should().Be("Test response");
    }

    [Fact]
    public async Task HandleChatRequestEvent_Should_Process_Event_And_Publish_Response()
    {
        // Arrange
        var chatEvent = new ChatRequestEvent
        {
            RequestId = "req-1",
            Message = "Process this message",
            Context = { ["key"] = "value" }
        };

        ChatResponseEvent? publishedEvent = null;
        _mockEventPublisher.Setup(p => p.PublishEventAsync(
                It.IsAny<Google.Protobuf.IMessage>(), 
                It.IsAny<EventDirection>(), 
                It.IsAny<CancellationToken>()))
            .Callback<Google.Protobuf.IMessage, EventDirection, CancellationToken>((msg, dir, ct) =>
            {
                publishedEvent = msg as ChatResponseEvent;
            })
            .ReturnsAsync(string.Empty);

        // Act
        await _sut.TestHandleChatRequestEvent(chatEvent);

        // Assert
        publishedEvent.Should().NotBeNull();
        publishedEvent!.RequestId.Should().Be("req-1");
        publishedEvent.Content.Should().Be("Test response");
        publishedEvent.ProcessingMode.Should().Be((int)AevatarAIProcessingMode.Standard);
    }

    [Fact]
    public async Task ChatAsync_Should_Use_SystemPrompt_From_Agent_When_Not_Provided_In_Request()
    {
        // Arrange
        var mockProvider = new Mock<IAevatarLLMProvider>();
        AevatarLLMRequest? capturedRequest = null;
        mockProvider.Setup(p => p.GenerateAsync(It.IsAny<AevatarLLMRequest>(), CancellationToken.None))
            .Callback<AevatarLLMRequest>(req => capturedRequest = req)
            .ReturnsAsync(new AevatarLLMResponse { Content = "Response" });

        // Create agent with mock provider
        var agent = new TestAIAgentWithMockProvider(mockProvider.Object);

        var request = new ChatRequest
        {
            RequestId = "test-1",
            Message = "Hello"
            // SystemPrompt not provided in context
        };

        // Act
        await agent.TestChatAsync(request);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.SystemPrompt.Should().Be("You are a test AI assistant.");
    }

    [Fact]
    public async Task ChatAsync_Should_Include_Conversation_History_In_LLM_Request()
    {
        // Arrange
        var mockProvider = new Mock<IAevatarLLMProvider>();
        AevatarLLMRequest? capturedRequest = null;
        mockProvider.Setup(p => p.GenerateAsync(It.IsAny<AevatarLLMRequest>(), CancellationToken.None))
            .Callback<AevatarLLMRequest>(req => capturedRequest = req)
            .ReturnsAsync(new AevatarLLMResponse { Content = "Response" });

        var agent = new TestAIAgentWithMockProvider(mockProvider.Object);

        // Add some conversation history
        agent.AIState.AddUserMessage("Previous question", agent.Configuration.MaxHistory);
        agent.AIState.AddAssistantMessage("Previous answer", agent.Configuration.MaxHistory);

        var request = new ChatRequest
        {
            Message = "New question"
        };

        // Act
        await agent.TestChatAsync(request);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Messages.Should().HaveCountGreaterThan(0);
        capturedRequest.Messages.Any(m => m.Content == "Previous question").Should().BeTrue();
        capturedRequest.Messages.Any(m => m.Content == "Previous answer").Should().BeTrue();
    }

    [Fact]
    public void ConfigureAI_Should_Allow_Customization_Of_Configuration()
    {
        // Arrange & Act
        var agent = new CustomConfigAIAgent();
        var config = agent.GetConfiguration();

        // Assert
        config.Model.Should().Be("custom-model");
        config.Temperature.Should().Be(0.5);
        config.MaxTokens.Should().Be(2000);
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    // Helper test class with custom mock provider
    private class TestAIAgentWithMockProvider : AIGAgentBase<TestAIAgentState>
    {
        private readonly IAevatarLLMProvider _mockProvider;
        private readonly AevatarAIAgentState _aiState = new AevatarAIAgentState();

        public TestAIAgentWithMockProvider(IAevatarLLMProvider mockProvider) : base()
        {
            _mockProvider = mockProvider;
            _aiState.AgentId = Id.ToString();
        }

        public override string SystemPrompt => "You are a test AI assistant.";

        public override Task<string> GetDescriptionAsync()
        {
            return Task.FromResult("Test AI Agent with mock provider");
        }
        
        protected override AevatarAIAgentState GetAIState()
        {
            return _aiState;
        }

        protected override IAevatarLLMProvider CreateLLMProvider() => _mockProvider;

        public Task<ChatResponse> TestChatAsync(ChatRequest request, CancellationToken ct = default)
            => ChatAsync(request);
        
        public AevatarAIAgentState AIState => _aiState;
    }

    // Helper test class with custom configuration
    private class CustomConfigAIAgent : AIGAgentBase<TestAIAgentState>
    {
        private readonly AevatarAIAgentState _aiState = new AevatarAIAgentState();
        
        public override string SystemPrompt => "Custom prompt";

        public override Task<string> GetDescriptionAsync()
        {
            return Task.FromResult("Custom config AI agent");
        }
        
        protected override AevatarAIAgentState GetAIState()
        {
            return _aiState;
        }

        protected override void ConfigureAI(AIAgentConfiguration config)
        {
            config.Model = "custom-model";
            config.Temperature = 0.5;
            config.MaxTokens = 2000;
            config.SystemPrompt = "Custom system prompt";
        }

        public AIAgentConfiguration GetConfiguration() => Configuration;
    }
}

// Additional tests for ChatRequest and ChatResponse models
public class ChatModelTests
{
    [Fact]
    public void ChatRequest_Should_Initialize_With_Default_Values()
    {
        // Arrange & Act
        var request = new ChatRequest();

        // Assert
        request.RequestId.Should().NotBeNullOrEmpty();
        request.Message.Should().BeEmpty();
        request.Context.Should().NotBeNull();
        request.Context.Should().BeEmpty();
    }

    [Fact]
    public void ChatResponse_Should_Initialize_With_Default_Values()
    {
        // Arrange & Act
        var response = new ChatResponse();

        // Assert
        response.RequestId.Should().BeEmpty();
        response.Content.Should().BeEmpty();
        response.ProcessingSteps.Should().BeEmpty();
    }

    [Fact]
    public void TokenUsage_Should_Be_Set_Correctly()
    {
        // Arrange & Act
        var usage = new AevatarTokenUsage
        {
            PromptTokens = 100,
            CompletionTokens = 50,
            TotalTokens = 150
        };

        // Assert
        usage.TotalTokens.Should().Be(150);
        usage.PromptTokens.Should().Be(100);
        usage.CompletionTokens.Should().Be(50);
    }
}
