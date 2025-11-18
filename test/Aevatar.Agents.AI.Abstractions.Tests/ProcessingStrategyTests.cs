using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.Core.Tests.Agents.AI;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.AI.Abstractions.Tests;

/// <summary>
/// Tests for AI Processing Strategies
/// </summary>
public class ProcessingStrategyTests
{
    private readonly Mock<ILogger> _mockLogger;

    public ProcessingStrategyTests()
    {
        _mockLogger = new Mock<ILogger>();
    }

    [Fact]
    public void Strategy_CanHandle_WithReasoningQuestion_ShouldReturnTrue()
    {
        // Arrange
        var strategy = new MockChainOfThoughtStrategy();
        var context = new AevatarAIContext
        {
            Question = "Why does water boil at 100°C?"
        };

        // Act
        var canHandle = strategy.CanHandle(context);

        // Assert
        canHandle.ShouldBeTrue();
    }

    [Fact]
    public void Strategy_EstimateComplexity_ShouldReturnExpectedValue()
    {
        // Arrange
        var strategy = new MockChainOfThoughtStrategy();
        var context = new AevatarAIContext
        {
            Question = "Explain quantum mechanics"
        };

        // Act
        var complexity = strategy.EstimateComplexity(context);

        // Assert
        complexity.ShouldBeGreaterThan(0);
        complexity.ShouldBeLessThanOrEqualTo(1);
        complexity.ShouldBe(0.6, 0.01); // Expected value for ChainOfThought
    }

