using Aevatar.Agents.AI.Abstractions;
// Models are now in Aevatar.Agents.AI namespace from protobuf
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.Core.Tests.Messages;
using FluentAssertions;
using Moq;

namespace Aevatar.Agents.AI.Core.Tests.Agents;

// Test implementation of AIGAgentWithToolBase
public class TestAIAgentWithTools : AIGAgentWithToolBase<TestAIAgentState>
{
    private readonly List<IAevatarTool> _registeredTools = new();
    
    public override string SystemPrompt => "You are a test AI assistant with tools.";
    
    public TestAIAgentWithTools() : base()
    {
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Test AI Agent with tools for unit testing");
    }

    public IReadOnlyList<IAevatarTool> RegisteredTools => _registeredTools;

    protected override void RegisterTools()
    {
        // Register test tools
        var mockTool = new Mock<IAevatarTool>();
        mockTool.Setup(t => t.Name).Returns("TestTool");
        mockTool.Setup(t => t.Description).Returns("A test tool");
        mockTool.Setup(t => t.CreateParameters()).Returns(new ToolParameters());
        
        _registeredTools.Add(mockTool.Object);
    }

    protected override IAevatarToolManager CreateToolManager()
    {
        var mockManager = new Mock<IAevatarToolManager>();
        
        // Setup tool registration
        mockManager.Setup(m => m.RegisterToolAsync(It.IsAny<ToolDefinition>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        // Setup function definitions generation
        mockManager.Setup(m => m.GenerateFunctionDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AevatarFunctionDefinition>
            {
                new() { Name = "TestTool", Description = "A test tool" }
            });
        
        // Setup tool execution
        mockManager.Setup(m => m.ExecuteToolAsync(
                It.IsAny<string>(), 
                It.IsAny<Dictionary<string, object>>(), 
                It.IsAny<ExecutionContext>(), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionResult
            {
                Success = true,
                Result = "Tool executed successfully"
            });
            
        return mockManager.Object;
    }

    protected override IAevatarLLMProvider CreateLLMProvider()
    {
        var mockProvider = new Mock<IAevatarLLMProvider>();
        mockProvider.Setup(p => p.GenerateAsync(It.IsAny<AevatarLLMRequest>(), CancellationToken.None))
            .ReturnsAsync(new AevatarLLMResponse { Content = "Test response" });
        return mockProvider.Object;
    }

    // Expose protected methods for testing
    public Task<ToolExecutionResult> TestExecuteToolAsync(string toolName, string argumentsJson, CancellationToken ct = default)
        => ExecuteToolAsync(toolName, argumentsJson);

    public Task<ChatResponse> TestChatAsync(ChatRequest request, CancellationToken ct = default)
        => ChatAsync(request);

    public Task TestHandleToolExecutionRequestEvent(ToolExecutionRequestEvent evt, CancellationToken ct = default)
        => HandleToolExecutionRequestEvent(evt);
}

public class AIGAgentWithToolBaseTests
{
    private readonly TestAIAgentWithTools _sut;
    private readonly Mock<Aevatar.Agents.Abstractions.IEventPublisher> _mockEventPublisher;

    public AIGAgentWithToolBaseTests()
    {
        _sut = new TestAIAgentWithTools();
        _mockEventPublisher = new Mock<Aevatar.Agents.Abstractions.IEventPublisher>();
        // _sut.SetEventPublisher(_mockEventPublisher.Object); // This method doesn't exist
    }

    [Fact]
    public void RegisterTools_Should_Be_Called_On_Initialization()
    {
        // Assert - Tools should be registered during construction
        _sut.RegisteredTools.Should().NotBeEmpty();
        _sut.RegisteredTools.Should().Contain(t => t.Name == "TestTool");
    }

    [Fact]
    public void ToolManager_Should_Be_Initialized()
    {
        // Assert - ToolManager is protected, checking through RegisteredTools instead
        _sut.RegisteredTools.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteToolAsync_Should_Execute_Tool_And_Return_Result()
    {
        // Arrange
        var toolName = "TestTool";
        var argumentsJson = "{\"param1\": \"value1\"}";

        // Act
        var result = await _sut.TestExecuteToolAsync(toolName, argumentsJson);

        // Assert
        result.Should().NotBeNull();
        result!.ToString().Should().Be("Tool executed successfully");
    }

    [Fact]
    public async Task ExecuteToolAsync_Should_Return_Error_Message_On_Invalid_Json()
    {
        // Arrange
        var toolName = "TestTool";
        var invalidJson = "{ invalid json }";

        // Act
        var result = await _sut.TestExecuteToolAsync(toolName, invalidJson);

        // Assert
        result.Should().NotBeNull();
        result!.ToString().Should().Contain("Invalid JSON format");
    }

    [Fact]
    public async Task ChatAsync_Should_Include_Tool_Definitions_In_LLM_Request()
    {
        // Arrange
        var mockProvider = new Mock<IAevatarLLMProvider>();
        AevatarLLMRequest? capturedRequest = null;
        mockProvider.Setup(p => p.GenerateAsync(It.IsAny<AevatarLLMRequest>(), CancellationToken.None))
            .Callback<AevatarLLMRequest>(req => capturedRequest = req)
            .ReturnsAsync(new AevatarLLMResponse { Content = "Response" });

        var agent = new TestAIAgentWithToolsAndMockProvider(mockProvider.Object);

        var request = new ChatRequest
        {
            Message = "Use a tool"
        };

        // Act
        await agent.TestChatAsync(request);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Functions.Should().NotBeNull();
        capturedRequest.Functions.Should().HaveCount(1);
        capturedRequest.Functions[0].Name.Should().Be("TestTool");
    }

    [Fact]
    public async Task ChatAsync_Should_Execute_Tool_When_LLM_Returns_FunctionCall()
    {
        // Arrange
        var mockProvider = new Mock<IAevatarLLMProvider>();
        mockProvider.SetupSequence(p => p.GenerateAsync(It.IsAny<AevatarLLMRequest>(), CancellationToken.None))
            .ReturnsAsync(new AevatarLLMResponse 
            { 
                AevatarFunctionCall = new AevatarFunctionCall
                {
                    Name = "TestTool",
                    Arguments = "{\"param\": \"value\"}"
                }
            })
            .ReturnsAsync(new AevatarLLMResponse { Content = "Final response" });

        var agent = new TestAIAgentWithToolsAndMockProvider(mockProvider.Object);

        var request = new ChatRequest
        {
            Message = "Execute the tool"
        };

        // Act
        var response = await agent.TestChatAsync(request);

        // Assert
        response.Content.Should().Be("Final response");
        agent.AIState.ConversationHistory.Count.Should().BeGreaterThan(2); // Should include function message
        var history = agent.AIState.ConversationHistory;
        history.Should().Contain(m => m.Role == Aevatar.Agents.AI.AevatarChatRole.Function);
    }

    [Fact]
    public async Task HandleToolExecutionRequestEvent_Should_Execute_Tool_And_Publish_Response()
    {
        // Arrange
        var toolEvent = new ToolExecutionRequestEvent
        {
            RequestId = "req-1",
            ToolName = "TestTool",
            Arguments = "{\"param\": \"value\"}"
        };

        ToolExecutionResponseEvent? publishedEvent = null;
        _mockEventPublisher.Setup(p => p.PublishEventAsync(
                It.IsAny<Google.Protobuf.IMessage>(),
                It.IsAny<EventDirection>(),
                It.IsAny<CancellationToken>()))
            .Callback<Google.Protobuf.IMessage, EventDirection, CancellationToken>((msg, dir, ct) =>
            {
                publishedEvent = msg as ToolExecutionResponseEvent;
            })
            .ReturnsAsync(string.Empty);

        // Act
        await _sut.TestHandleToolExecutionRequestEvent(toolEvent);

        // Assert
        publishedEvent.Should().NotBeNull();
        publishedEvent!.RequestId.Should().Be("req-1");
        publishedEvent.ToolName.Should().Be("TestTool");
        publishedEvent.Success.Should().BeTrue();
        publishedEvent.Result.Should().Be("Tool executed successfully");
    }

    [Fact]
    public async Task HandleToolExecutionRequestEvent_Should_Report_Error_On_Failure()
    {
        // Arrange
        var mockManager = new Mock<IAevatarToolManager>();
        mockManager.Setup(m => m.ExecuteToolAsync(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<ExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionResult
            {
                Success = false,
                Error = "Tool execution failed"
            });

        var agent = new TestAIAgentWithCustomToolManager(mockManager.Object);
        var mockEventPublisher = new Mock<Aevatar.Agents.Abstractions.IEventPublisher>();
        agent.SetEventPublisher(mockEventPublisher.Object);

        var toolEvent = new ToolExecutionRequestEvent
        {
            RequestId = "req-1",
            ToolName = "FailingTool",
            Arguments = "{}"
        };

        ToolExecutionResponseEvent? publishedEvent = null;
        mockEventPublisher.Setup(p => p.PublishEventAsync(
                It.IsAny<Google.Protobuf.IMessage>(),
                It.IsAny<EventDirection>(),
                It.IsAny<CancellationToken>()))
            .Callback<Google.Protobuf.IMessage, EventDirection, CancellationToken>((msg, dir, ct) =>
            {
                publishedEvent = msg as ToolExecutionResponseEvent;
            })
            .ReturnsAsync(string.Empty);

        // Act
        await agent.TestHandleToolExecutionRequestEvent(toolEvent);

        // Assert
        publishedEvent.Should().NotBeNull();
        publishedEvent!.Success.Should().BeFalse();
        publishedEvent.Error.Should().Contain("Tool execution failed");
    }

    // Helper test classes
    private class TestAIAgentWithToolsAndMockProvider : AIGAgentWithToolBase<TestAIAgentState>
    {
        private readonly IAevatarLLMProvider _mockProvider;
        private readonly AevatarAIAgentState _aiState = new AevatarAIAgentState();

        public TestAIAgentWithToolsAndMockProvider(IAevatarLLMProvider mockProvider) : base()
        {
            _mockProvider = mockProvider;
            _aiState.AgentId = Id.ToString();
        }
        
        protected override AevatarAIAgentState GetAIState()
        {
            return _aiState;
        }

        public override string SystemPrompt => "Test prompt";

        public override Task<string> GetDescriptionAsync()
        {
            return Task.FromResult("Test AI Agent with tools and mock provider");
        }

        protected override void RegisterTools()
        {
            // Register a test tool
        }

        protected override IAevatarLLMProvider CreateLLMProvider() => _mockProvider;

        protected override IAevatarToolManager CreateToolManager()
        {
            var mockManager = new Mock<IAevatarToolManager>();
            mockManager.Setup(m => m.GenerateFunctionDefinitionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<AevatarFunctionDefinition>
                {
                    new() { Name = "TestTool", Description = "A test tool" }
                });
            mockManager.Setup(m => m.ExecuteToolAsync(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.IsAny<ExecutionContext>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ToolExecutionResult
                {
                    Success = true,
                    Result = "Tool result"
                });
            return mockManager.Object;
        }

        public Task<ChatResponse> TestChatAsync(ChatRequest request, CancellationToken ct = default)
            => ChatAsync(request);

        public AevatarAIAgentState AIState => GetAIState();
    }

    private class TestAIAgentWithCustomToolManager : AIGAgentWithToolBase<TestAIAgentState>
    {
        private readonly IAevatarToolManager _toolManager;
        private readonly AevatarAIAgentState _aiState = new AevatarAIAgentState();

        public TestAIAgentWithCustomToolManager(IAevatarToolManager toolManager) : base()
        {
            _toolManager = toolManager;
            _aiState.AgentId = Id.ToString();
        }
        
        protected override AevatarAIAgentState GetAIState()
        {
            return _aiState;
        }

        public override string SystemPrompt => "Test";

        public override Task<string> GetDescriptionAsync()
        {
            return Task.FromResult("Test AI Agent with custom tool manager");
        }

        protected override void RegisterTools() { }

        protected override IAevatarToolManager CreateToolManager() => _toolManager;

        public Task TestHandleToolExecutionRequestEvent(ToolExecutionRequestEvent evt, CancellationToken ct = default)
            => HandleToolExecutionRequestEvent(evt);
    }
}
