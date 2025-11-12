using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Tools;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.Core.Strategies;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Aevatar.Agents.AI.Core.Tests.Strategies;

public class StandardProcessingStrategyTests
{
    private readonly StandardProcessingStrategy _sut;
    private readonly Mock<IAevatarLLMProvider> _mockLLMProvider;
    private readonly Mock<IAevatarToolManager> _mockToolManager;
    
    public StandardProcessingStrategyTests()
    {
        _sut = new StandardProcessingStrategy();
        _mockLLMProvider = new Mock<IAevatarLLMProvider>();
        _mockToolManager = new Mock<IAevatarToolManager>();
    }

    [Fact]
    public void Name_Should_Return_Standard_Processing()
    {
        // Assert
        _sut.Name.Should().Be("Standard Processing");
    }

    [Fact]
    public void Mode_Should_Return_Standard()
    {
        // Assert
        _sut.Mode.Should().Be(AevatarAIProcessingMode.Standard);
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        // Assert
        _sut.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CanHandle_Should_Return_True_For_Standard_Requests()
    {
        // Arrange
        var context = new AevatarAIContext
        {
            Question = "Test question"
        };

        // Act
        var result = _sut.CanHandle(context);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void EstimateComplexity_Should_Return_Low_Complexity()
    {
        // Arrange
        var context = new AevatarAIContext
        {
            Question = "Test question"
        };

        // Act
        var complexity = _sut.EstimateComplexity(context);

        // Assert
        complexity.Should().BeInRange(0.0, 0.3);
    }

    [Fact]
    public void ValidateRequirements_Should_Return_True_When_LLMProvider_Present()
    {
        // Arrange
        var dependencies = new AevatarAIStrategyDependencies
        {
            LLMProvider = _mockLLMProvider.Object,
            Configuration = new AevatarAIAgentConfiguration(),
            Logger = NullLogger<StandardProcessingStrategy>.Instance,
            AgentId = "test"
        };

        // Act
        var result = _sut.ValidateRequirements(dependencies);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateRequirements_Should_Return_False_When_LLMProvider_Missing()
    {
        // Arrange
        var deps = new AevatarAIStrategyDependencies
        {
            LLMProvider = null!,
            Configuration = new AevatarAIAgentConfiguration(),
            Logger = NullLogger<StandardProcessingStrategy>.Instance,
            AgentId = "test"
        };

        // Act
        var result = _sut.ValidateRequirements(deps);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessAsync_Should_Call_LLMProvider_With_Correct_Request()
    {
        // Arrange
        var context = new AevatarAIContext
        {
            AgentId = "test-agent",
            Question = "What is the weather?",
            SystemPrompt = "You are a weather assistant."
        };

        var llmResponse = new AevatarLLMResponse
        {
            Content = "The weather is sunny.",
            Usage = new AevatarTokenUsage { TotalTokens = 10 }
        };

        _mockLLMProvider.Setup(p => p.GenerateAsync(It.IsAny<AevatarLLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        _mockToolManager.Setup(t => t.GenerateFunctionDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AevatarFunctionDefinition>());

        var dependencies = new AevatarAIStrategyDependencies
        {
            LLMProvider = _mockLLMProvider.Object,
            ToolManager = _mockToolManager.Object,
            Configuration = new AevatarAIAgentConfiguration(),
            Logger = NullLogger<StandardProcessingStrategy>.Instance,
            AgentId = "test-agent"
        };

        // Act
        var result = await _sut.ProcessAsync(context, null, dependencies);

        // Assert
        result.Should().Be("The weather is sunny.");
        _mockLLMProvider.Verify(p => p.GenerateAsync(
            It.Is<AevatarLLMRequest>(r => 
                r.SystemPrompt == "You are a weather assistant." &&
                r.UserPrompt == "What is the weather?"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_Should_Include_Conversation_History()
    {
        // Arrange
        var context = new AevatarAIContext
        {
            Question = "And today?",
            ConversationHistory = new List<AevatarConversationEntry>
            {
                new() { Role = "user", Content = "What was the weather yesterday?" },
                new() { Role = "assistant", Content = "Yesterday was rainy." }
            }
        };

        var llmResponse = new AevatarLLMResponse
        {
            Content = "Today is sunny."
        };

        _mockLLMProvider.Setup(p => p.GenerateAsync(It.IsAny<AevatarLLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        _mockToolManager.Setup(t => t.GenerateFunctionDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AevatarFunctionDefinition>());

        var dependencies = new AevatarAIStrategyDependencies
        {
            LLMProvider = _mockLLMProvider.Object,
            ToolManager = _mockToolManager.Object,
            Configuration = new AevatarAIAgentConfiguration(),
            Logger = NullLogger<StandardProcessingStrategy>.Instance,
            AgentId = "test-agent"
        };

        // Act
        var result = await _sut.ProcessAsync(context, null, dependencies);

        // Assert
        result.Should().Be("Today is sunny.");
        _mockLLMProvider.Verify(p => p.GenerateAsync(
            It.Is<AevatarLLMRequest>(r => r.Messages.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_Should_Execute_Tool_When_LLM_Returns_FunctionCall()
    {
        // Arrange
        var context = new AevatarAIContext
        {
            Question = "Get the weather for Seattle"
        };

        var llmResponse = new AevatarLLMResponse
        {
            Content = "",
            AevatarFunctionCall = new AevatarFunctionCall
            {
                Name = "GetWeather",
                Arguments = "{\"city\":\"Seattle\"}"
            }
        };

        _mockLLMProvider.Setup(p => p.GenerateAsync(It.IsAny<AevatarLLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        _mockToolManager.Setup(t => t.GenerateFunctionDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AevatarFunctionDefinition>
            {
                new() { Name = "GetWeather", Description = "Get weather for a city" }
            });

        var toolExecuted = false;
        var dependencies = new AevatarAIStrategyDependencies
        {
            LLMProvider = _mockLLMProvider.Object,
            ToolManager = _mockToolManager.Object,
            Configuration = new AevatarAIAgentConfiguration(),
            Logger = NullLogger<StandardProcessingStrategy>.Instance,
            AgentId = "test-agent",
            ExecuteToolCallback = async (name, args, ct) =>
            {
                toolExecuted = true;
                name.Should().Be("GetWeather");
                args.Should().ContainKey("city");
                args.Should().ContainValue("Seattle");
                return "Sunny, 72°F";
            }
        };

        // Act
        var result = await _sut.ProcessAsync(context, null, dependencies);

        // Assert
        result.Should().Be("Sunny, 72°F");
        toolExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessAsync_Should_Return_Error_Message_On_Exception()
    {
        // Arrange
        var context = new AevatarAIContext
        {
            Question = "Test"
        };

        _mockLLMProvider.Setup(p => p.GenerateAsync(It.IsAny<AevatarLLMRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("LLM error"));

        var dependencies = new AevatarAIStrategyDependencies
        {
            LLMProvider = _mockLLMProvider.Object,
            Configuration = new AevatarAIAgentConfiguration(),
            Logger = NullLogger<StandardProcessingStrategy>.Instance,
            AgentId = "test-agent"
        };

        // Act
        var result = await _sut.ProcessAsync(context, null, dependencies);

        // Assert
        result.Should().Contain("I apologize");
        result.Should().Contain("error");
    }
}