    [Fact]
    public async Task ChainOfThought_ProcessAsync_ShouldGenerateMultipleThoughtSteps()
    {
        // Arrange
        var strategy = new MockChainOfThoughtStrategy();
        var thoughtSteps = new List<AevatarThoughtStepEvent>();

        var context = new AevatarAIContext
        {
            Question = "Why does ice float on water?"
        };

        var dependencies = CreateTestDependencies(
            onEventPublish: evt =>
            {
                if (evt is AevatarThoughtStepEvent step)
                    thoughtSteps.Add(step);
                return Task.CompletedTask;
            });

        // Act
        var result = await strategy.ProcessAsync(context, null, dependencies);

        // Assert
        result.ShouldNotBeEmpty();
        thoughtSteps.Count.ShouldBeGreaterThan(0);
        thoughtSteps[0].StepNumber.ShouldBe(1);
        thoughtSteps[0].ThoughtContent.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ChainOfThought_ProcessAsync_WithHighConfidenceConclusion_ShouldStopEarly()
    {
        // Arrange
        var strategy = new MockChainOfThoughtStrategy();
        strategy.SetHighConfidenceAfterSteps(2);

        var context = new AevatarAIContext
        {
            Question = "What is 2+2?"
        };

        var thoughtCount = 0;
        var dependencies = CreateTestDependencies(
            onEventPublish: evt =>
            {
                if (evt is AevatarThoughtStepEvent)
                    thoughtCount++;
                return Task.CompletedTask;
            });

        // Act
        var result = await strategy.ProcessAsync(context, null, dependencies);

        // Assert
        result.ShouldContain("4");
        thoughtCount.ShouldBe(2); // Should stop after 2 steps due to high confidence
    }

    [Fact]
    public async Task ChainOfThought_ProcessAsync_ReachingMaxSteps_ShouldSummarize()
    {
        // Arrange
        var strategy = new MockChainOfThoughtStrategy();
        var context = new AevatarAIContext
        {
            Question = "Complex philosophical question"
        };

        var config = new AevatarAIEventHandlerAttribute();
        var dependencies = CreateTestDependencies();
        dependencies.Configuration.MaxChainOfAevatarThoughtSteps = 3;

        // Act
        var result = await strategy.ProcessAsync(context, config, dependencies);

        // Assert
        result.ShouldNotBeEmpty();
        result.ShouldContain("Summary"); // Should contain summary after max steps
    }

    [Fact]
    public void ChainOfThought_ValidateRequirements_WithoutLLMProvider_ShouldFail()
    {
        // Arrange
        var strategy = new MockChainOfThoughtStrategy();
        var dependencies = new AevatarAIStrategyDependencies
        {
            LLMProvider = null,
            Configuration = new AevatarAIAgentConfiguration()
        };

        // Act
        var isValid = strategy.ValidateRequirements(dependencies);

        // Assert
        isValid.ShouldBeFalse();
    }

    [Fact]
    public void ReAct_CanHandle_WithToolRequiredQuestion_ShouldReturnTrue()
    {
        // Arrange
        var strategy = new MockReActStrategy();
        var context = new AevatarAIContext
        {
            Question = "Book a flight to Seattle"
        };
        context.Metadata["RequiresMultipleTools"] = "True";

        // Act
        var canHandle = strategy.CanHandle(context);

        // Assert
        canHandle.ShouldBeTrue();
    }

    [Fact]
    public async Task ReAct_ProcessAsync_ShouldAlternateThoughtAndAction()
    {
        // Arrange
        var strategy = new MockReActStrategy();
        var executionLog = new List<string>();

        var context = new AevatarAIContext
        {
            Question = "Find the weather and book a restaurant"
        };

        var dependencies = CreateTestDependencies(
            onToolExecute: async (name, args, cancellationToken) =>
            {
                executionLog.Add($"Action: {name}");
                await Task.CompletedTask;
                return $"Result for {name}";
            });

        strategy.OnThoughtGenerated = thought => executionLog.Add($"Thought: {thought}");
        strategy.OnObservationMade = obs => executionLog.Add($"Observation: {obs}");

        // Act
        await strategy.ProcessAsync(context, null, dependencies);

        // Assert
        executionLog.Count.ShouldBeGreaterThan(2);
        executionLog[0].ShouldStartWith("Thought:");
        executionLog.ShouldContain(log => log.StartsWith("Action:"));
        executionLog.ShouldContain(log => log.StartsWith("Observation:"));
    }

    [Fact]
    public async Task ReAct_ProcessAsync_ShouldExecuteToolsCorrectly()
    {
        // Arrange
        var strategy = new MockReActStrategy();
        var toolCalls = new List<(string name, Dictionary<string, object> args)>();

        var context = new AevatarAIContext
        {
            Question = "Calculate 5+3 and get weather for Seattle"
        };

        var dependencies = CreateTestDependencies(
            onToolExecute: async (name, args, cancellationToken) =>
            {
                toolCalls.Add((name, args));
                await Task.CompletedTask;
                return name == "calculator" ? 8 : "Sunny, 22°C";
            });

        // Act
        var result = await strategy.ProcessAsync(context, null, dependencies);

        // Assert
        result.ShouldNotBeEmpty();
        toolCalls.Count.ShouldBeGreaterThan(0);
        toolCalls.ShouldContain(tc => tc.name == "calculator" || tc.name == "weather");
    }

    [Fact]
    public async Task ReAct_ProcessAsync_WithToolFailure_ShouldHandleGracefully()
    {
        // Arrange
        var strategy = new MockReActStrategy();
        var context = new AevatarAIContext
        {
            Question = "Execute failing tool"
        };

        var dependencies = CreateTestDependencies(
            onToolExecute: (name, args, cancellationToken) =>
            {
                throw new Exception("Tool failed");
            });

        // Act
        var result = await strategy.ProcessAsync(context, null, dependencies);

        // Assert
        result.ShouldNotBeEmpty();
        result.ShouldNotContain("Exception"); // Should handle error gracefully
        result.ShouldContain("unable to complete"); // Should indicate failure appropriately
    }

    [Fact]
    public async Task ReAct_ProcessAsync_ReachingMaxIterations_ShouldStop()
    {
        // Arrange
        var strategy = new MockReActStrategy();
        strategy.SimulateEndlessLoop = true;

        var iterationCount = 0;
        var context = new AevatarAIContext
        {
            Question = "Endless task"
        };

        var dependencies = CreateTestDependencies(
            onToolExecute: async (name, args, cancellationToken) =>
            {
                iterationCount++;
                await Task.CompletedTask;
                return "continuing...";
            });

        dependencies.Configuration.MaxReActIterations = 3;

        // Act
        await strategy.ProcessAsync(context, null, dependencies);

        // Assert
        iterationCount.ShouldBeLessThanOrEqualTo(3);
    }

    [Fact]
    public async Task ReAct_IsTaskComplete_WithSufficientObservations_ShouldReturnTrue()
    {
        // Arrange
        var strategy = new MockReActStrategy();
        strategy.CompleteAfterObservations = 2;

        var observationCount = 0;
        var context = new AevatarAIContext
        {
            Question = "Multi-step task"
        };

        var dependencies = CreateTestDependencies(
            onToolExecute: async (name, args, cancellationToken) =>
            {
                observationCount++;
                await Task.CompletedTask;
                return $"Observation {observationCount}";
            });

        // Act
        var result = await strategy.ProcessAsync(context, null, dependencies);

        // Assert
        observationCount.ShouldBe(2);
        result.ShouldContain("Final answer");
    }

    #region Helper Methods

    private AevatarAIStrategyDependencies CreateTestDependencies(
        Func<IMessage, Task>? onEventPublish = null,
        Func<string, Dictionary<string, object>, CancellationToken, Task<object?>>? onToolExecute = null)
    {
        var mockLLMProvider = new MockLLMProvider();
        var mockToolManager = new MockToolManager();

        return new AevatarAIStrategyDependencies
        {
            AgentId = "test-agent",
            LLMProvider = mockLLMProvider,
            ToolManager = mockToolManager,
            Configuration = new AevatarAIAgentConfiguration
            {
                Model = "test-model",
                Temperature = 0.7,
                MaxChainOfAevatarThoughtSteps = 5,
                MaxReActIterations = 10
            },
            Logger = _mockLogger.Object,
            PublishEventCallback = onEventPublish,
            ExecuteToolCallback = onToolExecute
        };
    }

    #endregion
}

/// <summary>
/// Mock Chain of Thought strategy for testing
/// </summary>
public class MockChainOfThoughtStrategy : IAevatarAIProcessingStrategy
{
    private int _highConfidenceAfterSteps = int.MaxValue;

