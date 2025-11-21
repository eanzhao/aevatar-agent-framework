using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.WithTool;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Google.Protobuf;
using Moq;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.AI.Tests;

public class AIGAgentWithToolBaseTests
{
    private readonly Mock<IAevatarLLMProvider> _mockLLMProvider;
    private readonly Mock<IAevatarToolManager> _mockToolManager;
    private readonly Mock<IEventPublisher> _mockEventPublisher;
    private readonly TestAgent _agent;

    public AIGAgentWithToolBaseTests()
    {
        _mockLLMProvider = new Mock<IAevatarLLMProvider>();
        _mockToolManager = new Mock<IAevatarToolManager>();
        _mockEventPublisher = new Mock<IEventPublisher>();
        
        _agent = new TestAgent(_mockLLMProvider.Object, _mockToolManager.Object);
        _agent.SetEventPublisher(_mockEventPublisher.Object);
        
        // Setup default event publisher behavior
        _mockEventPublisher
            .Setup(p => p.PublishEventAsync(It.IsAny<IMessage>(), It.IsAny<EventDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-event-id");
    }

    [Fact]
    public async Task ChatAsync_ShouldAddMessagesToHistory()
    {
        // Arrange
        var request = new ChatRequest { Message = "Hello", RequestId = "req1" };
        var llmResponse = new AevatarLLMResponse { Content = "Hi there" };
        
        _mockLLMProvider
            .Setup(p => p.GenerateAsync(It.IsAny<AevatarLLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AevatarLLMResponse { Content = "Hello, user!" });

        _mockToolManager
            .Setup(m => m.GetAvailableToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ToolDefinition>());

        await _agent.InitializeAsync("test-provider");

        // Act
        var response = await _agent.ChatAsync(request);

        // Assert
        response.Content.ShouldBe("Hello, user!");
        
        // Verify History
        _agent.PublicState.History.Count.ShouldBe(2);
        _agent.PublicState.History[0].Role.ShouldBe(AevatarChatRole.User);
        _agent.PublicState.History[0].Content.ShouldBe("Hello");
        _agent.PublicState.History[1].Role.ShouldBe(AevatarChatRole.Assistant);
        _agent.PublicState.History[1].Content.ShouldBe("Hello, user!");
    }

    [Fact]
    public async Task ChatAsync_WithToolCall_ShouldExecuteToolAndAddToHistory()
    {
        // Arrange
        var request = new ChatRequest { Message = "What time is it?", RequestId = "req2" };
        
        // First response: Tool Call
        var toolCallResponse = new AevatarLLMResponse 
        { 
            Content = null,
            AevatarFunctionCall = new AevatarFunctionCall 
            { 
                Name = "GetTime", 
                Arguments = "{}" 
            } 
        };

        // Second response: Final Answer
        var finalResponse = new AevatarLLMResponse { Content = "It is 12:00" };

        _mockLLMProvider
            .SetupSequence(p => p.GenerateAsync(It.IsAny<AevatarLLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolCallResponse)
            .ReturnsAsync(finalResponse);

        _mockToolManager
            .Setup(m => m.ExecuteToolAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionResult { IsSuccess = true, Content = "12:00" });

        _mockToolManager
            .Setup(m => m.GetAvailableToolsAsync())
            .ReturnsAsync(new List<ToolDefinition> { new ToolDefinition { Name = "GetTime" } });

        await _agent.InitializeAsync("test-provider");

        // Act
        var response = await _agent.ChatAsync(request);

        // Assert
        response.Content.ShouldBe("It is 12:00");
        response.ToolCalled.ShouldBeTrue();
        response.ToolCall.ToolName.ShouldBe("GetTime");
        response.ToolCall.Result.ShouldBe("12:00");

        // Verify History
        // 1. User: What time is it?
        // 2. Assistant: Tool Call (GetTime)
        // 3. Tool: Result (12:00)
        // 4. Assistant: It is 12:00
        _agent.PublicState.History.Count.ShouldBe(4);
        
        _agent.PublicState.History[0].Role.ShouldBe(AevatarChatRole.User);
        _agent.PublicState.History[0].Content.ShouldBe("What time is it?");
        
        _agent.PublicState.History[1].Role.ShouldBe(AevatarChatRole.Assistant);
        _agent.PublicState.History[1].ToolCalls.ShouldNotBeEmpty();
        _agent.PublicState.History[1].ToolCalls[0].ToolName.ShouldBe("GetTime");
        
        _agent.PublicState.History[2].Role.ShouldBe(AevatarChatRole.Tool);
        _agent.PublicState.History[2].Content.ShouldBe("12:00");
        _agent.PublicState.History[2].ToolResult.ShouldNotBeNull();
        _agent.PublicState.History[2].ToolResult.ToolName.ShouldBe("GetTime");
        
        _agent.PublicState.History[3].Role.ShouldBe(AevatarChatRole.Assistant);
        _agent.PublicState.History[3].Content.ShouldBe("It is 12:00");
    }

    // Test Agent Implementation
    public class TestAgent : AIGAgentWithToolBase<AevatarAIAgentState>
    {
        public AevatarAIAgentState PublicState => State;

        public void SetEventPublisher(IEventPublisher publisher)
        {
            EventPublisher = publisher;
        }

        public TestAgent(IAevatarLLMProvider llmProvider, IAevatarToolManager toolManager) 
            : base(llmProvider, toolManager)
        {
            // Initialize state manually for test
            // State and Config are initialized by base class
            
            // Mock initialization
            
            // Mock initialization
            // Reflection to set _isInitialized or just override InitializeAsync?
            // InitializeAsync calls InitializeStateAndConfigAsync which we can override or mock stores.
            // But base.InitializeAsync sets _llmProvider.
            // We passed llmProvider in constructor but base doesn't set it.
            // We need to set it.
            
            // Hack: Set _llmProvider via reflection or just assume InitializeAsync works if we mock factories?
            // But we passed llmProvider in constructor.
            // Wait, AIGAgentWithToolBase constructor:
            /*
            protected AIGAgentWithToolBase(
                IAevatarLLMProvider llmProvider,
                IAevatarToolManager toolManager,
                ILogger? logger = null)
            {
                _toolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
            }
            */
            // It does NOT set _llmProvider in base AIGAgentBase.
            // AIGAgentBase has `protected IAevatarLLMProvider? _llmProvider;`
            
            // So we need to set it.
            SetLLMProvider(llmProvider);
        }

        private void SetLLMProvider(IAevatarLLMProvider provider)
        {
            var field = typeof(AIGAgentBase).GetField("_llmProvider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(this, provider);
            
            var initField = typeof(AIGAgentBase).GetField("_isInitialized", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            initField?.SetValue(this, true);
        }

        public override Task InitializeAsync(string providerName, Action<AevatarAIAgentConfig>? configAI = null, CancellationToken cancellationToken = default)
        {
            // Skip base init logic that requires factories
            return Task.CompletedTask;
        }
    }
}