    public string Name => "Mock Chain of Thought";
    public string Description => "Test implementation of Chain of Thought";
    public AevatarAIProcessingMode Mode => AevatarAIProcessingMode.ChainOfThought;

    public void SetHighConfidenceAfterSteps(int steps)
    {
        _highConfidenceAfterSteps = steps;
    }

    public bool CanHandle(AevatarAIContext context)
    {
        var question = context.Question?.ToLower() ?? string.Empty;
        return question.Contains("why") || question.Contains("explain") ||
               question.Contains("how") || question.Contains("analyze");
    }

    public double EstimateComplexity(AevatarAIContext context)
    {
        return 0.6;
    }

    public bool ValidateRequirements(AevatarAIStrategyDependencies dependencies)
    {
        return dependencies?.LLMProvider != null && dependencies.Configuration != null;
    }

    public async Task<string> ProcessAsync(
        AevatarAIContext context,
        AevatarAIEventHandlerAttribute? config,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken = default)
    {
        var maxSteps = dependencies.Configuration.MaxChainOfAevatarThoughtSteps ?? 5;
        var thoughts = new List<string>();

        for (int i = 1; i <= maxSteps; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var thought = $"Thought step {i}: Analyzing {context.Question}";
            thoughts.Add(thought);

            if (dependencies.PublishEventCallback != null)
            {
                await dependencies.PublishEventCallback(new AevatarThoughtStepEvent
                {
                    AgentId = dependencies.AgentId,
                    ThoughtId = Guid.NewGuid().ToString(),
                    StepNumber = i,
                    ThoughtContent = thought,
                    Reasoning = $"Reasoning for step {i}",
                    Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
                });
            }

            if (i >= _highConfidenceAfterSteps)
            {
                return "High confidence answer: 4";
            }
        }

        return $"Summary: {string.Join(", ", thoughts)}";
    }
}

/// <summary>
/// Mock ReAct strategy for testing
/// </summary>
public class MockReActStrategy : IAevatarAIProcessingStrategy
{
    public bool SimulateEndlessLoop { get; set; }
    public int CompleteAfterObservations { get; set; } = int.MaxValue;
    public Action<string>? OnThoughtGenerated { get; set; }
    public Action<string>? OnObservationMade { get; set; }

    private int _observationCount = 0;

    public string Name => "Mock ReAct";
    public string Description => "Test implementation of ReAct";
    public AevatarAIProcessingMode Mode => AevatarAIProcessingMode.ReAct;

    public bool CanHandle(AevatarAIContext context)
    {
        return context.Metadata?.ContainsKey("RequiresMultipleTools") == true ||
               context.Question?.Contains("book", StringComparison.OrdinalIgnoreCase) == true ||
               context.Question?.Contains("find", StringComparison.OrdinalIgnoreCase) == true;
    }

    public double EstimateComplexity(AevatarAIContext context)
    {
        return 0.7;
    }

    public bool ValidateRequirements(AevatarAIStrategyDependencies dependencies)
    {
        return dependencies?.LLMProvider != null &&
               dependencies.Configuration != null &&
               dependencies.ToolManager != null;
    }

    public async Task<string> ProcessAsync(
        AevatarAIContext context,
        AevatarAIEventHandlerAttribute? config,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken = default)
    {
        var maxIterations = dependencies.Configuration.MaxReActIterations ?? 10;
        var observations = new List<string>();

        for (int i = 0; i < maxIterations; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Thought
            var thought = $"Thinking about: {context.Question}";
            OnThoughtGenerated?.Invoke(thought);

            // Action
            if (dependencies.ExecuteToolCallback != null && !SimulateEndlessLoop)
            {
                var toolName = i % 2 == 0 ? "calculator" : "weather";
                var args = new Dictionary<string, object> { ["param"] = $"value{i}" };

                try
                {
                    var result = await dependencies.ExecuteToolCallback(toolName, args, cancellationToken);

                    // Observation
                    var observation = $"Observed: {result}";
                    observations.Add(observation);
                    OnObservationMade?.Invoke(observation);
                    _observationCount++;

                    if (_observationCount >= CompleteAfterObservations)
                    {
                        return "Final answer: Task completed successfully";
                    }
                }
                catch (Exception)
                {
                    return $"Task was unable to complete due to errors";
                }
            }

            if (!SimulateEndlessLoop && i > 0)
            {
                break;
            }
        }

        return $"Result after {observations.Count} observations";
    }
}